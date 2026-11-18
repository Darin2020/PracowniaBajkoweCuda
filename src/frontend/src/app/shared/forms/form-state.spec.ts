import { FormControl, FormGroup, Validators } from '@angular/forms';
import { ApiError } from '../../core/api/api-error';
import { FormState, fieldError } from './form-state';

describe('fieldError', () => {
  const touched = <T>(control: FormControl<T>) => {
    control.markAsTouched();
    return control;
  };

  it('stays quiet until the field was touched', () => {
    expect(fieldError(new FormControl('', Validators.required))).toBeNull();
  });

  it('names the problem of a touched field', () => {
    expect(fieldError(touched(new FormControl('', Validators.required)))).toContain('fill in');
    expect(fieldError(touched(new FormControl('nope', Validators.email)))).toContain('valid email');
    expect(fieldError(touched(new FormControl('short', Validators.minLength(12))))).toContain('12');
  });

  it('asks to accept the terms for an unticked checkbox', () => {
    expect(fieldError(touched(new FormControl(false, Validators.requiredTrue)))).toContain(
      'accept the terms',
    );
  });
});

describe('FormState', () => {
  it('does not send an invalid form', async () => {
    const state = new FormState();
    const form = new FormGroup({ email: new FormControl('', Validators.required) });
    const action = vi.fn(() => Promise.resolve());

    expect(await state.run(action, form)).toBe(false);
    expect(action).not.toHaveBeenCalled();
    expect(form.controls.email.touched).toBe(true);
  });

  it('keeps the translated error of a failed call', async () => {
    const state = new FormState();

    expect(await state.run(() => Promise.reject(new ApiError(429, 'rate_limited')))).toBe(false);
    expect(state.apiError()?.code).toBe('rate_limited');
    expect(state.error()).toContain('Too many attempts');
    expect(state.busy()).toBe(false);
  });
});
