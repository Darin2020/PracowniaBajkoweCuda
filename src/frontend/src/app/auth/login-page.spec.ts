import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ANNA, provideApiTesting } from '../testing/auth-testing';
import { LoginPage } from './login-page';

@Component({ template: '' })
class Blank {}

describe('LoginPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [
            { path: 'login', component: LoginPage },
            { path: 'login/2fa', component: Blank },
            { path: '**', component: Blank },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(url: string) {
    const harness = await RouterTestingHarness.create(url);
    http.expectOne('/api/auth/providers').flush(['google']);
    await new Promise((resolve) => setTimeout(resolve));
    harness.detectChanges();
    return harness;
  }

  function fill(element: HTMLElement, email: string, password: string): void {
    const set = (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('#login-email', email);
    set('#login-password', password);
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
  }

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('signs in and goes to the return URL', async () => {
    const harness = await open('/login?returnUrl=%2Forders%2F7');
    fill(harness.routeNativeElement!, 'anna@example.test', 'a long enough password');
    http.expectOne('/api/auth/login').flush({ authenticated: true, ...ANNA });
    await settle();

    expect(TestBed.inject(Router).url).toBe('/orders/7');
  });

  it('continues with the second factor', async () => {
    const harness = await open('/login?returnUrl=%2Fchat');
    fill(harness.routeNativeElement!, 'anna@example.test', 'a long enough password');
    http.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });
    await settle();

    expect(TestBed.inject(Router).url).toBe('/login/2fa?returnUrl=%2Fchat');
  });

  it('shows the translated error of a wrong password', async () => {
    const harness = await open('/login');
    fill(harness.routeNativeElement!, 'anna@example.test', 'wrong password!');
    http
      .expectOne('/api/auth/login')
      .flush({ code: 'auth.invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    harness.detectChanges();

    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      'password is not correct',
    );
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('does not send an empty form', async () => {
    const harness = await open('/login');
    fill(harness.routeNativeElement!, '', '');
    harness.detectChanges();

    expect(harness.routeNativeElement!.querySelector('#login-email-error')?.textContent).toContain(
      'fill in',
    );
  });

  it('offers the configured providers with the return URL', async () => {
    const harness = await open('/login?returnUrl=%2Fchat');

    expect(
      harness
        .routeNativeElement!.querySelector('a[href^="/api/auth/external/"]')
        ?.getAttribute('href'),
    ).toBe('/api/auth/external/google?mode=signin&lang=pl&returnUrl=%2Fpl%2Fchat');
  });

  it('returns to the home page after a provider sign-in without a return URL', async () => {
    const harness = await open('/login');

    expect(
      harness
        .routeNativeElement!.querySelector('a[href^="/api/auth/external/"]')
        ?.getAttribute('href'),
    ).toBe('/api/auth/external/google?mode=signin&lang=pl&returnUrl=%2Fpl%2Fsigned-in');
  });

  it('explains an error code from a redirect of the API', async () => {
    const harness = await open('/login?error=account_exists');

    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      'already exists',
    );
  });
});
