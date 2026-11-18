import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ResendConfirmation } from '../auth/resend-confirmation';
import { SITE } from '../content/site';
import { AuthService } from '../core/auth/auth.service';
import { Seo } from '../core/seo';

/**
 * `/chat` until the chat itself arrives (docs/PLAN.md, Phase 4). It already needs a signed-in user with a confirmed
 * email address, like the chat will.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ResendConfirmation, RouterLink],
  selector: 'app-chat-placeholder-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>
      @if (auth.user(); as user) {
        @if (user.emailConfirmed) {
          <p i18n="@@chat.soon">
            The chat opens very soon. Until then, please write to us by email and we will answer
            from there.
          </p>
          <p>
            <a [href]="'mailto:' + email">{{ email }}</a>
          </p>
        } @else {
          <p class="alert" i18n="@@chat.confirmFirst">
            Please confirm your email address first: open the link we sent to {{ user.email }}.
          </p>
          <app-resend-confirmation [email]="user.email" />
        }
      }
      <p><a routerLink="/account" i18n="@@chat.toAccount">Your account</a></p>
    </section>
  `,
})
export class ChatPlaceholderPage {
  protected readonly auth = inject(AuthService);
  protected readonly title = $localize`:@@chat.title:Chat`;
  protected readonly email = SITE.email;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }
}
