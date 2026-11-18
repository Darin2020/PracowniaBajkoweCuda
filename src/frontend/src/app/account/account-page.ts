import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ResendConfirmation } from '../auth/resend-confirmation';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AccountService } from '../core/auth/account.service';
import { AccountLogins, providerName } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { LOCALE_NAMES, SUPPORTED_LOCALES } from '../core/locales';
import { Seo } from '../core/seo';
import { FormState, PASSWORD_MIN_LENGTH, fieldError } from '../shared/forms/form-state';

/**
 * `/account` (docs/PLAN.md §3.2): name and language, the ways to sign in, two-factor authentication for admins,
 * "Export my data" and "Delete account". Query parameters come back from external sign-ins started here:
 * `?linked=google`, `?reauthenticated=1`, `?error=...`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ResendConfirmation, RouterLink],
  selector: 'app-account-page',
  styles: `
    .heading {
      justify-content: space-between;
    }

    .login-list {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 0.5rem 1rem;
        min-height: 2.75rem;
        padding: 0.25rem 0;
        border-bottom: 1px solid var(--color-border);
      }
    }

    h3 {
      margin-top: 0.5rem;
      font-size: 1.0625rem;
    }
  `,
  templateUrl: './account-page.html',
})
export class AccountPage {
  protected readonly auth = inject(AuthService);
  protected readonly account = inject(AccountService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly error = input<string>();
  readonly linked = input<string>();
  readonly reauthenticated = input<string>();

  protected readonly title = $localize`:@@account.title:Your account`;
  protected readonly locales = SUPPORTED_LOCALES;
  protected readonly localeNames = LOCALE_NAMES;
  protected readonly providerName = providerName;
  protected readonly fieldError = fieldError;

  protected readonly redirectError = computed(() => {
    const code = this.error();
    return code ? errorMessage(code) : null;
  });

  protected readonly logins = signal<AccountLogins | null>(null);
  protected readonly loginsError = signal<string | null>(null);

  protected readonly profileState = new FormState();
  protected readonly profileSaved = signal(false);
  protected readonly profileForm = this.formBuilder.group({
    displayName: [
      this.auth.user()?.displayName ?? '',
      [Validators.required, Validators.maxLength(100)],
    ],
    preferredLanguage: [this.auth.user()?.preferredLanguage ?? 'en', Validators.required],
  });

  protected readonly passwordState = new FormState();
  protected readonly passwordChanged = signal(false);
  protected readonly passwordForm = this.formBuilder.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)]],
  });

  protected readonly loginState = new FormState();

  /** The deletion form is shown on request, or straight away after re-authenticating through a provider. */
  protected readonly deleting = signal(false);
  protected readonly deleteState = new FormState();
  protected readonly deleteForm = this.formBuilder.group({
    password: ['', Validators.required],
  });

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    void this.loadLogins();
  }

  protected externalUrl(provider: string, mode: 'link' | 'reauth'): string {
    return this.auth.externalUrl(
      provider,
      mode,
      mode === 'reauth' ? '/account?reauthenticated=1' : '/account',
    );
  }

  protected async saveProfile(): Promise<void> {
    this.profileSaved.set(false);
    const value = this.profileForm.getRawValue();
    const saved = await this.profileState.run(
      () =>
        this.account.update({
          displayName: value.displayName.trim(),
          preferredLanguage: value.preferredLanguage,
        }),
      this.profileForm,
    );
    this.profileSaved.set(saved);
  }

  protected async changePassword(): Promise<void> {
    this.passwordChanged.set(false);
    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    if (
      await this.passwordState.run(
        () => this.account.changePassword(currentPassword, newPassword),
        this.passwordForm,
      )
    ) {
      this.passwordForm.reset();
      this.passwordChanged.set(true);
    }
  }

  protected async removeLogin(provider: string): Promise<void> {
    if (await this.loginState.run(() => this.account.removeLogin(provider))) {
      await this.loadLogins();
    }
  }

  protected startDeletion(): void {
    this.deleting.set(true);
  }

  protected cancelDeletion(): void {
    this.deleting.set(false);
    this.deleteForm.reset();
    this.deleteState.error.set(null);
  }

  /** With a password it is checked again; without one, the API checks the recent provider re-authentication. */
  protected async deleteAccount(): Promise<void> {
    const hasPassword = this.logins()?.hasPassword ?? true;
    const password = hasPassword ? this.deleteForm.getRawValue().password : null;
    const removed = await this.deleteState.run(
      () => this.account.delete(password),
      hasPassword ? this.deleteForm : undefined,
    );

    if (removed) {
      await this.router.navigate(['/account-deleted'], { replaceUrl: true });
    }
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }

  private async loadLogins(): Promise<void> {
    try {
      this.logins.set(await this.account.logins());
      this.loginsError.set(null);
      if (this.reauthenticated() && !this.logins()?.hasPassword) {
        this.deleting.set(true);
      }
    } catch (error) {
      this.loginsError.set(errorMessage(toApiError(error)));
    }
  }
}
