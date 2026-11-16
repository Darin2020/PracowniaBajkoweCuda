# syntax=docker/dockerfile:1

# One image: ASP.NET Core serves the API, the SignalR hub and the prerendered Angular site (docs/PLAN.md §9).
#
#   docker build -t bajkowecuda .
#   docker build --build-arg RUN_TESTS=true --build-arg APP_VERSION=$(git rev-parse HEAD) -t bajkowecuda .

# ---------------------------------------------------------------------------------------------
# 1. Frontend: localized, prerendered Angular build (static files only, no Node at runtime).
# ---------------------------------------------------------------------------------------------
FROM node:26-alpine AS frontend
WORKDIR /src

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/frontend/ ./

# Public origin baked into canonical / hreflang / Open Graph URLs, robots.txt and sitemap.xml.
ARG PUBLIC_BASE_URL=https://example.com
ENV PUBLIC_BASE_URL=${PUBLIC_BASE_URL}
RUN npm run build

# ---------------------------------------------------------------------------------------------
# 2. Backend: restore (cached while the project files do not change), optional tests, publish.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig BajkoweCuda.sln ./
COPY src/backend/BajkoweCuda.Domain/BajkoweCuda.Domain.csproj src/backend/BajkoweCuda.Domain/
COPY src/backend/BajkoweCuda.Application/BajkoweCuda.Application.csproj src/backend/BajkoweCuda.Application/
COPY src/backend/BajkoweCuda.Infrastructure/BajkoweCuda.Infrastructure.csproj src/backend/BajkoweCuda.Infrastructure/
COPY src/backend/BajkoweCuda.Api/BajkoweCuda.Api.csproj src/backend/BajkoweCuda.Api/
COPY tests/Directory.Build.props tests/
COPY tests/BajkoweCuda.Domain.UnitTests/BajkoweCuda.Domain.UnitTests.csproj tests/BajkoweCuda.Domain.UnitTests/
COPY tests/BajkoweCuda.Application.UnitTests/BajkoweCuda.Application.UnitTests.csproj tests/BajkoweCuda.Application.UnitTests/
COPY tests/BajkoweCuda.Api.IntegrationTests/BajkoweCuda.Api.IntegrationTests.csproj tests/BajkoweCuda.Api.IntegrationTests/
RUN dotnet restore BajkoweCuda.sln

COPY src/backend/ src/backend/
COPY tests/ tests/

# CI runs the tests in its own job; pass RUN_TESTS=true to run them as part of the image build.
ARG RUN_TESTS=false
RUN if [ "$RUN_TESTS" = "true" ]; then \
      dotnet test --solution BajkoweCuda.sln --configuration Release --no-restore; \
    fi

RUN dotnet publish src/backend/BajkoweCuda.Api/BajkoweCuda.Api.csproj \
      --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

# ---------------------------------------------------------------------------------------------
# 3. Runtime: chiseled Ubuntu (no shell, no package manager), non-root, port 8080.
#    The "-extra" variant includes ICU, needed for the de/fr/es/pl cultures.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

COPY --from=build /app/publish ./
COPY --from=frontend /src/dist/frontend/browser ./wwwroot

ARG APP_VERSION=dev
ENV APP_VERSION=${APP_VERSION} \
    ASPNETCORE_HTTP_PORTS=8080 \
    # Lets the container run with a read-only root file system.
    DOTNET_EnableDiagnostics=0

LABEL org.opencontainers.image.title="Pracownia Bajkowe Cuda" \
      org.opencontainers.image.description="Custom toy-sewing service: ASP.NET Core API + prerendered Angular site" \
      org.opencontainers.image.source="https://github.com/Darin2020/PracowniaBajkoweCuda" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.revision="${APP_VERSION}"

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BajkoweCuda.Api.dll"]
