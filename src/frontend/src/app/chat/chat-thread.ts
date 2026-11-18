import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ContentImage } from '../content/image';
import { ChatMessage } from '../core/chat/chat.models';
import { ImageViewer } from '../shared/image-viewer/image-viewer';
import { ChatMessageView } from './chat-message';
import { ChatSession, ChatSide } from './chat-session';

/** Closer to the bottom than this (px) counts as "at the bottom": new messages then scroll into view. */
const BOTTOM_SLACK = 120;

/**
 * The scrolling list of a conversation: date separators, the messages, older pages loading when the top comes into
 * view (keeping what is on screen in place), and the fullscreen viewer for photos. What to show while it is empty is
 * projected by the page.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatMessageView, DatePipe, ImageViewer],
  selector: 'app-chat-thread',
  styleUrl: './chat-thread.scss',
  template: `
    <div #list class="chat__list" role="log" aria-live="polite" [attr.aria-label]="label()">
      <div #top class="chat__top" aria-hidden="true"></div>

      @if (session().loadingOlder()) {
        <p class="chat__hint" role="status" i18n="@@chat.loadingOlder">Loading earlier messages…</p>
      }
      @if (session().error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      @if (session().loading()) {
        <p class="chat__hint" role="status" i18n="@@common.loading">Loading…</p>
      } @else if (session().messages().length === 0 && !session().error()) {
        <ng-content />
      }

      @for (message of session().messages(); track message.id; let index = $index) {
        @if (showDateBefore(index)) {
          <p class="chat__date">{{ message.createdAt | date: 'longDate' }}</p>
        }
        <app-chat-message
          [message]="message"
          [side]="side()"
          [otherName]="otherName()"
          (openImage)="openImage(message, $event)"
        />
      }
    </div>

    <app-image-viewer [items]="viewerImages()" />
  `,
})
export class ChatThread {
  readonly session = input.required<ChatSession>();
  readonly side = input.required<ChatSide>();
  readonly otherName = input.required<string>();
  readonly label = input<string>();

  protected readonly viewerImages = signal<ContentImage[]>([]);

  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');
  private readonly top = viewChild.required<ElementRef<HTMLElement>>('top');
  private readonly viewer = viewChild.required(ImageViewer);

  constructor() {
    const destroy = inject(DestroyRef);
    afterNextRender(() => {
      // Browsers without IntersectionObserver simply do not load older pages on scroll.
      if (typeof IntersectionObserver === 'undefined') {
        return;
      }

      const observer = new IntersectionObserver((entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          void this.loadOlder();
        }
      });
      observer.observe(this.top().nativeElement);
      destroy.onDestroy(() => observer.disconnect());
    });
  }

  /** Whether the reader is looking at the newest messages (then new ones may scroll into view). */
  isAtBottom(): boolean {
    const list = this.list().nativeElement;
    return list.scrollHeight - list.scrollTop - list.clientHeight < BOTTOM_SLACK;
  }

  /** After the next render, so the new messages are in the list. */
  scrollToBottom(): void {
    requestAnimationFrame(() => {
      const list = this.list().nativeElement;
      list.scrollTop = list.scrollHeight;
    });
  }

  protected showDateBefore(index: number): boolean {
    const messages = this.session().messages();
    return (
      index === 0 ||
      messages[index].createdAt.slice(0, 10) !== messages[index - 1].createdAt.slice(0, 10)
    );
  }

  protected openImage(message: ChatMessage, index: number): void {
    this.viewerImages.set(
      message.images.map((image, position) => ({
        src: image.url,
        alt: $localize`:@@chat.viewerPhotoAlt:Photo ${position + 1}:INDEX: of ${message.images.length}:TOTAL:`,
        width: image.width,
        height: image.height,
      })),
    );
    // The viewer reads its items on the next render.
    setTimeout(() => this.viewer().open(index));
  }

  private async loadOlder(): Promise<void> {
    const list = this.list().nativeElement;
    const heightBefore = list.scrollHeight;
    if (await this.session().loadOlder()) {
      requestAnimationFrame(() => {
        list.scrollTop += list.scrollHeight - heightBefore;
      });
    }
  }
}
