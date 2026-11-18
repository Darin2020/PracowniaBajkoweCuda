import {
  ChangeDetectionStrategy,
  Component,
  LOCALE_ID,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { safeReturnUrl } from '../core/auth/return-url';
import { toAppLocale } from '../core/locales';
import { Seo } from '../core/seo';
import { FormState, PASSWORD_MIN_LENGTH, fieldError } from '../shared/forms/form-state';
import { ProviderButtons } from './provider-buttons';
import { ResendConfirmation } from './resend-confirmation';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProviderButtons, ReactiveFormsModule, ResendConfirmation, RouterLink],
  selector: 'app-register-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (sentTo(); as email) {
        <div class="alert alert--success" role="status">
          <p i18n="@@auth.register.sent">
            Almost done! We have sent a confirmation link to {{ email }}. Open it to activate your
            account.
          </p>
        </div>
        <app-resend-confirmation [email]="email" />
      } @else {
        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="register-name" i18n="@@auth.displayName">Your name</label>
            <input
              id="register-name"
              class="input"
              type="text"
              autocomplete="name"
              maxlength="100"
              formControlName="displayName"
              [attr.aria-invalid]="!!fieldError(form.controls.displayName)"
              aria-describedby="register-name-error"
            />
            <p id="register-name-error" class="field-error">
              {{ fieldError(form.controls.displayName) }}
            </p>
          </div>

          <div class="field">
            <label for="register-email" i18n="@@auth.email">Email address</label>
            <input
              id="register-email"
              class="input"
              type="email"
              autocomplete="email"
              formControlName="email"
              [attr.aria-invalid]="!!fieldError(form.controls.email)"
              aria-describedby="register-email-error"
            />
            <p id="register-email-error" class="field-error">
              {{ fieldError(form.controls.email) }}
            </p>
          </div>

          <div class="field">
            <label for="register-password" i18n="@@auth.password">Password</label>
            <input
              id="register-password"
              class="input"
              type="password"
              autocomplete="new-password"
              formControlName="password"
              [attr.aria-invalid]="!!fieldError(form.controls.password)"
              aria-describedby="register-password-hint register-password-error"
            />
            <p id="register-password-hint" class="hint" i18n="@@auth.passwordHint">
              At least 12 characters. A short sentence is easy to remember and hard to guess.
            </p>
            <p id="register-password-error" class="field-error">
              {{ fieldError(form.controls.password) }}
            </p>
          </div>

          <div class="field">
            <label class="checkbox">
              <input
                type="checkbox"
                formControlName="acceptTerms"
                aria-describedby="register-terms-error"
              />
              <span i18n="@@auth.acceptTerms"
                >I accept the <a routerLink="/terms" target="_blank">terms of service</a> and the
                <a routerLink="/privacy" target="_blank">privacy policy</a>.</span
              >
            </label>
            <p id="register-terms-error" class="field-error">
              {{ fieldError(form.controls.acceptTerms) }}
            </p>
          </div>

          @if (state.error(); as message) {
            <p class="alert alert--error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button
              class="button button--primary"
              type="submit"
              [disabled]="state.busy()"
              i18n="@@auth.register.submit"
            >
              Create account
            </button>
          </div>
        </form>

        <app-provider-buttons [returnUrl]="target()" />

        <p>
          <span i18n="@@auth.register.haveAccount">Already have an account?</span>
          {{ ' ' }}
          <a
            routerLink="/login"
            [queryParams]="target() ? { returnUrl: target() } : {}"
            i18n="@@auth.register.signIn"
            >Sign in</a
          >
        </p>
      }
    </section>
  `,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly locale = toAppLocale(inject(LOCALE_ID));

  readonly returnUrl = input<string>();

  protected readonly title = $localize`:@@auth.register.title:Create an account`;
  protected readonly target = computed(() => safeReturnUrl(this.returnUrl()));
  protected readonly sentTo = signal<string | null>(null);

  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)]],
    acceptTerms: [false, Validators.requiredTrue],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }

  protected async submit(): Promise<void> {
    const value = this.form.getRawValue();
    const email = value.email.trim();
    const succeeded = await this.state.run(
      () =>
        this.auth.register({
          email,
          password: value.password,
          displayName: value.displayName.trim(),
          preferredLanguage: this.locale,
          acceptTerms: value.acceptTerms,
        }),
      this.form,
    );

    if (succeeded) {
      this.sentTo.set(email);
    }
  }
}
