import { NgOptimizedImage } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ContentImage } from '../../content/image';

/**
 * Swipeable image carousel built on CSS scroll-snap: touch and trackpad scrolling are native,
 * and the buttons, dots and arrow keys just scroll the track.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgOptimizedImage],
  selector: 'app-carousel',
  styleUrl: './carousel.scss',
  templateUrl: './carousel.html',
})
export class Carousel {
  readonly items = input.required<readonly ContentImage[]>();
  /** Accessible name of the carousel region. */
  readonly label = input.required<string>();
  /** Emits the index of the slide the user wants to see fullscreen. */
  readonly opened = output<number>();

  private readonly track = viewChild.required<ElementRef<HTMLElement>>('track');

  protected readonly index = signal(0);
  protected readonly atStart = computed(() => this.index() === 0);
  protected readonly atEnd = computed(() => this.index() >= this.items().length - 1);

  protected readonly previousLabel = $localize`:@@carousel.previous:Previous image`;
  protected readonly nextLabel = $localize`:@@carousel.next:Next image`;

  protected slideLabel(position: number): string {
    const total = this.items().length;

    return $localize`:@@carousel.slideLabel:Image ${position + 1}:INDEX: of ${total}:TOTAL:`;
  }

  protected openLabel(alt: string): string {
    return $localize`:@@carousel.open:View fullscreen: ${alt}:DESCRIPTION:`;
  }

  protected goToLabel(position: number): string {
    return $localize`:@@carousel.goTo:Go to image ${position + 1}:INDEX:`;
  }

  protected previous(): void {
    this.goTo(this.index() - 1);
  }

  protected next(): void {
    this.goTo(this.index() + 1);
  }

  protected goTo(position: number): void {
    const track = this.track().nativeElement;
    const target = Math.max(0, Math.min(position, this.items().length - 1));
    const slide = track.children.item(target);

    if (slide instanceof HTMLElement) {
      track.scrollTo({ left: slide.offsetLeft - track.offsetLeft });
    }

    this.index.set(target);
  }

  /**
   * Keeps the dots in sync when the user swipes or scrolls the track directly.
   * It reads the DOM only: the browser can fire `scroll` while the page is still hydrating,
   * before the inputs are bound.
   */
  protected onScroll(): void {
    const track = this.track().nativeElement;
    const slideWidth = track.firstElementChild?.clientWidth ?? 0;

    if (slideWidth > 0) {
      const position = Math.round(track.scrollLeft / slideWidth);
      this.index.set(Math.max(0, Math.min(position, track.childElementCount - 1)));
    }
  }
}
