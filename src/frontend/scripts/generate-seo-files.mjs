// Writes robots.txt and sitemap.xml into the built site (dist/frontend/browser), after `ng build`.
//
// The public origin comes from the PUBLIC_BASE_URL environment variable (default: https://example.com),
// the same variable the prerenderer uses for canonical and hreflang URLs.
import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const LOCALES = ['en', 'de', 'fr', 'es', 'pl'];
const DEFAULT_LOCALE = 'en';
/** Prerendered public pages, as paths inside a locale. Keep in step with app.routes.server.ts. */
const PUBLIC_PAGES = ['', 'privacy', 'terms', 'cookies'];
/** Client-rendered routes that search engines must not crawl (docs/PLAN.md §4.4). */
const PRIVATE_ROUTES = [
  'login',
  'register',
  'forgot-password',
  'reset-password',
  'confirm-email',
  'complete-profile',
  'chat',
  'orders',
  'account',
  'admin',
];

const baseUrl =
  (process.env.PUBLIC_BASE_URL ?? '').trim().replace(/\/+$/, '') || 'https://example.com';
const outDir = join(import.meta.dirname, '..', 'dist', 'frontend', 'browser');

if (!existsSync(outDir)) {
  console.error(`Build output not found: ${outDir}. Run "ng build" first.`);
  process.exit(1);
}

for (const locale of LOCALES) {
  for (const page of PUBLIC_PAGES) {
    const file = join(outDir, locale, page, 'index.html');

    if (!existsSync(file)) {
      console.error(`Expected prerendered page is missing: ${file}`);
      process.exit(1);
    }
  }
}

const urlOf = (locale, page) => `${baseUrl}/${locale}/${page}`;

const robots = [
  'User-agent: *',
  'Disallow: /api/',
  'Disallow: /hubs/',
  ...LOCALES.flatMap((locale) => PRIVATE_ROUTES.map((route) => `Disallow: /${locale}/${route}`)),
  '',
  `Sitemap: ${baseUrl}/sitemap.xml`,
  '',
].join('\n');

const urls = LOCALES.flatMap((locale) =>
  PUBLIC_PAGES.map((page) =>
    [
      '  <url>',
      `    <loc>${urlOf(locale, page)}</loc>`,
      ...LOCALES.map(
        (alternate) =>
          `    <xhtml:link rel="alternate" hreflang="${alternate}" href="${urlOf(alternate, page)}"/>`,
      ),
      `    <xhtml:link rel="alternate" hreflang="x-default" href="${urlOf(DEFAULT_LOCALE, page)}"/>`,
      '  </url>',
    ].join('\n'),
  ),
);

const sitemap = [
  '<?xml version="1.0" encoding="UTF-8"?>',
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">',
  ...urls,
  '</urlset>',
  '',
].join('\n');

writeFileSync(join(outDir, 'robots.txt'), robots);
writeFileSync(join(outDir, 'sitemap.xml'), sitemap);

console.log(`robots.txt and sitemap.xml written for ${baseUrl} (${urls.length} URLs).`);
