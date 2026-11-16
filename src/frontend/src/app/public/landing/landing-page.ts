import { CurrencyPipe, NgOptimizedImage } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ABOUT } from '../../content/about';
import { GALLERY } from '../../content/gallery';
import { CURRENCY, PRICING_NOTE, PRICING_PACKAGES } from '../../content/pricing';
import { SITE } from '../../content/site';
import { ActiveSection } from '../../core/active-section';
import { Seo } from '../../core/seo';
import { Carousel } from '../../shared/carousel/carousel';
import { ImageViewer } from '../../shared/image-viewer/image-viewer';
import { buildLandingStructuredData } from './landing-structured-data';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Carousel, CurrencyPipe, ImageViewer, NgOptimizedImage, RouterLink],
  selector: 'app-landing-page',
  styleUrl: './landing-page.scss',
  templateUrl: './landing-page.html',
})
export class LandingPage {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly activeSection = inject(ActiveSection);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly site = SITE;
  protected readonly about = ABOUT;
  protected readonly gallery = GALLERY;
  protected readonly packages = PRICING_PACKAGES;
  protected readonly pricingNote = PRICING_NOTE;
  protected readonly currency = CURRENCY;
  protected readonly galleryLabel = $localize`:@@gallery.carouselLabel:Gallery of finished toys`;

  constructor() {
    inject(Seo).setPage({
      description: SITE.description,
      path: '',
      structuredData: buildLandingStructuredData,
    });

    afterNextRender(() => this.trackActiveSection());
  }

  /** Reports the section crossing the middle of the viewport, for the top-bar highlight. */
  private trackActiveSection(): void {
    const sections = this.host.nativeElement.querySelectorAll('section[id]');
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            this.activeSection.id.set(entry.target.id);
          }
        }
      },
      { rootMargin: '-45% 0px -50% 0px' },
    );

    sections.forEach((section) => observer.observe(section));

    this.destroyRef.onDestroy(() => {
      observer.disconnect();
      this.activeSection.id.set(null);
    });
  }
}
