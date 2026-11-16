import { ApplicationConfig, mergeApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { PUBLIC_BASE_URL, normalizeBaseUrl } from './core/public-base-url';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Prerendering runs at build time, so the public origin comes from the build environment.
    { provide: PUBLIC_BASE_URL, useValue: normalizeBaseUrl(process.env['PUBLIC_BASE_URL']) },
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
