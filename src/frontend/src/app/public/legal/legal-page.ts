import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import {
  LEGAL_DOCUMENTS,
  LEGAL_PLACEHOLDER_NOTICE,
  LEGAL_TOPICS_HEADING,
  LegalDocumentId,
} from '../../content/legal';
import { Seo } from '../../core/seo';

/** One component for the three legal pages. The route data says which document to show. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-legal-page',
  styleUrl: './legal-page.scss',
  template: `
    <article class="legal container">
      <h1>{{ content().title }}</h1>
      <p class="legal__notice" role="note">{{ notice }}</p>
      <p>{{ content().intro }}</p>

      <h2>{{ topicsHeading }}</h2>
      <ul class="legal__topics">
        @for (topic of content().topics; track $index) {
          <li>{{ topic }}</li>
        }
      </ul>
    </article>
  `,
})
export class LegalPage {
  private readonly seo = inject(Seo);

  /** Bound from the route data through `withComponentInputBinding()`. */
  readonly document = input.required<LegalDocumentId>();

  protected readonly notice = LEGAL_PLACEHOLDER_NOTICE;
  protected readonly topicsHeading = LEGAL_TOPICS_HEADING;
  protected readonly content = computed(() => LEGAL_DOCUMENTS[this.document()]);

  constructor() {
    effect(() => {
      const content = this.content();

      this.seo.setPage({
        title: content.title,
        description: content.description,
        path: content.id,
      });
    });
  }
}
