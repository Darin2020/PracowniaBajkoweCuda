import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage, toApiError } from '../core/api/api-error';
import { TwoFactorSetup } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { Seo } from '../core/seo';
import { FormState, fieldError } from '../shared/forms/form-state';

/**
 * `/admin/2fa-setup` (docs/PLAN.md §3.3, §8.1): admins must turn on TOTP before the admin area answers. The key is
 * shown as text and as an `otpauth://` link (which opens the authenticator app on a phone); after the first valid
 * code the recovery codes are shown once.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-two-factor-setup-page',
  styles: `
    .key {
      font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
      font-size: 1.125rem;
      overflow-wrap: anywhere;
    }

    ol {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      margin: 0;
      padding-left: 1.25rem;
    }
  `,
  template: `
    <section class="page page--wide container">
      <h1>{{ title }}</h1>

      @if (recoveryCodes(); as codes) {
        <p class="alert alert--success" role="status" i18n="@@twoFactor.enabled">
          Two-factor authentication is on.
        </p>
        <section class="card" aria-labelledby="recovery-codes">
          <h2 id="recovery-codes" i18n="@@twoFactor.recoveryTitle">Your recovery codes</h2>
          <p i18n="@@twoFactor.recoveryText">
            Save these codes somewhere safe, for example in a password manager. If you lose your
            phone, each code lets you sign in once. They are shown only now.
          </p>
          <ul class="code-list">
            @for (code of codes; track code) {
              <li>{{ code }}</li>
            }
          </ul>
          <div class="actions">
            <a
              class="button"
              [href]="download(codes)"
              download="bajkowecuda-recovery-codes.txt"
              i18n="@@twoFactor.download"
              >Download as a file</a
            >
            <a class="button button--primary" routerLink="/account" i18n="@@twoFactor.done">Done</a>
          </div>
        </section>
      } @else if (auth.user()?.twoFactorEnabled) {
        <p i18n="@@account.twoFactorOn">Two-factor authentication is on.</p>
        <section class="card" aria-labelledby="new-codes">
          <h2 id="new-codes" i18n="@@twoFactor.newCodesTitle">New recovery codes</h2>
          <p i18n="@@twoFactor.newCodesText">
            Used up or lost your recovery codes? New codes replace all the old ones.
          </p>
          @if (state.error(); as message) {
            <p class="alert alert--error" role="alert">{{ message }}</p>
          }
          <div class="actions">
            <button
              class="button"
              type="button"
              [disabled]="state.busy()"
              (click)="newCodes()"
              i18n="@@twoFactor.newCodes"
            >
              Create new recovery codes
            </button>
          </div>
        </section>
      } @else if (setup(); as setup) {
        <ol>
          <li i18n="@@twoFactor.step1">
            Install an authenticator app on your phone, for example Microsoft Authenticator, Google
            Authenticator or 2FAS.
          </li>
          <li>
            <p i18n="@@twoFactor.step2">Add an account in the app with this key:</p>
            <p class="key">{{ setup.sharedKey }}</p>
            <p>
              <a [href]="setup.authenticatorUri" i18n="@@twoFactor.openApp"
                >On this phone? Open the authenticator app</a
              >
            </p>
          </li>
          <li>
            <form class="form" [formGroup]="form" (ngSubmit)="enable()" novalidate>
              <div class="field">
                <label for="setup-code" i18n="@@twoFactor.step3"
                  >Enter the 6-digit code the app shows</label
                >
                <input
                  id="setup-code"
                  class="input input--code"
                  type="text"
                  inputmode="numeric"
                  autocomplete="one-time-code"
                  maxlength="12"
                  formControlName="code"
                  [attr.aria-invalid]="!!fieldError(form.controls.code)"
                  aria-describedby="setup-code-error"
                />
                <p id="setup-code-error" class="field-error">
                  {{ fieldError(form.controls.code) }}
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
                  i18n="@@twoFactor.enable"
                >
                  Turn on
                </button>
              </div>
            </form>
          </li>
        </ol>
      } @else if (loadError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      } @else {
        <p role="status" i18n="@@common.loading">Loading…</p>
      }
    </section>
  `,
})
export class TwoFactorSetupPage {
  protected readonly auth = inject(AuthService);

  protected readonly title = $localize`:@@twoFactor.title:Two-factor authentication`;
  protected readonly setup = signal<TwoFactorSetup | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly recoveryCodes = signal<readonly string[] | null>(null);

  protected readonly state = new FormState();
  protected readonly fieldError = fieldError;
  protected readonly form = inject(NonNullableFormBuilder).group({
    code: ['', Validators.required],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    if (!this.auth.user()?.twoFactorEnabled) {
      void this.load();
    }
  }

  /** The codes as a text file, made in the browser: they never travel anywhere else. */
  protected download(codes: readonly string[]): string {
    return `data:text/plain;charset=utf-8,${encodeURIComponent(codes.join('\n') + '\n')}`;
  }

  protected async enable(): Promise<void> {
    const code = this.form.getRawValue().code.trim();
    await this.state.run(
      async () => this.recoveryCodes.set(await this.auth.enableTwoFactor(code)),
      this.form,
    );
  }

  protected async newCodes(): Promise<void> {
    await this.state.run(async () => this.recoveryCodes.set(await this.auth.newRecoveryCodes()));
  }

  private async load(): Promise<void> {
    try {
      this.setup.set(await this.auth.twoFactorSetup());
    } catch (error) {
      this.loadError.set(errorMessage(toApiError(error)));
    }
  }
}
