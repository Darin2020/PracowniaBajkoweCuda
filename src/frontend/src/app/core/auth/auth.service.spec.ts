import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ANNA, provideApiTesting } from '../../testing/auth-testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideApiTesting(), provideRouter([])] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks who is signed in only once', async () => {
    const first = auth.ensureLoaded();
    const second = auth.ensureLoaded();
    http.expectOne('/api/auth/me').flush({ authenticated: true, ...ANNA });

    expect(await first).toEqual(ANNA);
    expect(await second).toEqual(ANNA);
    expect(await auth.ensureLoaded()).toEqual(ANNA);
    expect(auth.signedIn()).toBe(true);
    expect(auth.loaded()).toBe(true);
  });

  it('treats a visitor as signed out', async () => {
    const loading = auth.ensureLoaded();
    http.expectOne('/api/auth/me').flush({ authenticated: false });

    expect(await loading).toBeNull();
    expect(auth.signedIn()).toBe(false);
  });

  it('treats a failed request as signed out', async () => {
    const loading = auth.ensureLoaded();
    http.expectOne('/api/auth/me').flush(null, { status: 503, statusText: 'Unavailable' });

    expect(await loading).toBeNull();
    expect(auth.loaded()).toBe(true);
  });

  it('signs in with a password', async () => {
    const login = auth.login('anna@example.test', 'a long enough password');
    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'anna@example.test',
      password: 'a long enough password',
    });
    request.flush({ authenticated: true, ...ANNA });

    expect(await login).toBe('signed-in');
    expect(auth.user()?.displayName).toBe('Anna');
  });

  it('reports that a second factor is needed', async () => {
    const login = auth.login('anna@example.test', 'a long enough password');
    http.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });

    expect(await login).toBe('two-factor');
    expect(auth.signedIn()).toBe(false);
  });

  it('turns a wrong password into an ApiError with the code', async () => {
    const login = auth.login('anna@example.test', 'wrong password!');
    http
      .expectOne('/api/auth/login')
      .flush({ code: 'auth.invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });

    await expect(login).rejects.toMatchObject({ status: 401, code: 'auth.invalid_credentials' });
  });

  it('uses the recovery endpoint for a recovery code', async () => {
    const login = auth.loginWithSecondFactor('ABCD-EFGH', true);
    http.expectOne('/api/auth/2fa/recovery-login').flush({ authenticated: true, ...ANNA });
    await login;

    expect(auth.signedIn()).toBe(true);
  });

  it('forgets the user when signing out, even if the request fails', async () => {
    const login = auth.login('anna@example.test', 'a long enough password');
    http.expectOne('/api/auth/login').flush({ authenticated: true, ...ANNA });
    await login;

    const logout = auth.logout();
    http.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Error' });
    await expect(logout).rejects.toBeTruthy();

    expect(auth.signedIn()).toBe(false);
  });

  it('builds the external sign-in address with the locale prefix on the return URL', () => {
    expect(auth.externalUrl('google', 'signin', '/chat')).toBe(
      '/api/auth/external/google?mode=signin&lang=pl&returnUrl=%2Fpl%2Fchat',
    );
    expect(auth.externalUrl('google', 'link', '//evil.test')).toBe(
      '/api/auth/external/google?mode=link&lang=pl',
    );
  });

  it('sends admins without 2FA to the setup, customers to the chat', () => {
    expect(auth.homeFor(ANNA)).toBe('/chat');
    expect(auth.homeFor({ ...ANNA, roles: ['Admin'] })).toBe('/admin/2fa-setup');
    expect(auth.homeFor({ ...ANNA, roles: ['Admin'], twoFactorEnabled: true })).toBe('/account');
  });
});
