import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-forgot-password-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (sent()) {
        <p class="alert alert--success" role="status" i18n="@@auth.forgot.sent">
          If an account uses this address, we have sent it a link to choose a new password. The link
          works for a few hours.
        </p>
        <p><a routerLink="/login" i18n="@@auth.backToLogin">Back to sign-in</a></p>
      } @else {
        <p i18n="@@auth.forgot.intro">
          Enter the email address of your account and we will send you a link to choose a new
          password.
        </p>

        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="forgot-email" i18n="@@auth.email">Email address</label>
            <input
              id="forgot-email"
              class="input"
              type="email"
              autocomplete="email"
              formControlName="email"
              [attr.aria-invalid]="!!fieldError(form.controls.email)"
              aria-describedby="forgot-email-error"
            />
            <p id="forgot-email-error" class="field-error">{{ fieldError(form.controls.email) }}</p>
          </div>

          @if (state.error(); as message) {
            <p class="alert alert--error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button
              class="button button--primary"
              type="submit"
              [disabled]="state.busy()"
              i18n="@@auth.forgot.submit"
            >
              Send the link
            </button>
            <a routerLink="/login" i18n="@@auth.backToLogin">Back to sign-in</a>
          </div>
        </form>
      }
    </section>
  `,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);

  protected readonly title = $localize`:@@auth.forgot.title:Forgot your password?`;
  protected readonly sent = signal(false);
  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }

  protected async submit(): Promise<void> {
    const email = this.form.getRawValue().email.trim();
    if (await this.state.run(() => this.auth.forgotPassword(email), this.form)) {
      this.sent.set(true);
    }
  }
}
