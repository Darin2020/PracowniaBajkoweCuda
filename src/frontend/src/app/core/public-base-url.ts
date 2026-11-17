import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/**
 * Absolute origin of the public site without a trailing slash, for example `https://example.com`.
 * It is used for canonical URLs, `hreflang` alternates and Open Graph tags.
 *
 * In the browser it is the current origin. During prerendering there is no request and the origin
 * is not known yet (one image is promoted through every environment), so the server configuration
 * provides `PUBLIC_BASE_URL_PLACEHOLDER` and the API replaces it when it serves the files.
 */
export const PUBLIC_BASE_URL = new InjectionToken<string>('PUBLIC_BASE_URL', {
  factory: () => inject(DOCUMENT).location.origin,
});

/**
 * Stand-in origin written into prerendered pages, robots.txt and sitemap.xml at build time.
 * Keep in step with `PublicSiteOptions.OriginPlaceholder` in the API.
 */
export const PUBLIC_BASE_URL_PLACEHOLDER = 'https://public-base-url.invalid';
