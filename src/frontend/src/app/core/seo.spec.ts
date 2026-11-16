import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PUBLIC_BASE_URL, normalizeBaseUrl } from './public-base-url';
import { Seo } from './seo';

describe('Seo', () => {
  let seo: Seo;

  const head = () => document.head;
  const content = (selector: string) => head().querySelector(selector)?.getAttribute('content');
  const hrefs = (selector: string) =>
    Array.from(head().querySelectorAll(selector)).map((link) => link.getAttribute('href'));

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: LOCALE_ID, useValue: 'de' },
        { provide: PUBLIC_BASE_URL, useValue: 'https://toys.test' },
      ],
    });
    seo = TestBed.inject(Seo);
  });

  afterEach(() => {
    head()
      .querySelectorAll('link[rel="canonical"], link[rel="alternate"], meta, script')
      .forEach((element) => element.remove());
  });

  it('writes the canonical URL of the page in the current locale', () => {
    seo.setPage({ title: 'Privacy', description: 'About data.', path: 'privacy' });

    expect(hrefs('link[rel="canonical"]')).toEqual(['https://toys.test/de/privacy']);
    expect(content('meta[property="og:url"]')).toBe('https://toys.test/de/privacy');
  });

  it('writes one hreflang alternate per locale plus x-default pointing at English', () => {
    seo.setPage({ description: 'Landing.', path: '' });

    const alternates = Array.from(head().querySelectorAll('link[rel="alternate"]')).map((link) => [
      link.getAttribute('hreflang'),
      link.getAttribute('href'),
    ]);

    expect(alternates).toEqual([
      ['en', 'https://toys.test/en/'],
      ['de', 'https://toys.test/de/'],
      ['fr', 'https://toys.test/fr/'],
      ['es', 'https://toys.test/es/'],
      ['pl', 'https://toys.test/pl/'],
      ['x-default', 'https://toys.test/en/'],
    ]);
  });

  it('does not duplicate head tags when the page changes', () => {
    seo.setPage({ description: 'Landing.', path: '' });
    seo.setPage({ title: 'Terms', description: 'Rules.', path: 'terms' });

    expect(hrefs('link[rel="canonical"]')).toEqual(['https://toys.test/de/terms']);
    expect(head().querySelectorAll('link[rel="alternate"]')).toHaveLength(6);
    expect(head().querySelectorAll('meta[name="description"]')).toHaveLength(1);
    expect(content('meta[name="description"]')).toBe('Rules.');
  });

  it('sets the title, description and share tags', () => {
    seo.setPage({ title: 'Terms', description: 'Rules.', path: 'terms' });

    expect(document.title).toBe('Terms · Pracownia Bajkowe Cuda');
    expect(content('meta[property="og:image"]')).toBe('https://toys.test/de/og-image.png');
    expect(content('meta[property="og:locale"]')).toBe('de_DE');
    expect(content('meta[name="twitter:card"]')).toBe('summary_large_image');
    expect(content('meta[name="robots"]')).toBe('index, follow');
  });

  it('writes structured data as JSON-LD that cannot close its script element', () => {
    seo.setPage({
      description: 'Landing.',
      path: '',
      structuredData: (urls) => ({ url: urls.pageUrl, name: '</script><b>' }),
    });

    const script = head().querySelector('script#structured-data');

    expect(script?.getAttribute('type')).toBe('application/ld+json');
    expect(script?.textContent).not.toContain('</script>');
    expect(JSON.parse(script?.textContent ?? '')).toEqual({
      url: 'https://toys.test/de/',
      name: '</script><b>',
    });
  });

  it('marks client-rendered pages as noindex and removes the indexing tags', () => {
    seo.setPage({ description: 'Landing.', path: '', structuredData: () => ({}) });
    seo.setNoIndexPage('Page not found');

    expect(content('meta[name="robots"]')).toBe('noindex, nofollow');
    expect(head().querySelector('link[rel="canonical"]')).toBeNull();
    expect(head().querySelector('link[rel="alternate"]')).toBeNull();
    expect(head().querySelector('script#structured-data')).toBeNull();
    expect(head().querySelector('meta[name="description"]')).toBeNull();
  });
});

describe('normalizeBaseUrl', () => {
  it('strips trailing slashes', () => {
    expect(normalizeBaseUrl('https://toys.test//')).toBe('https://toys.test');
  });

  it.each([undefined, '', '   '])('falls back to the placeholder origin for %j', (value) => {
    expect(normalizeBaseUrl(value)).toBe('https://example.com');
  });
});
