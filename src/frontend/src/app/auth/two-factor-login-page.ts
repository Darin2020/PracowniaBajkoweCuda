import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { safeReturnUrl } from '../core/auth/return-url';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';

/** The second step of signing in to an account with 2FA: a code of the authenticator app, or a recovery code. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-two-factor-login-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (recovery()) {
        <p i18n="@@auth.2fa.recoveryIntro">
          Enter one of the recovery codes you saved when you set up two-factor authentication. Each
          code works once.
        </p>
      } @else {
        <p i18n="@@auth.2fa.intro">
          Open your authenticator app and enter the 6-digit code for Pracownia Bajkowe Cuda.
        </p>
      }

      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="field">
          @if (recovery()) {
            <label for="two-factor-code" i18n="@@auth.2fa.recoveryLabel">Recovery code</label>
          } @else {
            <label for="two-factor-code" i18n="@@auth.2fa.codeLabel">Code</label>
          }
          <input
            id="two-factor-code"
            class="input input--code"
            type="text"
            [attr.inputmode]="recovery() ? 'text' : 'numeric'"
            autocomplete="one-time-code"
            maxlength="32"
            formControlName="code"
            [attr.aria-invalid]="!!fieldError(form.controls.code)"
            aria-describedby="two-factor-code-error"
          />
          <p id="two-factor-code-error" class="field-error">{{ fieldError(form.controls.code) }}</p>
        </div>

        @if (state.error(); as message) {
          <p class="alert alert--error" role="alert">{{ message }}</p>
          @if (state.apiError()?.code === 'auth.invalid_credentials') {
            <p><a routerLink="/login" i18n="@@auth.backToLogin">Back to sign-in</a></p>
          }
        }

        <div class="actions">
          <button
            class="button button--primary"
            type="submit"
            [disabled]="state.busy()"
            i18n="@@auth.2fa.submit"
          >
            Continue
          </button>
          <button class="link-button" type="button" (click)="toggleRecovery()">
            @if (recovery()) {
              <ng-container i18n="@@auth.2fa.useApp">Use the authenticator app</ng-container>
            } @else {
              <ng-container i18n="@@auth.2fa.useRecovery">Use a recovery code</ng-container>
            }
          </button>
        </div>
      </form>
    </section>
  `,
})
export class TwoFactorLoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  readonly returnUrl = input<string>();

  protected readonly title = $localize`:@@auth.2fa.title:Two-factor authentication`;
  // After an external sign-in the API passes the return URL with the locale prefix ("/pl/chat"): strip it.
  protected readonly target = computed(() => {
    const returnUrl = this.returnUrl();
    return safeReturnUrl(returnUrl ? this.location.normalize(returnUrl) : returnUrl);
  });
  protected readonly recovery = signal(false);

  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    code: ['', Validators.required],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }

  protected toggleRecovery(): void {
    this.recovery.update((recovery) => !recovery);
    this.form.reset();
    this.state.error.set(null);
  }

  protected async submit(): Promise<void> {
    const code = this.form.getRawValue().code.trim();
    if (
      await this.state.run(() => this.auth.loginWithSecondFactor(code, this.recovery()), this.form)
    ) {
      await this.router.navigateByUrl(this.target() ?? this.auth.homeFor(this.auth.user()));
    } else if (this.state.apiError()?.code === 'auth.invalid_credentials') {
      // The password step is remembered for a few minutes only.
      this.state.error.set(
        $localize`:@@auth.2fa.expired:This sign-in took too long. Please enter your password again.`,
      );
    }
  }
}
