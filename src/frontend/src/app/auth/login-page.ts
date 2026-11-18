import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorMessage } from '../core/api/api-error';
import { LoginResult } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { safeReturnUrl } from '../core/auth/return-url';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';
import { ProviderButtons } from './provider-buttons';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProviderButtons, ReactiveFormsModule, RouterLink],
  selector: 'app-login-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (redirectError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="field">
          <label for="login-email" i18n="@@auth.email">Email address</label>
          <input
            id="login-email"
            class="input"
            type="email"
            autocomplete="email"
            formControlName="email"
            [attr.aria-invalid]="!!fieldError(form.controls.email)"
            aria-describedby="login-email-error"
          />
          <p id="login-email-error" class="field-error">{{ fieldError(form.controls.email) }}</p>
        </div>

        <div class="field">
          <label for="login-password" i18n="@@auth.password">Password</label>
          <input
            id="login-password"
            class="input"
            type="password"
            autocomplete="current-password"
            formControlName="password"
            [attr.aria-invalid]="!!fieldError(form.controls.password)"
            aria-describedby="login-password-error"
          />
          <p id="login-password-error" class="field-error">
            {{ fieldError(form.controls.password) }}
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
            i18n="@@auth.login.submit"
          >
            Sign in
          </button>
          <a routerLink="/forgot-password" i18n="@@auth.login.forgot">Forgot your password?</a>
        </div>
      </form>

      <app-provider-buttons [returnUrl]="target()" />

      <p>
        <span i18n="@@auth.login.noAccount">New here?</span>
        {{ ' ' }}
        <a
          routerLink="/register"
          [queryParams]="target() ? { returnUrl: target() } : {}"
          i18n="@@auth.login.register"
          >Create an account</a
        >
      </p>
    </section>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Query parameters: where to go afterwards, and an error code from a redirect of the API. */
  readonly returnUrl = input<string>();
  readonly error = input<string>();

  protected readonly title = $localize`:@@auth.login.title:Sign in`;
  protected readonly target = computed(() => safeReturnUrl(this.returnUrl()));
  protected readonly redirectError = computed(() => {
    const code = this.error();
    return code ? errorMessage(code) : null;
  });

  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
  }

  protected async submit(): Promise<void> {
    const outcome: { result?: LoginResult } = {};
    const { email, password } = this.form.getRawValue();
    const succeeded = await this.state.run(async () => {
      outcome.result = await this.auth.login(email.trim(), password);
    }, this.form);

    if (!succeeded) {
      return;
    }

    if (outcome.result === 'two-factor') {
      await this.router.navigate(['/login/2fa'], {
        queryParams: this.target() ? { returnUrl: this.target() } : {},
      });
    } else {
      await this.router.navigateByUrl(this.target() ?? this.auth.homeFor(this.auth.user()));
    }
  }
}
