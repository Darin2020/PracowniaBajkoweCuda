/** A static image shipped with the app. `src` is relative to the locale root (the `public/` folder). */
export interface ContentImage {
  readonly src: string;
  readonly alt: string;
  readonly width: number;
  readonly height: number;
}
