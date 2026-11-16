import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  LOCALE_ID,
  computed,
  inject,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { AppLocale, LOCALE_NAMES, SUPPORTED_LOCALES, toAppLocale } from '../../core/locales';

/** Name of the cookie the server reads to pick the language for `/` (strictly necessary, 1 year). */
export const LANGUAGE_COOKIE = 'lang';
const ONE_YEAR_IN_SECONDS = 365 * 24 * 60 * 60;

interface LanguageLink {
  readonly code: AppLocale;
  readonly name: string;
  readonly href: string;
  readonly current: boolean;
}

/**
 * Links to the same page in every locale. Each locale is a separately built app under its own URL
 * prefix, so switching is a full page load, not a router navigation.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-language-switcher',
  styleUrl: './language-switcher.scss',
  template: `
    <nav class="languages" aria-label="Language" i18n-aria-label="@@language.label">
      <ul class="languages__list">
        @for (link of links(); track link.code) {
          <li>
            <a
              class="languages__link"
              [class.languages__link--current]="link.current"
              [href]="link.href"
              [attr.hreflang]="link.code"
              [attr.lang]="link.code"
              [attr.aria-current]="link.current ? 'true' : null"
              (click)="remember(link.code)"
            >
              <span aria-hidden="true">{{ link.code }}</span>
              <span class="visually-hidden">{{ link.name }}</span>
            </a>
          </li>
        }
      </ul>
    </nav>
  `,
})
export class LanguageSwitcher {
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly currentLocale = toAppLocale(inject(LOCALE_ID));

  /** Router URL without query and fragment, for example `/` or `/privacy`. */
  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.currentPath()),
    ),
    { initialValue: this.currentPath() },
  );

  protected readonly links = computed<LanguageLink[]>(() =>
    SUPPORTED_LOCALES.map((code) => ({
      code,
      name: LOCALE_NAMES[code],
      href: `/${code}${this.path()}`,
      current: code === this.currentLocale,
    })),
  );

  protected remember(locale: AppLocale): void {
    const secure = this.document.location.protocol === 'https:' ? '; Secure' : '';

    this.document.cookie = `${LANGUAGE_COOKIE}=${locale}; Path=/; Max-Age=${ONE_YEAR_IN_SECONDS}; SameSite=Lax${secure}`;
  }

  private currentPath(): string {
    return this.router.url.split(/[?#]/)[0] || '/';
  }
}
