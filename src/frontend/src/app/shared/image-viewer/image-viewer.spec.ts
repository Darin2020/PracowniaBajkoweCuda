import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentImage } from '../../content/image';
import { ImageViewer } from './image-viewer';

const IMAGES: readonly ContentImage[] = [
  { src: 'gallery/one.svg', alt: 'First toy', width: 800, height: 600 },
  { src: 'gallery/two.svg', alt: 'Second toy', width: 800, height: 600 },
];

describe('ImageViewer', () => {
  let fixture: ComponentFixture<ImageViewer>;
  let element: HTMLElement;
  let dialog: HTMLDialogElement;

  const shownImage = () => element.querySelector('img')?.getAttribute('alt');
  const buttons = () => Array.from(element.querySelectorAll<HTMLButtonElement>('button'));

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ImageViewer] });
    fixture = TestBed.createComponent(ImageViewer);
    fixture.componentRef.setInput('items', IMAGES);
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;
    dialog = element.querySelector('dialog')!;

    // jsdom does not implement modal dialogs.
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => {
      dialog.removeAttribute('open');
      dialog.dispatchEvent(new Event('close'));
    };
  });

  it('loads no image until it is opened', () => {
    expect(element.querySelector('img')).toBeNull();
  });

  it('opens on the requested image', () => {
    fixture.componentInstance.open(1);
    fixture.detectChanges();

    expect(dialog.hasAttribute('open')).toBe(true);
    expect(shownImage()).toBe('Second toy');
  });

  it('steps through the images and wraps around at both ends', () => {
    fixture.componentInstance.open(0);
    fixture.detectChanges();
    const [previous, , next] = buttons();

    previous.click();
    fixture.detectChanges();
    expect(shownImage()).toBe('Second toy');

    next.click();
    fixture.detectChanges();
    expect(shownImage()).toBe('First toy');
  });

  it('closes and unloads the image', () => {
    fixture.componentInstance.open(0);
    fixture.detectChanges();

    buttons()[1].click();
    fixture.detectChanges();

    expect(dialog.hasAttribute('open')).toBe(false);
    expect(element.querySelector('img')).toBeNull();
  });
});
