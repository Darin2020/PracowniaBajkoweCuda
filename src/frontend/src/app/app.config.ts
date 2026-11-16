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

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
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
