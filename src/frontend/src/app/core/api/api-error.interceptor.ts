import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { safeReturnUrl } from '../auth/return-url';
import { toApiError } from './api-error';

/**
 * Turns every failed API call into an `ApiError` with the problem's `code` (docs/PLAN.md §4.3), and reacts to the two
 * answers that are about the session rather than the request:
 * - 401 `auth.not_authenticated` (the session ended): forget the user and go to the sign-in page, then come back;
 * - 403 `auth.mfa_required` (an admin without 2FA): go to the 2FA setup.
 * The `/api/auth/*` calls handle their own answers (a wrong password is also a 401).
 */
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);

  return next(request).pipe(
    catchError((response: unknown) => {
      const error = toApiError(response);

      if (!request.url.startsWith('/api/auth/')) {
        if (error.status === 401 && error.code === 'auth.not_authenticated') {
          auth.clear();
          const returnUrl = safeReturnUrl(router.url);
          void router.navigate(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
        } else if (error.status === 403 && error.code === 'auth.mfa_required') {
          void router.navigate(['/admin/2fa-setup']);
        }
      }

      return throwError(() => error);
    }),
  );
};
