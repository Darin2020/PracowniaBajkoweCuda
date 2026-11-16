import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Seo } from '../../core/seo';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-not-found-page',
  styleUrl: './message-page.scss',
  template: `
    <section class="message container">
      <h1>{{ title }}</h1>
      <p i18n="@@notFound.text">The page you are looking for does not exist or has been moved.</p>
      <a class="button" routerLink="/" i18n="@@message.backHome">Back to the home page</a>
    </section>
  `,
})
export class NotFoundPage {
  protected readonly title = $localize`:@@notFound.title:Page not found`;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }
}
