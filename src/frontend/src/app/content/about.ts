import { ContentImage } from './image';

/** About section. Texts and the photo are PLACEHOLDERS until the owner provides the real ones. */
export const ABOUT = {
  heading: $localize`:@@about.heading:Toys with a story`,
  paragraphs: [
    $localize`:@@about.paragraph1:Every toy starts with a conversation. Tell us who it is for and what it should look like, send a drawing or a photo, and we will turn the idea into a soft friend.`,
    $localize`:@@about.paragraph2:Each toy is sewn by hand in a small home studio, from safe, certified fabrics and fillings. No two are the same.`,
    $localize`:@@about.paragraph3:You follow your order from the first sketch to the finished toy, with photos along the way.`,
  ],
  photo: {
    src: 'images/maker.svg',
    alt: $localize`:@@about.photoAlt:Placeholder illustration of the maker at her sewing table`,
    width: 800,
    height: 800,
  } satisfies ContentImage,
} as const;
