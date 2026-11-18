import { signal } from '@angular/core';
import { AbstractControl, FormGroup } from '@angular/forms';
import { ApiError, errorMessage, toApiError } from '../../core/api/api-error';

/** The state of a form that calls the API: busy while the request runs, then the localized error, if any. */
export class FormState {
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  /** The last error itself, for pages that react to a particular code. */
  readonly apiError = signal<ApiError | null>(null);

  /**
   * Runs the action unless one is already running, and returns whether it succeeded. With a form, an invalid form
   * shows its field errors instead of sending anything.
   */
  async run(action: () => Promise<unknown>, form?: FormGroup): Promise<boolean> {
    if (this.busy()) {
      return false;
    }

    if (form?.invalid) {
      form.markAllAsTouched();
      return false;
    }

    this.busy.set(true);
    this.error.set(null);
    this.apiError.set(null);
    try {
      await action();
      return true;
    } catch (caught) {
      const error = toApiError(caught);
      this.apiError.set(error);
      this.error.set(errorMessage(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

export const PASSWORD_MIN_LENGTH = 12;

/** The message under a field, once the user has left it or tried to send the form. */
export function fieldError(control: AbstractControl): string | null {
  if (!control.invalid || !control.touched) {
    return null;
  }

  if (control.hasError('required')) {
    // Validators.requiredTrue reports `required` too: an unticked checkbox is the terms checkbox.
    return typeof control.value === 'boolean'
      ? $localize`:@@form.termsRequired:Please accept the terms to continue.`
      : $localize`:@@form.required:Please fill in this field.`;
  }

  if (control.hasError('email')) {
    return $localize`:@@form.email:Enter a valid email address.`;
  }

  if (control.hasError('minlength')) {
    return $localize`:@@error.passwordTooShort:The password must have at least 12 characters.`;
  }

  return $localize`:@@error.validation:Some fields are not filled in correctly.`;
}
