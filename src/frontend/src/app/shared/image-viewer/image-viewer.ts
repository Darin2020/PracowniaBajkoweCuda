import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ContentImage } from '../../content/image';

/**
 * Fullscreen image viewer on the native `<dialog>` element, which provides the focus trap,
 * the Escape key and the inert background for free.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Arrow keys bubble up from the focused button inside the dialog.
  host: { '(keydown.arrowLeft)': 'previous()', '(keydown.arrowRight)': 'next()' },
  selector: 'app-image-viewer',
  styleUrl: './image-viewer.scss',
  template: `
    <dialog
      #dialog
      class="viewer"
      aria-label="Image viewer"
      i18n-aria-label="@@viewer.label"
      (close)="isOpen.set(false)"
    >
      @if (isOpen() && current(); as image) {
        <img
          class="viewer__image"
          [src]="image.src"
          [alt]="image.alt"
          [width]="image.width"
          [height]="image.height"
        />
        <p class="viewer__caption">{{ image.alt }}</p>
      }

      <div class="viewer__controls">
        <button type="button" class="button" (click)="previous()" i18n="@@viewer.previous">
          Previous
        </button>
        <button
          type="button"
          class="button button--primary"
          (click)="close()"
          i18n="@@viewer.close"
        >
          Close
        </button>
        <button type="button" class="button" (click)="next()" i18n="@@viewer.next">Next</button>
      </div>
    </dialog>
  `,
})
export class ImageViewer {
  readonly items = input.required<readonly ContentImage[]>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly index = signal(0);
  protected readonly isOpen = signal(false);
  protected readonly current = computed(() => this.items().at(this.index()));

  open(position: number): void {
    this.index.set(position);
    this.isOpen.set(true);
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  protected previous(): void {
    this.step(-1);
  }

  protected next(): void {
    this.step(1);
  }

  /** Moves to the neighbouring image, wrapping around at both ends. */
  private step(delta: number): void {
    const total = this.items().length;

    if (total > 0) {
      this.index.update((position) => (position + delta + total) % total);
    }
  }
}
