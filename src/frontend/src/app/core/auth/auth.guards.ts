import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ADMIN_ROLE } from './auth.models';
import { AuthService } from './auth.service';
import { safeReturnUrl } from './return-url';

// Route guards (docs/PLAN.md §4.4). They only steer the browser; the API checks every request on its own.

/** Signed-in users only; others sign in first and come back. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  if (await inject(AuthService).ensureLoaded()) {
    return true;
  }

  const returnUrl = safeReturnUrl(state.url);
  return router.createUrlTree(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
};

/** Admins only (the 2FA setup page; the other admin pages also need the second factor, which the API enforces). */
export const adminGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const user = await inject(AuthService).ensureLoaded();
  if (!user) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  return user.roles.includes(ADMIN_ROLE) ? true : router.createUrlTree(['/account']);
};

/** Sign-in and registration pages: a signed-in user goes to their home page instead. */
export const anonymousGuard: CanActivateFn = async () => {
  // inject() works only before the first await.
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = await auth.ensureLoaded();
  return user ? router.parseUrl(auth.homeFor(user)) : true;
};

/**
 * `/signed-in`: where an external sign-in returns when no return URL was given. The API only redirects the browser;
 * this sends the user on to their home page (the chat, or the 2FA setup for an admin without it).
 */
export const homeGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = await auth.ensureLoaded();
  return router.parseUrl(user ? auth.homeFor(user) : '/login');
};

/** Pages open to everyone that send a form: they need the XSRF cookie, which `GET /api/auth/me` issues. */
export const sessionGuard: CanActivateFn = async () => {
  await inject(AuthService).ensureLoaded();
  return true;
};
