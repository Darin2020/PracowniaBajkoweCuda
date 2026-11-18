import { APP_BASE_HREF } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, LOCALE_ID, Provider } from '@angular/core';
import { apiErrorInterceptor } from '../core/api/api-error.interceptor';
import { SessionUser } from '../core/auth/auth.models';

/** A signed-in customer for tests. */
export const ANNA: SessionUser = {
  id: '6c1b0e6e-3a0f-4c5e-9d7c-2c4e5b8f9a01',
  email: 'anna@example.test',
  displayName: 'Anna',
  emailConfirmed: true,
  preferredLanguage: 'pl',
  roles: [],
  twoFactorEnabled: false,
  unreadCount: 0,
};

/** HttpClient with the app's interceptor, answered by HttpTestingController, in the Polish build. */
export function provideApiTesting(): (Provider | EnvironmentProviders)[] {
  return [
    provideHttpClient(withInterceptors([apiErrorInterceptor])),
    provideHttpClientTesting(),
    { provide: LOCALE_ID, useValue: 'pl' },
    { provide: APP_BASE_HREF, useValue: '/pl/' },
  ];
}
