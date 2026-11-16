/** Currency used everywhere on the site (docs/PLAN.md §2). */
export const CURRENCY = 'EUR';

export interface PricingPackage {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  /** Lower bound in EUR. */
  readonly priceFrom: number;
  /** Upper bound in EUR. Omitted for open-ended "from" prices. */
  readonly priceTo?: number;
}

/** Service packages. Names and prices are PLACEHOLDERS until the owner provides the real ones. */
export const PRICING_PACKAGES: readonly PricingPackage[] = [
  {
    id: 'small',
    name: $localize`:@@pricing.small.name:Small toy`,
    description: $localize`:@@pricing.small.description:Up to 20 cm. A simple shape with embroidered details.`,
    priceFrom: 35,
    priceTo: 60,
  },
  {
    id: 'medium',
    name: $localize`:@@pricing.medium.name:Medium toy`,
    description: $localize`:@@pricing.medium.description:20 to 40 cm. Clothes, accessories or movable limbs.`,
    priceFrom: 60,
    priceTo: 120,
  },
  {
    id: 'large',
    name: $localize`:@@pricing.large.name:Large or complex toy`,
    description: $localize`:@@pricing.large.description:Over 40 cm, or a detailed character with many elements.`,
    priceFrom: 120,
  },
  {
    id: 'drawing',
    name: $localize`:@@pricing.drawing.name:Toy from a child's drawing`,
    description: $localize`:@@pricing.drawing.description:We recreate the drawing as faithfully as we can, colours and quirks included.`,
    priceFrom: 80,
  },
];

export const PRICING_NOTE = $localize`:@@pricing.note:These are price ranges. The final price depends on the size, materials and details, and is agreed with you in the chat before we start.`;
