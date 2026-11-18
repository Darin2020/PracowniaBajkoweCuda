/**
 * Return URLs are app paths like "/chat" (without the locale prefix). Only local paths are accepted, the same rule as
 * the API's (open-redirect protection, docs/PLAN.md §6): "/chat" yes; "//evil", "/\evil", "https://evil" no.
 */
export function safeReturnUrl(value: unknown): string | null {
  if (typeof value !== 'string' || value.length === 0 || value.length > 512 || value[0] !== '/') {
    return null;
  }

  if (value.length > 1 && (value[1] === '/' || value[1] === '\\')) {
    return null;
  }

  for (const character of value) {
    const code = character.charCodeAt(0);
    if (code < 0x20 || code === 0x7f || character === '\\') {
      return null;
    }
  }

  // The auth pages themselves are never a destination after signing in.
  return /^\/(login|register|complete-profile)(\/|\?|$)/.test(value) ? null : value;
}
