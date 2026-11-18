import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ANNA, provideApiTesting } from '../testing/auth-testing';
import { TwoFactorLoginPage } from './two-factor-login-page';

@Component({ template: '' })
class Blank {}

describe('TwoFactorLoginPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [
            { path: 'login/2fa', component: TwoFactorLoginPage },
            { path: '**', component: Blank },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function submit(url: string, code: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create(url);
    const element = harness.routeNativeElement!;
    const input = element.querySelector<HTMLInputElement>('#two-factor-code')!;
    input.value = code;
    input.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    return element;
  }

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('strips the locale prefix of a return URL from an external sign-in', async () => {
    await submit('/login/2fa?returnUrl=%2Fpl%2Forders%2F7', '123456');
    http.expectOne('/api/auth/2fa/login').flush({ authenticated: true, ...ANNA });
    await settle();

    expect(TestBed.inject(Router).url).toBe('/orders/7');
  });

  it('asks for the password again when the first step has expired', async () => {
    const element = await submit('/login/2fa', '123456');
    http
      .expectOne('/api/auth/2fa/login')
      .flush({ code: 'auth.invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    await settle();

    expect(element.querySelector('[role=alert]')?.textContent).toContain('password again');
  });
});
