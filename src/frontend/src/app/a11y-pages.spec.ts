import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { AccountPage } from './account/account-page';
import { TwoFactorSetupPage } from './admin/two-factor-setup-page';
import { CompleteProfilePage } from './auth/complete-profile-page';
import { ConfirmEmailPage } from './auth/confirm-email-page';
import { ForgotPasswordPage } from './auth/forgot-password-page';
import { RegisterPage } from './auth/register-page';
import { ResetPasswordPage } from './auth/reset-password-page';
import { AuthService } from './core/auth/auth.service';
import { ChatRealtime } from './core/chat/chat-realtime.service';
import { NotFoundPage } from './public/pages/not-found-page';
import { ANNA, provideApiTesting } from './testing/auth-testing';
import { expectAccessible } from './testing/axe';
import { FakeRealtime } from './testing/chat-testing';

/** What the pages ask the API for while they render. */
const ANSWERS: Record<string, unknown> = {
  '/api/auth/providers': ['google'],
  '/api/account/logins': {
    hasPassword: true,
    logins: [{ provider: 'google', displayName: 'Anna' }],
    available: ['google', 'facebook'],
  },
  '/api/auth/2fa/setup': {
    sharedKey: 'abcd efgh ijkl mnop',
    authenticatorUri: 'otpauth://totp/Pracownia:anna@example.test?secret=ABCDEFGHIJKLMNOP',
  },
  '/api/auth/complete-profile': {
    provider: 'google',
    email: 'anna@example.test',
    emailVerified: true,
    displayName: 'Anna',
  },
};

/** The pages that have no spec of their own, checked with axe as a signed-in customer (or admin). */
describe('accessibility of the other pages', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([], withComponentInputBinding()),
        { provide: ChatRealtime, useValue: new FakeRealtime() },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function check(page: Type<unknown>, me: object = { authenticated: true, ...ANNA }) {
    const loading = TestBed.inject(AuthService).ensureLoaded();
    http.expectOne('/api/auth/me').flush(me);
    await loading;

    const fixture = TestBed.createComponent(page);
    fixture.detectChanges();
    for (let round = 0; round < 3; round++) {
      await settle();
      for (const request of http.match(() => true)) {
        request.flush(ANSWERS[request.request.url] ?? null);
      }
      fixture.detectChanges();
    }

    const element = fixture.nativeElement as HTMLElement;
    // Something real was rendered: a heading and at least one control or link.
    expect(element.querySelector('h1')).not.toBeNull();
    expect(element.querySelectorAll('input, button, a').length).toBeGreaterThan(0);
    await expectAccessible(element);
  }

  const visitor = { authenticated: false };

  it('register', () => check(RegisterPage, visitor));
  it('forgot password', () => check(ForgotPasswordPage, visitor));
  it('reset password', () => check(ResetPasswordPage, visitor));
  it('complete profile', () => check(CompleteProfilePage, visitor));
  it('confirm email', () => check(ConfirmEmailPage));
  it('account', () => check(AccountPage));
  it('two-factor setup', () =>
    check(TwoFactorSetupPage, { authenticated: true, ...ANNA, roles: ['Admin'] }));
  it('not found', () => check(NotFoundPage, visitor));
});
