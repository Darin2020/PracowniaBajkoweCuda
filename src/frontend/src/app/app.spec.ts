import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  function render(): HTMLElement {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('renders the top bar, the main landmark and the footer', () => {
    const element = render();

    expect(element.querySelector('app-top-bar header')).not.toBeNull();
    expect(element.querySelector('main#main')).not.toBeNull();
    expect(element.querySelector('app-footer footer')).not.toBeNull();
  });

  it('offers a skip link as the first focusable element', () => {
    const first = render().querySelector('a, button');

    expect(first?.classList.contains('skip-link')).toBe(true);
  });

  it('links every landing section from the menu', () => {
    const fragments = Array.from(render().querySelectorAll('app-top-bar .nav-link')).map(
      (link) => link.getAttribute('href')?.split('#')[1],
    );

    expect(fragments).toEqual(['about', 'gallery', 'pricing', 'contact']);
  });

  it('opens and closes the menu with the hamburger button', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const toggle = element.querySelector<HTMLButtonElement>('.menu-toggle')!;

    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector('.menu')?.classList.contains('menu--open')).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
  });
});
