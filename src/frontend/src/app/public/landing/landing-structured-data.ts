import { CURRENCY, PRICING_PACKAGES } from '../../content/pricing';
import { SITE } from '../../content/site';
import { PageUrls } from '../../core/seo';

/** schema.org `LocalBusiness` + `Service` with one `Offer` per pricing package (JSON-LD). */
export function buildLandingStructuredData(urls: PageUrls): object {
  const businessId = `${urls.baseUrl}/#business`;

  return {
    '@context': 'https://schema.org',
    '@graph': [
      {
        '@type': 'LocalBusiness',
        '@id': businessId,
        name: SITE.name,
        description: SITE.description,
        url: urls.localeUrl,
        image: urls.imageUrl,
        email: SITE.email,
        telephone: SITE.phone,
        currenciesAccepted: CURRENCY,
        sameAs: SITE.socials.map((social) => social.url),
      },
      {
        '@type': 'Service',
        name: SITE.tagline,
        serviceType: 'Custom toy sewing',
        provider: { '@id': businessId },
        url: urls.pageUrl,
        offers: PRICING_PACKAGES.map((item) => ({
          '@type': 'Offer',
          name: item.name,
          description: item.description,
          priceCurrency: CURRENCY,
          priceSpecification: {
            '@type': 'PriceSpecification',
            priceCurrency: CURRENCY,
            minPrice: item.priceFrom,
            ...(item.priceTo === undefined ? {} : { maxPrice: item.priceTo }),
          },
        })),
      },
    ],
  };
}
