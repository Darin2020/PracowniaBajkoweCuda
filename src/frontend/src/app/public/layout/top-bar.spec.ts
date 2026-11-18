import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ANNA, provideApiTesting } from '../../testing/auth-testing';
import { TopBar } from './top-bar';

@Component({ template: '' })
class Blank {}

describe('TopBar', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TopBar],
      providers: [provideApiTesting(), provideRouter([{ path: '**', component: Blank }])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(me: object) {
    const loading = TestBed.inject(AuthService).ensureLoaded();
    http.expectOne('/api/auth/me').flush(me);
    await loading;
    const fixture = TestBed.createComponent(TopBar);
    fixture.detectChanges();
    return fixture;
  }

  it('offers "Sign in" to visitors', async () => {
    const fixture = await render({ authenticated: false });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.sign-in')?.getAttribute('href')).toBe('/pl/login');
    expect(element.querySelector('.user-menu')).toBeNull();
  });

  it('shows the signed-in user with a menu', async () => {
    const fixture = await render({ authenticated: true, ...ANNA });
    const element = fixture.nativeElement as HTMLElement;
    const toggle = element.querySelector<HTMLButtonElement>('.user-menu__toggle')!;

    expect(toggle.textContent).toContain('Anna');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const links = Array.from(element.querySelectorAll('#user-menu a')).map((link) =>
      link.getAttribute('href'),
    );
    expect(links).toEqual(['/pl/chat', '/pl/account']);
  });

  it('gives admins the conversations instead of the chat', async () => {
    const fixture = await render({
      authenticated: true,
      ...ANNA,
      roles: ['Admin'],
      unreadCount: 4,
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('a[href="/pl/admin/conversations"]')?.textContent).toContain('4');
    expect(element.querySelector('a[href="/pl/chat"]')).toBeNull();
  });

  it('reminds an admin without 2FA to set it up', async () => {
    const fixture = await render({ authenticated: true, ...ANNA, roles: ['Admin'] });

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/pl/admin/2fa-setup"]'),
    ).not.toBeNull();
  });

  it('signs out and goes home', async () => {
    const fixture = await render({ authenticated: true, ...ANNA });
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.user-menu__toggle')!.click();
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>('.nav-link--button')!.click();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/');
    expect(element.querySelector('.sign-in')).not.toBeNull();
  });
});
