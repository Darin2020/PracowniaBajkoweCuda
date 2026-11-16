import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

export const COOKIE_NOTICE_STORAGE_KEY = 'cookie-notice-dismissed';

/**
 * Information notice, not a consent banner: the site sets only strictly necessary cookies, so there
 * is nothing to consent to (docs/PLAN.md §3.7). It is shown in the browser only, after hydration.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-cookie-notice',
  styleUrl: './cookie-notice.scss',
  template: `
    @if (visible()) {
      <aside class="notice" aria-label="Cookies" i18n-aria-label="@@cookieNotice.label">
        <p class="notice__text">
          <ng-container i18n="@@cookieNotice.text"
            >This site uses only the cookies it needs to work: sign-in, security and your language
            choice.</ng-container
          >&ngsp;<a routerLink="/cookies" i18n="@@cookieNotice.more">Cookie information</a>
        </p>
        <button type="button" class="button" (click)="dismiss()" i18n="@@cookieNotice.dismiss">
          Got it
        </button>
      </aside>
    }
  `,
})
export class CookieNotice {
  private readonly storage = inject(DOCUMENT).defaultView?.localStorage;

  protected readonly visible = signal(false);

  constructor() {
    afterNextRender(() => this.visible.set(!this.wasDismissed()));
  }

  protected dismiss(): void {
    this.visible.set(false);

    try {
      this.storage?.setItem(COOKIE_NOTICE_STORAGE_KEY, '1');
    } catch {
      // Storage can be blocked (private mode). The notice then reappears on the next visit.
    }
  }

  private wasDismissed(): boolean {
    try {
      return this.storage?.getItem(COOKIE_NOTICE_STORAGE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
