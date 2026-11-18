import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Seo } from '../../core/seo';

/** Shown after "Delete account" (docs/PLAN.md §8.6); the confirmation email is on its way. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-account-deleted-page',
  styleUrl: './message-page.scss',
  template: `
    <section class="message container">
      <h1>{{ title }}</h1>
      <p i18n="@@accountDeleted.text">
        Your account and your personal data have been deleted. We have sent a confirmation to your
        email address. Thank you for the time with us.
      </p>
      <a class="button" routerLink="/" i18n="@@message.backHome">Back to the home page</a>
    </section>
  `,
})
export class AccountDeletedPage {
  protected readonly title = $localize`:@@accountDeleted.title:Your account has been deleted`;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }
}
