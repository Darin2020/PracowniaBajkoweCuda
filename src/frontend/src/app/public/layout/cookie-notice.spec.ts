import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { COOKIE_NOTICE_STORAGE_KEY, CookieNotice } from './cookie-notice';

describe('CookieNotice', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [CookieNotice], providers: [provideRouter([])] });
  });

  async function render() {
    const fixture = TestBed.createComponent(CookieNotice);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture;
  }

  it('is shown on the first visit with a link to the cookie information', async () => {
    const element = (await render()).nativeElement as HTMLElement;

    expect(element.querySelector('aside')).not.toBeNull();
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/cookies');
  });

  it('disappears and stays dismissed after "Got it"', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector('button')!.click();
    fixture.detectChanges();

    expect(element.querySelector('aside')).toBeNull();
    expect(localStorage.getItem(COOKIE_NOTICE_STORAGE_KEY)).toBe('1');
  });

  it('is not shown again once dismissed', async () => {
    localStorage.setItem(COOKIE_NOTICE_STORAGE_KEY, '1');

    const element = (await render()).nativeElement as HTMLElement;

    expect(element.querySelector('aside')).toBeNull();
  });
});
