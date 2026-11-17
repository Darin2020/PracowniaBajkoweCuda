import { ApplicationConfig, mergeApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { PUBLIC_BASE_URL, PUBLIC_BASE_URL_PLACEHOLDER } from './core/public-base-url';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Prerendering runs at build time, before the public origin is known: the API fills it in.
    { provide: PUBLIC_BASE_URL, useValue: PUBLIC_BASE_URL_PLACEHOLDER },
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
