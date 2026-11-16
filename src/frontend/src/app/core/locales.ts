/** Locales the site is built for. Each one is served under its own URL prefix (`/en/`, `/de/`, ...). */
export const SUPPORTED_LOCALES = ['en', 'de', 'fr', 'es', 'pl'] as const;

export type AppLocale = (typeof SUPPORTED_LOCALES)[number];

export const DEFAULT_LOCALE: AppLocale = 'en';

/** Language names in their own language. They are deliberately not translated. */
export const LOCALE_NAMES: Record<AppLocale, string> = {
  en: 'English',
  de: 'Deutsch',
  fr: 'Français',
  es: 'Español',
  pl: 'Polski',
};

/** Open Graph locale codes (`og:locale`). */
export const OPEN_GRAPH_LOCALES: Record<AppLocale, string> = {
  en: 'en_GB',
  de: 'de_DE',
  fr: 'fr_FR',
  es: 'es_ES',
  pl: 'pl_PL',
};

/** Maps Angular's `LOCALE_ID` (for example `en-US` in the dev server) to a supported locale. */
export function toAppLocale(localeId: string): AppLocale {
  const language = localeId.toLowerCase().split('-')[0];

  return SUPPORTED_LOCALES.find((locale) => locale === language) ?? DEFAULT_LOCALE;
}
