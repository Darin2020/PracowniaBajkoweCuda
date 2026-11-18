import { ViewportScroller } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  afterNextRender,
  inject,
} from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { ChatRealtime } from './core/chat/chat-realtime.service';
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

    // Who is signed in is asked only in the browser, after the (possibly prerendered) page has hydrated.
    const auth = inject(AuthService);
    // Connects to the chat hub whenever a user who may chat is signed in (and only in the browser).
    inject(ChatRealtime);
    afterNextRender(() => void auth.ensureLoaded());
  }
}
