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
import { Router, RouterLink } from '@angular/router';
import { errorMessage, toApiError } from '../core/api/api-error';
import { PendingProfile, providerName } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { safeReturnUrl } from '../core/auth/return-url';
import { toAppLocale } from '../core/locales';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';

/**
 * The last step of a first sign-in with Google or Facebook (docs/PLAN.md §3.2): the name, the terms, and the email
 * address when the provider did not send one. The provider's answer waits in a short-lived cookie meanwhile.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-complete-profile-page',
  template: `
    <section class="page container">
      <h1>{{ title }}</h1>

      @if (loadError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
        <p><a class="button" routerLink="/login" i18n="@@auth.backToLogin">Back to sign-in</a></p>
      } @else if (profile(); as profile) {
        <p i18n="@@auth.complete.intro">
          You are signing in with {{ providerName(profile.provider) }} for the first time. Please
          check your details.
        </p>

        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="profile-name" i18n="@@auth.displayName">Your name</label>
            <input
              id="profile-name"
              class="input"
              type="text"
              autocomplete="name"
              maxlength="100"
              formControlName="displayName"
              [attr.aria-invalid]="!!fieldError(form.controls.displayName)"
              aria-describedby="profile-name-error"
            />
            <p id="profile-name-error" class="field-error">
              {{ fieldError(form.controls.displayName) }}
            </p>
          </div>

          @if (profile.email) {
            <div class="field">
              <span class="field__label" i18n="@@auth.email">Email address</span>
              <p>{{ profile.email }}</p>
            </div>
          } @else {
            <div class="field">
              <label for="profile-email" i18n="@@auth.email">Email address</label>
              <input
                id="profile-email"
                class="input"
                type="email"
                autocomplete="email"
                formControlName="email"
                [attr.aria-invalid]="!!fieldError(form.controls.email)"
                aria-describedby="profile-email-hint profile-email-error"
              />
              <p id="profile-email-hint" class="hint" i18n="@@auth.complete.emailHint">
                {{ providerName(profile.provider) }} did not share your email address. We will send
                a link to confirm it.
              </p>
              <p id="profile-email-error" class="field-error">
                {{ fieldError(form.controls.email) }}
              </p>
            </div>
          }

          <div class="field">
            <label class="checkbox">
              <input
                type="checkbox"
                formControlName="acceptTerms"
                aria-describedby="profile-terms-error"
              />
              <span i18n="@@auth.acceptTerms"
                >I accept the <a routerLink="/terms" target="_blank">terms of service</a> and the
                <a routerLink="/privacy" target="_blank">privacy policy</a>.</span
              >
            </label>
            <p id="profile-terms-error" class="field-error">
              {{ fieldError(form.controls.acceptTerms) }}
            </p>
          </div>

          @if (state.error(); as message) {
            <p class="alert alert--error" role="alert">{{ message }}</p>
            @if (state.apiError()?.code === 'auth.account_exists') {
              <p><a routerLink="/login" i18n="@@auth.backToLogin">Back to sign-in</a></p>
            }
          }

          <div class="actions">
            <button
              class="button button--primary"
              type="submit"
              [disabled]="state.busy()"
              i18n="@@auth.complete.submit"
            >
              Create account
            </button>
          </div>
        </form>
      } @else {
        <p role="status" i18n="@@common.loading">Loading…</p>
      }
    </section>
  `,
})
export class CompleteProfilePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly locale = toAppLocale(inject(LOCALE_ID));

  readonly returnUrl = input<string>();

  protected readonly title = $localize`:@@auth.complete.title:Almost there`;
  protected readonly providerName = providerName;
  protected readonly target = computed(() => safeReturnUrl(this.returnUrl()));
  protected readonly profile = signal<PendingProfile | null>(null);
  protected readonly loadError = signal<string | null>(null);

  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    acceptTerms: [false, Validators.requiredTrue],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const profile = await this.auth.pendingProfile();
      this.form.controls.displayName.setValue(profile.displayName ?? '');
      if (profile.email) {
        // The provider's address is used; the field is not needed.
        this.form.controls.email.disable();
      }

      this.profile.set(profile);
    } catch (error) {
      this.loadError.set(errorMessage(toApiError(error)));
    }
  }

  protected async submit(): Promise<void> {
    const value = this.form.getRawValue();
    const succeeded = await this.state.run(
      () =>
        this.auth.completeProfile({
          email: this.form.controls.email.enabled ? value.email.trim() : null,
          displayName: value.displayName.trim(),
          preferredLanguage: this.locale,
          acceptTerms: value.acceptTerms,
        }),
      this.form,
    );

    if (succeeded) {
      await this.router.navigateByUrl(this.target() ?? this.auth.homeFor(this.auth.user()));
    }
  }
}
