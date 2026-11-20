import { HttpErrorResponse } from '@angular/common/http';

/**
 * A failed API call. The API answers with RFC 7807 problem details that carry a stable `code`
 * (docs/PLAN.md §4.2); the page shows `errorMessage(error)`, never the server's title.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    /** Field errors of `validation_failed` and `auth.password_rejected` (field name → codes or texts). */
    readonly errors: Readonly<Record<string, readonly string[]>> = {},
  ) {
    super(code);
    this.name = 'ApiError';
  }
}

/** Status 0: the request never got an answer (offline, DNS, the server is down). */
export const NETWORK_ERROR = 'network';
export const UNKNOWN_ERROR = 'unknown';

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (!(error instanceof HttpErrorResponse)) {
    return new ApiError(0, UNKNOWN_ERROR);
  }

  if (error.status === 0) {
    return new ApiError(0, NETWORK_ERROR);
  }

  const body: unknown = error.error;
  if (body && typeof body === 'object') {
    const problem = body as { code?: unknown; errors?: unknown };
    const code = typeof problem.code === 'string' ? problem.code : UNKNOWN_ERROR;
    const errors = isFieldErrors(problem.errors) ? problem.errors : {};

    return new ApiError(error.status, code, errors);
  }

  return new ApiError(error.status, error.status === 429 ? 'rate_limited' : UNKNOWN_ERROR);
}

function isFieldErrors(value: unknown): value is Record<string, string[]> {
  return (
    !!value &&
    typeof value === 'object' &&
    Object.values(value).every(
      (item) => Array.isArray(item) && item.every((text) => typeof text === 'string'),
    )
  );
}

/** The localized text for an error of the API, or for an `?error=` code of a redirect from it. */
export function errorMessage(error: ApiError | string): string {
  const code = typeof error === 'string' ? error : error.code;

  switch (code) {
    case 'auth.invalid_credentials':
      return $localize`:@@error.invalidCredentials:The email address or the password is not correct.`;
    case 'auth.invalid_token':
      return $localize`:@@error.invalidToken:This link is not valid any more. It may have been used already or have expired.`;
    case 'auth.invalid_2fa_code':
      return $localize`:@@error.invalidTwoFactorCode:The code is not correct. Check the time on your phone and try again.`;
    case 'auth.reauthentication_failed':
    case 'reauthentication_failed':
      return $localize`:@@error.reauthenticationFailed:We could not confirm that it is you. Check the password and try again.`;
    case 'auth.account_exists':
    case 'account_exists':
      return $localize`:@@error.accountExists:An account with this email address already exists. Sign in with your password, then link the provider on your account page.`;
    case 'login_in_use':
      return $localize`:@@error.loginInUse:This provider account is already linked to another account.`;
    case 'external_failed':
      return $localize`:@@error.externalFailed:Signing in through the provider did not work. Please try again.`;
    case 'auth.external_session_expired':
      return $localize`:@@error.externalSessionExpired:This sign-in took too long. Please start again.`;
    case 'auth.provider_unavailable':
      return $localize`:@@error.providerUnavailable:This sign-in method is not available at the moment.`;
    case 'auth.last_login':
      return $localize`:@@error.lastLogin:This is the only way to sign in to your account, so it cannot be removed.`;
    case 'auth.password_rejected':
      return passwordMessage(typeof error === 'string' ? [] : (error.errors['password'] ?? []));
    case 'auth.mfa_required':
      return $localize`:@@error.mfaRequired:Set up two-factor authentication to continue.`;
    case 'auth.forbidden':
      return $localize`:@@error.forbidden:You do not have access to this page.`;
    case 'auth.not_authenticated':
      return $localize`:@@error.notAuthenticated:Please sign in to continue.`;
    case 'antiforgery.invalid':
      return $localize`:@@error.antiforgery:Your session has changed. Reload the page and try again.`;
    case 'chat.empty_message':
      return $localize`:@@error.chatEmpty:Write a message or add a photo.`;
    case 'chat.message_too_long':
      return $localize`:@@error.chatTooLong:A message can have at most 2,000 characters.`;
    case 'chat.too_many_attachments':
      return $localize`:@@error.chatTooManyImages:You can send at most 5 photos in one message.`;
    case 'chat.attachment_unavailable':
      return $localize`:@@error.chatImageUnavailable:A photo is no longer available. Remove it and add it again.`;
    case 'chat.email_not_confirmed':
      return $localize`:@@error.chatEmailNotConfirmed:Please confirm your email address first.`;
    case 'files.too_large':
      return $localize`:@@error.fileTooLarge:The photo is too large. The limit is 10 MB.`;
    case 'files.unsupported_type':
      return $localize`:@@error.fileType:Only JPEG, PNG and WebP photos can be sent.`;
    case 'files.too_many_pixels':
      return $localize`:@@error.fileTooManyPixels:The photo has too many pixels. Please send a smaller one.`;
    case 'files.unreadable':
    case 'files.missing':
      return $localize`:@@error.fileUnreadable:The photo could not be read. Please try another one.`;
    case 'orders.not_found':
      return $localize`:@@error.orderNotFound:This order does not exist or is not available.`;
    case 'orders.conflict':
      return $localize`:@@error.orderConflict:Someone else has just changed this order. The page now shows the latest version: check it and try again.`;
    case 'orders.invalid_transition':
      return $localize`:@@error.orderInvalidTransition:This step is not possible in the order's current status.`;
    case 'orders.not_editable':
      return $localize`:@@error.orderNotEditable:The terms of the order cannot change any more.`;
    case 'orders.photo_required':
      return $localize`:@@error.orderPhotoRequired:Add at least one photo of the finished toy first.`;
    case 'orders.photos_locked':
      return $localize`:@@error.orderPhotosLocked:Photos can be changed only while the toy is being made or is ready.`;
    case 'orders.reason_required':
      return $localize`:@@error.orderReasonRequired:Write the reason for the customer.`;
    case 'orders.title_required':
      return $localize`:@@error.orderTitleRequired:The order needs a title.`;
    case 'orders.too_long':
      return $localize`:@@error.orderTooLong:A text is too long.`;
    case 'orders.invalid_price':
      return $localize`:@@error.orderInvalidPrice:Enter a price above zero, with at most two decimals.`;
    case 'orders.too_many_photos':
      return $localize`:@@error.orderTooManyPhotos:An order can have at most 20 photos.`;
    case 'orders.photo_not_found':
      return $localize`:@@error.orderPhotoNotFound:This photo has already been removed.`;
    case 'orders.conversation_not_found':
      return $localize`:@@error.orderConversationNotFound:The conversation does not exist any more.`;
    case 'orders.image_unavailable':
      return $localize`:@@error.orderImageUnavailable:This image cannot be used for the order. Choose one from the conversation or upload it again.`;
    case 'rate_limited':
      return $localize`:@@error.rateLimited:Too many attempts. Please wait a minute and try again.`;
    case 'validation_failed':
      return $localize`:@@error.validation:Some fields are not filled in correctly.`;
    case NETWORK_ERROR:
      return $localize`:@@error.network:No connection to the server. Check your internet connection and try again.`;
    default:
      return $localize`:@@error.unknown:Something went wrong. Please try again later.`;
  }
}

function passwordMessage(codes: readonly string[]): string {
  return codes.includes('PasswordTooShort')
    ? $localize`:@@error.passwordTooShort:The password must have at least 12 characters.`
    : $localize`:@@error.passwordRejected:Please choose a different password.`;
}
