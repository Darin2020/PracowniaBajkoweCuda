import { ChangeDetectionStrategy, Component, computed, input, viewChild } from '@angular/core';
import { ContentImage } from '../../content/image';
import { ChatImage } from '../../core/chat/chat.models';
import { ImageViewer } from '../image-viewer/image-viewer';

/** Thumbnails of private images (`/api/files`) that open in the fullscreen viewer. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ImageViewer],
  selector: 'app-photo-gallery',
  host: { '[class.single]': 'images().length === 1' },
  styles: `
    ul {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(7rem, 1fr));
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    button {
      display: block;
      width: 100%;
      padding: 0;
      overflow: hidden;
      border: 0;
      border-radius: var(--radius-small);
      background: var(--color-border);
      cursor: zoom-in;
    }

    img {
      display: block;
      width: 100%;
      height: auto;
      aspect-ratio: 1;
      object-fit: cover;
    }

    :host(.single) ul {
      grid-template-columns: minmax(0, 16rem);
    }

    :host(.single) img {
      aspect-ratio: auto;
    }
  `,
  template: `
    <ul>
      @for (image of images(); track image.id; let position = $index) {
        <li>
          <button type="button" (click)="viewer().open(position)">
            <img
              [src]="image.thumbnailUrl"
              [width]="image.width"
              [height]="image.height"
              loading="lazy"
              [alt]="viewerItems()[position].alt"
            />
          </button>
        </li>
      }
    </ul>
    <app-image-viewer [items]="viewerItems()" />
  `,
})
export class PhotoGallery {
  readonly images = input.required<readonly ChatImage[]>();

  protected readonly viewer = viewChild.required(ImageViewer);
  protected readonly viewerItems = computed<ContentImage[]>(() => {
    const total = this.images().length;
    return this.images().map((image, index) => ({
      src: image.url,
      width: image.width,
      height: image.height,
      alt: $localize`:@@chat.viewerPhotoAlt:Photo ${index + 1}:INDEX: of ${total}:TOTAL:`,
    }));
  });
}
