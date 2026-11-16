import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentImage } from '../../content/image';
import { Carousel } from './carousel';

const IMAGES: readonly ContentImage[] = [
  { src: 'gallery/one.svg', alt: 'First toy', width: 800, height: 600 },
  { src: 'gallery/two.svg', alt: 'Second toy', width: 800, height: 600 },
  { src: 'gallery/three.svg', alt: 'Third toy', width: 800, height: 600 },
];

describe('Carousel', () => {
  let fixture: ComponentFixture<Carousel>;
  let element: HTMLElement;

  const arrows = () => Array.from(element.querySelectorAll<HTMLButtonElement>('.arrow'));
  const dots = () => Array.from(element.querySelectorAll<HTMLButtonElement>('.dot'));
  const currentDot = () => dots().findIndex((dot) => dot.getAttribute('aria-current') === 'true');

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [Carousel] });
    fixture = TestBed.createComponent(Carousel);
    fixture.componentRef.setInput('items', IMAGES);
    fixture.componentRef.setInput('label', 'Gallery');
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;

    // jsdom has no layout engine, so scrolling is a no-op there.
    element.querySelector<HTMLElement>('.track')!.scrollTo = () => undefined;
  });

  it('renders one labelled slide and one dot per image', () => {
    const slides = Array.from(element.querySelectorAll('.slide'));

    expect(slides.map((slide) => slide.getAttribute('aria-label'))).toEqual([
      'Image 1 of 3',
      'Image 2 of 3',
      'Image 3 of 3',
    ]);
    expect(dots()).toHaveLength(3);
    expect(element.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Gallery');
  });

  it('gives every image its alternative text and intrinsic size', () => {
    const image = element.querySelector('img')!;

    expect(image.getAttribute('alt')).toBe('First toy');
    expect(image.getAttribute('width')).toBe('800');
    expect(image.getAttribute('height')).toBe('600');
  });

  it('starts on the first image with "previous" disabled', () => {
    expect(currentDot()).toBe(0);
    expect(arrows()[0].disabled).toBe(true);
    expect(arrows()[1].disabled).toBe(false);
  });

  it('moves with the arrows and stops at the last image', () => {
    const [, next] = arrows();

    next.click();
    next.click();
    fixture.detectChanges();

    expect(currentDot()).toBe(2);
    expect(arrows()[1].disabled).toBe(true);

    arrows()[0].click();
    fixture.detectChanges();

    expect(currentDot()).toBe(1);
  });

  it('jumps to an image when its dot is clicked', () => {
    dots()[2].click();
    fixture.detectChanges();

    expect(currentDot()).toBe(2);
  });

  it('moves with the arrow keys', () => {
    const track = element.querySelector<HTMLElement>('.track')!;

    track.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();
    expect(currentDot()).toBe(1);

    track.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    fixture.detectChanges();
    expect(currentDot()).toBe(0);
  });

  it('asks to open the clicked image fullscreen', () => {
    const opened: number[] = [];
    fixture.componentInstance.opened.subscribe((index) => opened.push(index));

    element.querySelectorAll<HTMLButtonElement>('.slide__button')[1].click();

    expect(opened).toEqual([1]);
  });
});
