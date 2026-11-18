import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { providerName } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';

/**
 * "Continue with Google / Facebook" for the providers this environment has configured (docs/PLAN.md §3.2). They are
 * links, not buttons: the sign-in leaves the app for the provider's page and comes back through the API.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-provider-buttons',
  styles: `
    :host {
      display: contents;
    }

    .providers {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
  `,
  template: `
    @if (providers().length > 0) {
      <p class="divider" i18n="@@auth.providers.or">or</p>
      <div class="providers">
        @for (provider of providers(); track provider) {
          <a
            class="button"
            [href]="auth.externalUrl(provider, 'signin', returnUrl() || '/signed-in')"
            i18n="@@auth.providers.continueWith"
            >Continue with {{ name(provider) }}</a
          >
        }
      </div>
    }
  `,
})
export class ProviderButtons {
  protected readonly auth = inject(AuthService);

  /** Where to go after signing in (an app path like "/chat"); by default the user's home page. */
  readonly returnUrl = input<string | null | undefined>(null);

  protected readonly providers = signal<string[]>([]);
  protected readonly name = providerName;

  constructor() {
    // Without the list the page still works with email and password, so a failure is not shown.
    this.auth.providers().then(
      (providers) => this.providers.set(providers),
      () => this.providers.set([]),
    );
  }
}
