import { Component, LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LanguageSwitcher } from './language-switcher';
import { expectAccessible } from '../../testing/axe';

@Component({ template: '' })
class Blank {}

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LanguageSwitcher],
      providers: [
        provideRouter([{ path: 'privacy', component: Blank }]),
        { provide: LOCALE_ID, useValue: 'pl' },
      ],
    });
    document.cookie = 'lang=; Path=/; Max-Age=0';
  });

  async function render(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(LanguageSwitcher);
    fixture.detectChanges();

    return fixture;
  }

  const links = (element: HTMLElement) =>
    Array.from(element.querySelectorAll<HTMLAnchorElement>('a'));

  it('links to the landing page of every locale', async () => {
    const fixture = await render('/');

    expect(links(fixture.nativeElement).map((link) => link.getAttribute('href'))).toEqual([
      '/en/',
      '/de/',
      '/fr/',
      '/es/',
      '/pl/',
    ]);
  });

  it('keeps the current page and drops the query and fragment', async () => {
    const fixture = await render('/privacy?x=1#top');

    expect(links(fixture.nativeElement).map((link) => link.getAttribute('href'))).toEqual([
      '/en/privacy',
      '/de/privacy',
      '/fr/privacy',
      '/es/privacy',
      '/pl/privacy',
    ]);
  });

  it('marks the current locale and labels each link with the language name', async () => {
    const fixture = await render('/');
    const current = links(fixture.nativeElement).filter(
      (link) => link.getAttribute('aria-current') === 'true',
    );

    expect(current).toHaveLength(1);
    expect(current[0].getAttribute('hreflang')).toBe('pl');
    expect(current[0].querySelector('.visually-hidden')?.textContent).toBe('Polski');
  });

  it('remembers the chosen language in the lang cookie', async () => {
    const fixture = await render('/');
    const german = links(fixture.nativeElement).find(
      (link) => link.getAttribute('hreflang') === 'de',
    )!;
    german.addEventListener('click', (event) => event.preventDefault());

    german.click();

    expect(document.cookie).toContain('lang=de');
  });

  it('has no accessibility violations', async () => {
    const fixture = await render('/');
    await expectAccessible(fixture.nativeElement as HTMLElement);
  });
});
