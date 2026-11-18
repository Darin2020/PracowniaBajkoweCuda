import { HttpErrorResponse } from '@angular/common/http';
import { ApiError, NETWORK_ERROR, errorMessage, toApiError } from './api-error';

describe('toApiError', () => {
  it('takes the code and the field errors of a problem response', () => {
    const error = toApiError(
      new HttpErrorResponse({
        status: 400,
        error: { code: 'auth.password_rejected', errors: { password: ['PasswordTooShort'] } },
      }),
    );

    expect(error.status).toBe(400);
    expect(error.code).toBe('auth.password_rejected');
    expect(error.errors).toEqual({ password: ['PasswordTooShort'] });
  });

  it('reports a request without an answer as a network error', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 })).code).toBe(NETWORK_ERROR);
  });

  it('recognises a rate limit without a problem body', () => {
    expect(toApiError(new HttpErrorResponse({ status: 429, error: 'Too many' })).code).toBe(
      'rate_limited',
    );
  });

  it('keeps an ApiError as it is', () => {
    const error = new ApiError(409, 'auth.last_login');

    expect(toApiError(error)).toBe(error);
  });
});

describe('errorMessage', () => {
  it('translates codes of the API and of redirects', () => {
    expect(errorMessage(new ApiError(401, 'auth.invalid_credentials'))).toContain('password');
    expect(errorMessage('account_exists')).toBe(
      errorMessage(new ApiError(409, 'auth.account_exists')),
    );
  });

  it('explains a too short password', () => {
    const error = new ApiError(400, 'auth.password_rejected', { password: ['PasswordTooShort'] });

    expect(errorMessage(error)).toContain('12');
  });

  it('falls back to a generic message for unknown codes', () => {
    expect(errorMessage('something.new')).toBe(errorMessage('unknown'));
  });
});
