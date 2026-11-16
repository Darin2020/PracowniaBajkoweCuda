import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SITE } from '../../content/site';
import { Seo } from '../../core/seo';

/** Stand-in for sign-in and the chat until accounts exist (docs/PLAN.md, Phase 3). */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-coming-soon-page',
  styleUrl: './message-page.scss',
  template: `
    <section class="message container">
      <h1>{{ title }}</h1>
      <p i18n="@@comingSoon.text">
        Sign-in and the chat are not open yet. Until then, please write to us by email.
      </p>
      <p>
        <a [href]="'mailto:' + email">{{ email }}</a>
      </p>
      <a class="button" routerLink="/" i18n="@@message.backHome">Back to the home page</a>
    </section>
  `,
})
export class ComingSoonPage {
  protected readonly title = $localize`:@@comingSoon.title:Accounts are coming soon`;
  protected readonly email = SITE.email;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }
}
