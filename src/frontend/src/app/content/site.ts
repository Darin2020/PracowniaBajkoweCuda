/**
 * Site-wide content. Contact details are PLACEHOLDERS until the owner provides the real ones
 * (docs/PLAN.md §16). Changing content = editing the files in this folder and redeploying.
 */
export interface SocialLink {
  readonly name: string;
  readonly url: string;
}

export const SITE = {
  name: 'Pracownia Bajkowe Cuda',
  tagline: $localize`:@@site.tagline:Hand-sewn toys, made to order`,
  description: $localize`:@@site.description:Pracownia Bajkowe Cuda sews soft toys to order: from a drawing, a photo or just an idea. Write to us and we will agree on every detail together.`,
  email: 'hello@example.com',
  phone: '+48 000 000 000',
  phoneHref: 'tel:+48000000000',
  socials: [
    { name: 'Instagram', url: 'https://www.instagram.com/' },
    { name: 'Facebook', url: 'https://www.facebook.com/' },
  ] as readonly SocialLink[],
} as const;
