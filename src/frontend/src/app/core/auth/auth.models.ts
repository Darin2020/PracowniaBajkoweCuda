// Shapes of /api/auth and /api/account (docs/PLAN.md §6). Keep in step with the backend's AuthResponses.cs.

export const ADMIN_ROLE = 'Admin';

/** The signed-in user, as `GET /api/auth/me` describes them. */
export interface SessionUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailConfirmed: boolean;
  readonly preferredLanguage: string;
  readonly roles: readonly string[];
  readonly twoFactorEnabled: boolean;
}

export type MeResponse =
  { readonly authenticated: false } | ({ readonly authenticated: true } & SessionUser);

export interface TwoFactorRequiredResponse {
  readonly requiresTwoFactor: true;
}

/** How a password sign-in ended: signed in, or a second factor is needed (`/login/2fa`). */
export type LoginResult = 'signed-in' | 'two-factor';

export interface RegisterRequest {
  readonly email: string;
  readonly password: string;
  readonly displayName: string;
  readonly preferredLanguage: string;
  readonly acceptTerms: boolean;
}

export interface PendingProfile {
  readonly provider: string;
  readonly email: string | null;
  readonly emailVerified: boolean;
  readonly displayName: string | null;
}

export interface CompleteProfileRequest {
  readonly email: string | null;
  readonly displayName: string;
  readonly preferredLanguage: string;
  readonly acceptTerms: boolean;
}

export interface TwoFactorSetup {
  readonly sharedKey: string;
  readonly authenticatorUri: string;
}

export interface RecoveryCodes {
  readonly recoveryCodes: readonly string[];
}

export interface Account {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailConfirmed: boolean;
  readonly preferredLanguage: string;
  readonly createdAt: string;
}

export interface AccountLogins {
  readonly hasPassword: boolean;
  readonly logins: readonly { readonly provider: string; readonly displayName: string | null }[];
  readonly available: readonly string[];
}

/** What the external sign-in is for (`GET /api/auth/external/{provider}?mode=`). */
export type ExternalMode = 'signin' | 'link' | 'reauth';

/** Provider names as the API spells them; shown as brand names, never translated. */
export const PROVIDER_NAMES: Readonly<Record<string, string>> = {
  google: 'Google',
  facebook: 'Facebook',
};

export const providerName = (provider: string): string => PROVIDER_NAMES[provider] ?? provider;
