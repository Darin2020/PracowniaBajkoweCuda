import { DOCUMENT, Injectable, LOCALE_ID, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { SITE } from '../content/site';
import { DEFAULT_LOCALE, OPEN_GRAPH_LOCALES, SUPPORTED_LOCALES, toAppLocale } from './locales';
import { PUBLIC_BASE_URL } from './public-base-url';

export interface PageSeo {
  /** Page title without the site name. Omit it on the landing page. */
  readonly title?: string;
  readonly description: string;
  /** Path inside the locale, without slashes: `''` for the landing page, `'privacy'`, ... */
  readonly path: string;
  /** Builds the schema.org structured data for the page from its absolute URLs. */
  readonly structuredData?: (urls: PageUrls) => object;
}

export interface PageUrls {
  /** Site origin, for example `https://example.com`. */
  readonly baseUrl: string;
  /** Canonical URL of the current page in the current locale. */
  readonly pageUrl: string;
  /** Root URL of the current locale, with a trailing slash. */
  readonly localeUrl: string;
  readonly imageUrl: string;
}

const STRUCTURED_DATA_ID = 'structured-data';
const SHARE_IMAGE = 'og-image.png';

/** Writes the per-page head tags: title, description, canonical, hreflang, Open Graph and JSON-LD. */
@Injectable({ providedIn: 'root' })
export class Seo {
  private readonly document = inject(DOCUMENT);
  private readonly meta = inject(Meta);
  private readonly titleService = inject(Title);
  private readonly baseUrl = inject(PUBLIC_BASE_URL);
  private readonly locale = toAppLocale(inject(LOCALE_ID));

  /** Describes an indexable public page. */
  setPage(page: PageSeo): void {
    const urls = this.urlsFor(page.path);
    const title = page.title ? `${page.title} · ${SITE.name}` : `${SITE.name} · ${SITE.tagline}`;

    this.titleService.setTitle(title);
    this.meta.updateTag({ name: 'description', content: page.description });
    this.meta.updateTag({ name: 'robots', content: 'index, follow' });

    this.meta.updateTag({ property: 'og:type', content: 'website' });
    this.meta.updateTag({ property: 'og:site_name', content: SITE.name });
    this.meta.updateTag({ property: 'og:title', content: title });
    this.meta.updateTag({ property: 'og:description', content: page.description });
    this.meta.updateTag({ property: 'og:url', content: urls.pageUrl });
    this.meta.updateTag({ property: 'og:image', content: urls.imageUrl });
    this.meta.updateTag({ property: 'og:image:width', content: '1200' });
    this.meta.updateTag({ property: 'og:image:height', content: '630' });
    this.meta.updateTag({ property: 'og:locale', content: OPEN_GRAPH_LOCALES[this.locale] });

    this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    this.meta.updateTag({ name: 'twitter:title', content: title });
    this.meta.updateTag({ name: 'twitter:description', content: page.description });
    this.meta.updateTag({ name: 'twitter:image', content: urls.imageUrl });

    this.setCanonical(urls.pageUrl);
    this.setAlternates(page.path);
    this.setStructuredData(page.structuredData?.(urls));
  }

  /** Describes a page that search engines must not index (client-rendered and error pages). */
  setNoIndexPage(title: string): void {
    this.titleService.setTitle(`${title} · ${SITE.name}`);
    this.meta.updateTag({ name: 'robots', content: 'noindex, nofollow' });
    this.meta.removeTag('name="description"');

    this.removeAll('link[rel="canonical"]');
    this.removeAll('link[rel="alternate"][hreflang]');
    this.setStructuredData(undefined);
  }

  private urlsFor(path: string): PageUrls {
    return {
      baseUrl: this.baseUrl,
      pageUrl: this.urlOf(this.locale, path),
      localeUrl: this.urlOf(this.locale, ''),
      imageUrl: `${this.baseUrl}/${this.locale}/${SHARE_IMAGE}`,
    };
  }

  private urlOf(locale: string, path: string): string {
    return `${this.baseUrl}/${locale}/${path}`;
  }

  private setCanonical(url: string): void {
    this.removeAll('link[rel="canonical"]');
    this.appendLink({ rel: 'canonical', href: url });
  }

  private setAlternates(path: string): void {
    this.removeAll('link[rel="alternate"][hreflang]');

    for (const locale of SUPPORTED_LOCALES) {
      this.appendLink({ rel: 'alternate', hreflang: locale, href: this.urlOf(locale, path) });
    }

    this.appendLink({
      rel: 'alternate',
      hreflang: 'x-default',
      href: this.urlOf(DEFAULT_LOCALE, path),
    });
  }

  private setStructuredData(data: object | undefined): void {
    this.document.getElementById(STRUCTURED_DATA_ID)?.remove();

    if (!data) {
      return;
    }

    const script = this.document.createElement('script');
    script.id = STRUCTURED_DATA_ID;
    script.type = 'application/ld+json';
    // `<` is escaped so that content can never close the script element.
    script.textContent = JSON.stringify(data).replace(/</g, '\\u003c');
    this.document.head.appendChild(script);
  }

  private appendLink(attributes: Record<string, string>): void {
    const link = this.document.createElement('link');

    for (const [name, value] of Object.entries(attributes)) {
      link.setAttribute(name, value);
    }

    this.document.head.appendChild(link);
  }

  private removeAll(selector: string): void {
    this.document.head.querySelectorAll(selector).forEach((element) => element.remove());
  }
}
