import { Location, isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Injectable, LOCALE_ID, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toAppLocale } from '../locales';
import {
  ADMIN_ROLE,
  CompleteProfileRequest,
  ExternalMode,
  LoginResult,
  MeResponse,
  PendingProfile,
  RecoveryCodes,
  RegisterRequest,
  SessionUser,
  TwoFactorRequiredResponse,
  TwoFactorSetup,
} from './auth.models';
import { safeReturnUrl } from './return-url';

/**
 * The session of the browser (docs/PLAN.md §4.3): who is signed in, as signals, and the calls of `/api/auth`.
 * The session itself is an HttpOnly cookie that the app never sees; `GET /api/auth/me` tells who it belongs to and
 * also issues the XSRF cookie that every later POST needs. Prerendered pages never call it (§4.4): on the server the
 * user is always "not signed in" and the browser loads the real state after hydration.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly location = inject(Location);
  private readonly locale = toAppLocale(inject(LOCALE_ID));
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly current = signal<SessionUser | null>(null);
  private readonly ready = signal(false);
  private pending: Promise<SessionUser | null> | undefined;

  /** The signed-in user, or `null`. */
  readonly user = this.current.asReadonly();
  /** `true` once `GET /api/auth/me` has answered (until then the user is unknown, not "signed out"). */
  readonly loaded = this.ready.asReadonly();
  readonly signedIn = computed(() => this.current() !== null);
  readonly isAdmin = computed(() => this.current()?.roles.includes(ADMIN_ROLE) ?? false);

  /** The session, loading it on first use. Guards wait for this. */
  ensureLoaded(): Promise<SessionUser | null> {
    if (this.ready()) {
      return Promise.resolve(this.current());
    }

    return (this.pending ??= this.refresh());
  }

  /** Asks the API again who is signed in (after confirming the email, for example). */
  async refresh(): Promise<SessionUser | null> {
    if (!this.browser) {
      return null;
    }

    try {
      this.apply(await firstValueFrom(this.http.get<MeResponse>('/api/auth/me')));
    } catch {
      // No answer: treat the visitor as signed out; the next call that needs a session will say so.
      this.apply({ authenticated: false });
    } finally {
      this.pending = undefined;
    }

    return this.current();
  }

  /** The chat badge changed (pushed by the server, or the user read the messages). */
  setUnreadCount(count: number): void {
    this.current.update((user) => (user ? { ...user, unreadCount: Math.max(0, count) } : user));
  }

  /** The customer's first order was confirmed (pushed by the server): show the "My orders" tab. */
  setHasOrders(): void {
    this.current.update((user) => (user && !user.hasOrders ? { ...user, hasOrders: true } : user));
  }

  /** Forgets the user without a request, when the API has said the session is gone (401). */
  clear(): void {
    this.apply({ authenticated: false });
  }

  async login(email: string, password: string): Promise<LoginResult> {
    const response = await firstValueFrom(
      this.http.post<MeResponse | TwoFactorRequiredResponse>('/api/auth/login', {
        email,
        password,
      }),
    );

    if ('requiresTwoFactor' in response) {
      return 'two-factor';
    }

    this.apply(response);
    return 'signed-in';
  }

  /** The second step of a sign-in with 2FA: a code of the authenticator app, or a recovery code. */
  async loginWithSecondFactor(code: string, recoveryCode: boolean): Promise<void> {
    const endpoint = recoveryCode ? '/api/auth/2fa/recovery-login' : '/api/auth/2fa/login';
    this.apply(await firstValueFrom(this.http.post<MeResponse>(endpoint, { code })));
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.clear();
    }
  }

  /** Answers 202 whether or not the address already has an account (no user enumeration, §8.1). */
  async register(request: RegisterRequest): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/register', request));
  }

  async confirmEmail(userId: string, code: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/confirm-email', { userId, code }));
    if (this.current()) {
      await this.refresh();
    }
  }

  async resendConfirmation(email: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/resend-confirmation', { email }));
  }

  async forgotPassword(email: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/forgot-password', { email }));
  }

  async resetPassword(userId: string, code: string, password: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/reset-password', { userId, code, password }));
  }

  /** The external providers configured in this environment, for example `["google"]`. */
  providers(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/auth/providers'));
  }

  pendingProfile(): Promise<PendingProfile> {
    return firstValueFrom(this.http.get<PendingProfile>('/api/auth/complete-profile'));
  }

  async completeProfile(request: CompleteProfileRequest): Promise<void> {
    this.apply(
      await firstValueFrom(this.http.post<MeResponse>('/api/auth/complete-profile', request)),
    );
  }

  twoFactorSetup(): Promise<TwoFactorSetup> {
    return firstValueFrom(this.http.get<TwoFactorSetup>('/api/auth/2fa/setup'));
  }

  /** Turns 2FA on; the session gets the second factor at once. Returns the recovery codes (shown only once). */
  async enableTwoFactor(code: string): Promise<readonly string[]> {
    const response = await firstValueFrom(
      this.http.post<RecoveryCodes>('/api/auth/2fa/enable', { code }),
    );
    await this.refresh();
    return response.recoveryCodes;
  }

  async newRecoveryCodes(): Promise<readonly string[]> {
    return (await firstValueFrom(this.http.post<RecoveryCodes>('/api/auth/2fa/recovery-codes', {})))
      .recoveryCodes;
  }

  /**
   * The address of an external sign-in. It is a full-page navigation (the provider's page is not part of the app),
   * so the return URL gets the locale prefix: "/chat" becomes "/pl/chat".
   */
  externalUrl(provider: string, mode: ExternalMode, returnUrl?: string | null): string {
    const parameters = new URLSearchParams({ mode, lang: this.locale });
    const target = safeReturnUrl(returnUrl);
    if (target) {
      parameters.set('returnUrl', this.location.prepareExternalUrl(target));
    }

    return `/api/auth/external/${encodeURIComponent(provider)}?${parameters.toString()}`;
  }

  /** Where to go after signing in when no return URL was given. */
  homeFor(user: SessionUser | null): string {
    if (user?.roles.includes(ADMIN_ROLE) && !user.twoFactorEnabled) {
      return '/admin/2fa-setup';
    }

    return user?.roles.includes(ADMIN_ROLE) ? '/admin/conversations' : '/chat';
  }

  private apply(response: MeResponse): void {
    if (response.authenticated) {
      this.current.set({
        id: response.id,
        email: response.email,
        displayName: response.displayName,
        emailConfirmed: response.emailConfirmed,
        preferredLanguage: response.preferredLanguage,
        roles: response.roles ?? [],
        twoFactorEnabled: response.twoFactorEnabled,
        unreadCount: response.unreadCount ?? 0,
        hasOrders: response.hasOrders ?? false,
      });
    } else {
      this.current.set(null);
    }

    this.ready.set(true);
  }
}
