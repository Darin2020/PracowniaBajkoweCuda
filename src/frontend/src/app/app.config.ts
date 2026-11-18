import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import {
  provideClientHydration,
  withI18nSupport,
  withNoIncrementalHydration,
} from '@angular/platform-browser';
import {
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
  withRouterConfig,
} from '@angular/router';
import { routes } from './app.routes';
import { apiErrorInterceptor } from './core/api/api-error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Same origin, relative URLs only (§4.3). The API's antiforgery token travels from the XSRF-TOKEN cookie into the
    // X-XSRF-TOKEN header of every POST, PUT, PATCH and DELETE.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([apiErrorInterceptor]),
    ),
    provideRouter(
      routes,
      // Route data (for example the legal document id) is bound to component inputs.
      withComponentInputBinding(),
      // Menu items are fragment links (`/#gallery`): the router scrolls to the section.
      withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' }),
      // Lets a menu item scroll back to its section when it is clicked a second time.
      withRouterConfig({ onSameUrlNavigation: 'reload' }),
    ),
    // Incremental hydration is off because it brings event replay with it, and event replay needs
    // inline scripts, which the Content-Security-Policy (`script-src 'self'`) forbids.
    provideClientHydration(withI18nSupport(), withNoIncrementalHydration()),
  ],
};
