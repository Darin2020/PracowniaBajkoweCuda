#!/usr/bin/env bash
# Smoke test for a running instance of the application image.
#
#   scripts/smoke-test.sh [base-url]        default: http://localhost:8080
#   EXPECTED_VERSION=<git sha> scripts/smoke-test.sh https://...   also checks which build is running
#
# The base URL must be the public origin the instance is configured with (PublicBaseUrl):
# the canonical URLs are compared against it.
#
# Checks what a deployment must get right before anything else: the liveness and readiness probes, the language
# redirect, one prerendered page per locale (with content in the HTML itself), the client-side
# fallback, the API 404 and the security headers. Used by CI and, later, by the deployment pipeline.
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
BASE_URL="${BASE_URL%/}"
failures=0

pass() { printf '  ok    %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; failures=$((failures + 1)); }

# status <path> -> HTTP status code (redirects are not followed)
status() { curl --silent --output /dev/null --write-out '%{http_code}' "$BASE_URL$1"; }
body() { curl --silent "$BASE_URL$1"; }
headers() { curl --silent --output /dev/null --dump-header - "$BASE_URL$1" | tr -d '\r'; }

expect_status() {
  local path="$1" expected="$2" actual
  actual="$(status "$path")"
  if [ "$actual" = "$expected" ]; then pass "$path -> $expected"; else fail "$path -> $actual (expected $expected)"; fi
}

# The response is read completely before it is searched. Piping curl straight into `grep --quiet`
# is a race: grep exits at the first match, curl then fails with a broken pipe, and with `pipefail`
# that turns a successful match into a failure.
expect_contains() {
  local path="$1" needle="$2" content
  content="$(body "$path")"
  if grep --quiet --fixed-strings -- "$needle" <<<"$content"; then
    pass "$path contains \"$needle\""
  else
    fail "$path does not contain \"$needle\""
  fi
}

expect_not_contains() {
  local path="$1" needle="$2" content
  content="$(body "$path")"
  if grep --quiet --fixed-strings -- "$needle" <<<"$content"; then
    fail "$path contains \"$needle\""
  else
    pass "$path does not contain \"$needle\""
  fi
}

expect_header() {
  local path="$1" pattern="$2" content
  content="$(headers "$path")"
  if grep --quiet --ignore-case --extended-regexp -- "$pattern" <<<"$content"; then
    pass "$path has header matching /$pattern/"
  else
    fail "$path has no header matching /$pattern/"
  fi
}

echo "Smoke test against $BASE_URL"

echo "Health and version"
expect_status /health/live 200
expect_contains /health/live Healthy
# Readiness opens a database connection with the app's own credentials: in Azure this proves that the migration
# job created the managed identity's database user.
expect_status /health/ready 200
# The session endpoint issues the antiforgery cookie, which needs the Data Protection keys (Blob + Key Vault in Azure).
expect_contains /api/auth/me '"authenticated":false'
expect_header /api/auth/me '^set-cookie: XSRF-TOKEN='
expect_status /api/admin/audit 401
expect_status /api/chat 401
expect_status /api/admin/conversations 401
expect_status /hubs/chat 401
expect_status /api/version 200
if [ -n "${EXPECTED_VERSION:-}" ]; then
  expect_contains /api/version "\"version\":\"$EXPECTED_VERSION\""
fi

echo "Language redirect"
expect_status / 302
expect_header / '^location: /en/$'

echo "Prerendered landing page per locale (content must be in the HTML, not rendered by JavaScript)"
expect_contains /en/ '<html lang="en"'
expect_contains /en/ 'Toys with a story'
expect_contains /de/ '<html lang="de"'
expect_contains /de/ 'Spielzeug mit Geschichte'
expect_contains /fr/ '<html lang="fr"'
expect_contains /fr/ 'Des jouets qui ont une histoire'
expect_contains /es/ '<html lang="es"'
expect_contains /es/ 'Juguetes con historia'
expect_contains /pl/ '<html lang="pl"'
expect_contains /pl/ 'Zabawki z historią'

echo "SEO"
expect_contains /pl/ "<link rel=\"canonical\" href=\"$BASE_URL/pl/\""
expect_contains /pl/ 'hreflang="x-default"'
expect_contains /pl/ 'application/ld+json'
expect_contains /pl/privacy 'Polityka prywatności'
expect_status /robots.txt 200
expect_contains /robots.txt 'User-agent:'
expect_status /sitemap.xml 200
expect_contains /sitemap.xml "<loc>$BASE_URL/en/</loc>"

echo "The build-time placeholder origin must never reach a visitor"
expect_not_contains /pl/ 'public-base-url.invalid'
expect_not_contains /sitemap.xml 'public-base-url.invalid'
expect_not_contains /robots.txt 'public-base-url.invalid'

echo "Client-side fallback and API"
expect_status /de/chat 200
expect_contains /de/chat '<app-root'
expect_status /de/missing-file.js 404
expect_status /api/does-not-exist 404

echo "Headers"
expect_header /en/ "^content-security-policy: .*script-src 'self';"
expect_header /en/ '^x-content-type-options: nosniff$'
expect_header /en/ '^cache-control: no-cache$'

if [ "$failures" -gt 0 ]; then
  echo "Smoke test FAILED: $failures check(s)."
  exit 1
fi

echo "Smoke test passed."
