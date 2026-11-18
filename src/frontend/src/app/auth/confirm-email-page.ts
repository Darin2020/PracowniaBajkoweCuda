import {
  ChangeDetectionStrategy,
  Component,
  afterNextRender,
  inject,
  input,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';

type Status = 'working' | 'confirmed' | 'failed';

/**
 * Opened from the confirmation email: `/confirm-email?userId=...&code=...`. The page confirms with a POST, so a mail
 * scanner that only fetches the link confirms nothing.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-confirm-email-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @switch (status()) {
        @case ('working') {
          <p role="status" i18n="@@auth.confirm.working">Confirming your email address…</p>
        }
        @case ('confirmed') {
          <p class="alert alert--success" role="status" i18n="@@auth.confirm.done">
            Thank you, your email address is confirmed.
          </p>
          <p>
            @if (auth.signedIn()) {
              <a class="button button--primary" routerLink="/chat" i18n="@@auth.confirm.toChat"
                >Go to the chat</a
              >
            } @else {
              <a class="button button--primary" routerLink="/login" i18n="@@auth.confirm.signIn"
                >Sign in</a
              >
            }
          </p>
        }
        @case ('failed') {
          <p class="alert alert--error" role="alert">{{ confirmState.error() }}</p>

          @if (resent()) {
            <p class="alert alert--success" role="status" i18n="@@auth.resend.sent">
              If the address needs confirming, a new link is on its way. Please also check the spam
              folder.
            </p>
          } @else {
            <form class="form" [formGroup]="form" (ngSubmit)="resend()" novalidate>
              <div class="field">
                <label for="confirm-email" i18n="@@auth.confirm.resendLabel"
                  >Send a new link to</label
                >
                <input
                  id="confirm-email"
                  class="input"
                  type="email"
                  autocomplete="email"
                  formControlName="email"
                  [attr.aria-invalid]="!!fieldError(form.controls.email)"
                  aria-describedby="confirm-email-error"
                />
                <p id="confirm-email-error" class="field-error">
                  {{ fieldError(form.controls.email) }}
                </p>
              </div>
              @if (resendState.error(); as message) {
                <p class="alert alert--error" role="alert">{{ message }}</p>
              }
              <div class="actions">
                <button
                  class="button"
                  type="submit"
                  [disabled]="resendState.busy()"
                  i18n="@@auth.resend.button"
                >
                  Send the link again
                </button>
              </div>
            </form>
          }
        }
      }
    </section>
  `,
})
export class ConfirmEmailPage {
  protected readonly auth = inject(AuthService);

  readonly userId = input<string>();
  readonly code = input<string>();

  protected readonly title = $localize`:@@auth.confirm.title:Confirm your email address`;
  protected readonly status = signal<Status>('working');
  protected readonly resent = signal(false);
  protected readonly confirmState = new FormState();
  protected readonly resendState = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: [this.auth.user()?.email ?? '', [Validators.required, Validators.email]],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    afterNextRender(() => void this.confirm());
  }

  private async confirm(): Promise<void> {
    const userId = this.userId();
    const code = this.code();
    const confirmed = await this.confirmState.run(async () => {
      if (!userId || !code) {
        throw new Error('incomplete link');
      }

      await this.auth.confirmEmail(userId, code);
    });

    if (!confirmed && (!userId || !code)) {
      this.confirmState.error.set(
        $localize`:@@auth.confirm.incomplete:This link is incomplete. Please open the link from the email again.`,
      );
    }

    this.status.set(confirmed ? 'confirmed' : 'failed');
  }

  protected async resend(): Promise<void> {
    const email = this.form.getRawValue().email.trim();
    if (await this.resendState.run(() => this.auth.resendConfirmation(email), this.form)) {
      this.resent.set(true);
    }
  }
}
