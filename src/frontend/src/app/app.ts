import { ViewportScroller } from '@angular/common';
import { ChangeDetectionStrategy, Component, DOCUMENT, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { CookieNotice } from './public/layout/cookie-notice';
import { Footer } from './public/layout/footer';
import { TopBar } from './public/layout/top-bar';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CookieNotice, Footer, RouterLink, RouterOutlet, TopBar],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  constructor() {
    const document = inject(DOCUMENT);

    // Menu links scroll to a section: stop below the sticky top bar, not underneath it.
    inject(ViewportScroller).setOffset(() => [
      0,
      document.querySelector<HTMLElement>('app-top-bar')?.offsetHeight ?? 0,
    ]);
  }
}
