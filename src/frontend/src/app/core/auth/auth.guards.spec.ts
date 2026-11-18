import { HttpClient } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ANNA, provideApiTesting } from '../../testing/auth-testing';
import { adminGuard, anonymousGuard, authGuard } from './auth.guards';

@Component({ template: '' })
class Blank {}

describe('auth guards and the API error interceptor', () => {
  let router: Router;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([
          { path: '', component: Blank },
          { path: 'login', component: Blank, canActivate: [anonymousGuard] },
          { path: 'chat', component: Blank, canActivate: [authGuard] },
          { path: 'account', component: Blank, canActivate: [authGuard] },
          { path: 'orders', component: Blank },
          { path: 'admin/2fa-setup', component: Blank, canActivate: [adminGuard] },
        ]),
      ],
    });
    router = TestBed.inject(Router);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function navigate(url: string, me: object): Promise<string> {
    const navigation = router.navigateByUrl(url);
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('/api/auth/me').flush(me);
    await navigation;
    return router.url;
  }

  it('sends a visitor to the sign-in page and back', async () => {
    expect(await navigate('/chat', { authenticated: false })).toBe('/login?returnUrl=%2Fchat');
  });

  it('lets a signed-in user in', async () => {
    expect(await navigate('/chat', { authenticated: true, ...ANNA })).toBe('/chat');
  });

  it('keeps a signed-in user away from the sign-in page', async () => {
    expect(await navigate('/login', { authenticated: true, ...ANNA })).toBe('/chat');
  });

  it('keeps customers out of the admin pages', async () => {
    expect(await navigate('/admin/2fa-setup', { authenticated: true, ...ANNA })).toBe('/account');
  });

  it('lets an admin reach the 2FA setup', async () => {
    const admin = { authenticated: true, ...ANNA, roles: ['Admin'] };

    expect(await navigate('/admin/2fa-setup', admin)).toBe('/admin/2fa-setup');
  });

  it('goes to the sign-in page when the session has ended', async () => {
    await router.navigateByUrl('/orders');
    const request = firstValueFrom(TestBed.inject(HttpClient).get('/api/orders'));
    http
      .expectOne('/api/orders')
      .flush({ code: 'auth.not_authenticated' }, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toMatchObject({ code: 'auth.not_authenticated' });
    // The interceptor has forgotten the user, so the sign-in page opens without asking the API again.
    await new Promise((resolve) => setTimeout(resolve));

    expect(router.url).toBe('/login?returnUrl=%2Forders');
  });

  it('leaves a wrong password to the sign-in page', async () => {
    await router.navigateByUrl('/orders');
    const request = firstValueFrom(TestBed.inject(HttpClient).post('/api/auth/login', {}));
    http
      .expectOne('/api/auth/login')
      .flush({ code: 'auth.invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toMatchObject({ code: 'auth.invalid_credentials' });
    expect(router.url).toBe('/orders');
  });
});
