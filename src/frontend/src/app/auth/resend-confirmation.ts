import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { AuthService } from '../core/auth/auth.service';
import { FormState } from '../shared/forms/form-state';

/** "Send the link again" for an address that has not been confirmed yet. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-resend-confirmation',
  template: `
    @if (sent()) {
      <p class="alert alert--success" role="status" i18n="@@auth.resend.sent">
        If the address needs confirming, a new link is on its way. Please also check the spam
        folder.
      </p>
    } @else {
      <p class="actions">
        <span i18n="@@auth.resend.question">No email after a few minutes?</span>
        <button
          class="link-button"
          type="button"
          [disabled]="state.busy()"
          (click)="resend()"
          i18n="@@auth.resend.button"
        >
          Send the link again
        </button>
      </p>
      @if (state.error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }
    }
  `,
})
export class ResendConfirmation {
  private readonly auth = inject(AuthService);

  readonly email = input.required<string>();

  protected readonly sent = signal(false);
  protected readonly state = new FormState();

  protected async resend(): Promise<void> {
    if (await this.state.run(() => this.auth.resendConfirmation(this.email()))) {
      this.sent.set(true);
    }
  }
}
