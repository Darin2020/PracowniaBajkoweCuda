import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/**
 * Absolute origin of the public site without a trailing slash, for example `https://example.com`.
 * It is used for canonical URLs, `hreflang` alternates and Open Graph tags.
 *
 * In the browser it is the current origin. During prerendering there is no request, so the server
 * configuration overrides this token with the `PUBLIC_BASE_URL` build-time environment variable.
 */
export const PUBLIC_BASE_URL = new InjectionToken<string>('PUBLIC_BASE_URL', {
  factory: () => inject(DOCUMENT).location.origin,
});

export const FALLBACK_PUBLIC_BASE_URL = 'https://example.com';

export function normalizeBaseUrl(value: string | undefined): string {
  const trimmed = (value ?? '').trim().replace(/\/+$/, '');

  return trimmed.length > 0 ? trimmed : FALLBACK_PUBLIC_BASE_URL;
}
