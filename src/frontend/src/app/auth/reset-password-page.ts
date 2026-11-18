import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Seo } from '../core/seo';
import { FormState, PASSWORD_MIN_LENGTH, fieldError } from '../shared/forms/form-state';

/** Opened from the reset email: `/reset-password?userId=...&code=...`. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-reset-password-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (done()) {
        <p class="alert alert--success" role="status" i18n="@@auth.reset.done">
          Your password has been changed. You can now sign in with it.
        </p>
        <p>
          <a class="button button--primary" routerLink="/login" i18n="@@auth.reset.signIn"
            >Sign in</a
          >
        </p>
      } @else if (!userId() || !code()) {
        <p class="alert alert--error" role="alert" i18n="@@auth.reset.incomplete">
          This link is incomplete. Please open the link from the email again, or ask for a new one.
        </p>
        <p><a routerLink="/forgot-password" i18n="@@auth.reset.newLink">Ask for a new link</a></p>
      } @else {
        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="reset-password" i18n="@@auth.newPassword">New password</label>
            <input
              id="reset-password"
              class="input"
              type="password"
              autocomplete="new-password"
              formControlName="password"
              [attr.aria-invalid]="!!fieldError(form.controls.password)"
              aria-describedby="reset-password-hint reset-password-error"
            />
            <p id="reset-password-hint" class="hint" i18n="@@auth.passwordHint">
              At least 12 characters. A short sentence is easy to remember and hard to guess.
            </p>
            <p id="reset-password-error" class="field-error">
              {{ fieldError(form.controls.password) }}
            </p>
          </div>

          @if (state.error(); as message) {
            <p class="alert alert--error" role="alert">{{ message }}</p>
            @if (state.apiError()?.code === 'auth.invalid_token') {
              <p>
                <a routerLink="/forgot-password" i18n="@@auth.reset.newLink">Ask for a new link</a>
              </p>
            }
          }

          <div class="actions">
            <button
              class="button button--primary"
              type="submit"
              [disabled]="state.busy()"
              i18n="@@auth.reset.submit"
            >
              Save the new password
            </button>
          </div>
        </form>
      }
    </section>
  `,
})
export class ResetPasswordPage {
  private readonly auth = inject(AuthService);

  readonly userId = input<string>();
  readonly code = input<string>();

  protected readonly title = $localize`:@@auth.reset.title:Choose a new password`;
  protected readonly done = signal(false);
  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)]],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }

  protected async submit(): Promise<void> {
    const userId = this.userId();
    const code = this.code();
    if (!userId || !code) {
      return;
    }

    const { password } = this.form.getRawValue();
    if (await this.state.run(() => this.auth.resetPassword(userId, code, password), this.form)) {
      this.done.set(true);
    }
  }
}
