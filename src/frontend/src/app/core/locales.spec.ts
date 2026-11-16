import { toAppLocale } from './locales';

describe('toAppLocale', () => {
  it.each([
    ['en', 'en'],
    ['de', 'de'],
    ['pl', 'pl'],
    ['en-US', 'en'],
    ['fr-CA', 'fr'],
    ['ES', 'es'],
  ])('maps %s to %s', (localeId, expected) => {
    expect(toAppLocale(localeId)).toBe(expected);
  });

  it('falls back to English for an unsupported language', () => {
    expect(toAppLocale('it-IT')).toBe('en');
  });
});
