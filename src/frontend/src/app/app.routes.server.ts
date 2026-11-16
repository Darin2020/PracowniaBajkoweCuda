import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Public pages are prerendered to static HTML per locale (they must not need a server or the database).
 * Everything else is client-rendered from the CSR shell (`index.csr.html`).
 */
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },
  { path: 'privacy', renderMode: RenderMode.Prerender },
  { path: 'terms', renderMode: RenderMode.Prerender },
  { path: 'cookies', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Client },
];
