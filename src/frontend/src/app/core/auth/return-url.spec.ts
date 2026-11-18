import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/chat', '/orders/42?tab=photos', '/account#data'])(
    'accepts the local path %s',
    (url) => {
      expect(safeReturnUrl(url)).toBe(url);
    },
  );

  it.each([
    undefined,
    '',
    'chat',
    'https://evil.test/',
    '//evil.test',
    '/\\evil.test',
    '/chat\\..',
    '/chat\nSet-Cookie: x',
    `/${'a'.repeat(600)}`,
  ])('rejects %j', (url) => {
    expect(safeReturnUrl(url)).toBeNull();
  });

  it.each(['/login', '/login/2fa', '/register?x=1', '/complete-profile'])(
    'never returns to the auth page %s',
    (url) => {
      expect(safeReturnUrl(url)).toBeNull();
    },
  );
});
