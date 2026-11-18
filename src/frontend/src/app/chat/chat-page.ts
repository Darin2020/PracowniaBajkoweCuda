import { DatePipe, DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ResendConfirmation } from '../auth/resend-confirmation';
import { ContentImage } from '../content/image';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AuthService } from '../core/auth/auth.service';
import { ChatApi } from '../core/chat/chat-api.service';
import {
  ACCEPTED_IMAGE_TYPES,
  ChatMessage,
  CustomerChat,
  MAX_IMAGES,
  MAX_IMAGE_BYTES,
  MAX_MESSAGE_LENGTH,
  mergeMessages,
} from '../core/chat/chat.models';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { Seo } from '../core/seo';
import { ImageViewer } from '../shared/image-viewer/image-viewer';

/** A photo in the composer: uploading, uploaded (with its attachment id) or refused. */
interface PendingImage {
  readonly key: number;
  readonly preview: string;
  readonly name: string;
  readonly attachmentId: string | null;
  readonly error: string | null;
}

/** Closer to the bottom than this (px) counts as "at the bottom": new messages then scroll into view. */
const BOTTOM_SLACK = 120;

/**
 * The customer's chat with the studio (docs/PLAN.md §3.4). History loads newest first and older pages load when the
 * top is reached; new messages arrive through SignalR. After the first message a dialog asks whether replies should
 * also come by email; later the composer has a checkbox for it (§3.6).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, ImageViewer, ResendConfirmation, RouterLink],
  selector: 'app-chat-page',
  styleUrl: './chat-page.scss',
  templateUrl: './chat-page.html',
})
export class ChatPage {
  protected readonly auth = inject(AuthService);
  protected readonly realtime = inject(ChatRealtime);
  private readonly api = inject(ChatApi);
  private readonly document = inject(DOCUMENT);

  protected readonly title = $localize`:@@chat.title:Chat`;
  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly maxImages = MAX_IMAGES;
  protected readonly acceptedTypes = ACCEPTED_IMAGE_TYPES;

  protected readonly chat = signal<CustomerChat | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadingOlder = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly text = signal('');
  protected readonly images = signal<PendingImage[]>([]);
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);
  protected readonly settingError = signal<string | null>(null);

  protected readonly uploading = computed(() =>
    this.images().some((image) => !image.attachmentId && !image.error),
  );
  protected readonly canSend = computed(
    () =>
      !this.sending() &&
      !this.uploading() &&
      (this.text().trim().length > 0 || this.images().some((image) => image.attachmentId)) &&
      this.text().trim().length <= MAX_MESSAGE_LENGTH,
  );

  /** The images of the message open in the viewer. */
  protected readonly viewerImages = signal<ContentImage[]>([]);

  private readonly list = viewChild<ElementRef<HTMLElement>>('list');
  private readonly top = viewChild<ElementRef<HTMLElement>>('top');
  private readonly emailDialog = viewChild<ElementRef<HTMLDialogElement>>('emailDialog');
  private readonly viewer = viewChild(ImageViewer);
  private nextKey = 0;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    const destroy = inject(DestroyRef);

    afterNextRender(() => {
      if (!this.auth.user()?.emailConfirmed) {
        this.loading.set(false);
        return;
      }

      void this.load();

      const onVisible = () => this.markRead();
      this.document.addEventListener('visibilitychange', onVisible);

      // Older pages load when the top of the list comes into view (not in browsers without IntersectionObserver).
      const observer =
        typeof IntersectionObserver === 'undefined'
          ? null
          : new IntersectionObserver((entries) => {
              if (entries.some((entry) => entry.isIntersecting)) {
                void this.loadOlder();
              }
            });
      const top = this.top()?.nativeElement;
      if (observer && top) {
        observer.observe(top);
      }

      destroy.onDestroy(() => {
        observer?.disconnect();
        this.document.removeEventListener('visibilitychange', onVisible);
        for (const image of this.images()) {
          URL.revokeObjectURL(image.preview);
        }
      });
    });

    this.realtime.messages.pipe(takeUntilDestroyed()).subscribe((message) => {
      const conversationId = this.chat()?.conversationId;
      // A customer only receives their own conversation's events; before the first message it has no id yet.
      if (conversationId && message.conversationId !== conversationId) {
        return;
      }

      this.addMessages([message], this.isAtBottom() || message.from === 'customer');
      this.markRead();
    });

    this.realtime.reads.pipe(takeUntilDestroyed()).subscribe((receipt) => {
      if (receipt.by !== 'studio') {
        return;
      }

      const upTo = this.messages().find((message) => message.id === receipt.upToMessageId);
      if (upTo) {
        this.messages.update((messages) =>
          messages.map((message) =>
            !message.readByAdminAt && message.createdAt <= upTo.createdAt
              ? { ...message, readByAdminAt: receipt.readAt }
              : message,
          ),
        );
      }
    });

    // After a lost connection, fetch what may have arrived meanwhile.
    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => void this.refresh());
  }

  protected isMine(message: ChatMessage): boolean {
    return message.from === 'customer';
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
    setTimeout(() => this.viewer()?.open(index));
  }

  protected onText(value: string): void {
    this.text.set(value);
    this.sendError.set(null);
  }

  /** Enter sends, Shift+Enter starts a new line (on touch keyboards Enter is a new line; there is the button). */
  protected onKeydown(event: KeyboardEvent): void {
    if (
      event.key === 'Enter' &&
      !event.shiftKey &&
      !event.isComposing &&
      matchMedia('(pointer: fine)').matches
    ) {
      event.preventDefault();
      void this.send();
    }
  }

  protected addFiles(input: HTMLInputElement): void {
    const files = Array.from(input.files ?? []);
    input.value = '';
    const room = MAX_IMAGES - this.images().length;
    if (files.length > room) {
      this.sendError.set(errorMessage('chat.too_many_attachments'));
    }

    for (const file of files.slice(0, Math.max(0, room))) {
      void this.upload(file);
    }
  }

  protected removeImage(key: number): void {
    const image = this.images().find((item) => item.key === key);
    if (image) {
      URL.revokeObjectURL(image.preview);
    }

    this.images.update((images) => images.filter((item) => item.key !== key));
  }

  protected async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }

    const firstMessage = !this.chat()?.conversationId;
    this.sending.set(true);
    this.sendError.set(null);
    try {
      const ids = this.images().flatMap((image) =>
        image.attachmentId ? [image.attachmentId] : [],
      );
      const message = await this.api.send(this.text().trim(), ids);
      this.text.set('');
      for (const image of this.images()) {
        URL.revokeObjectURL(image.preview);
      }

      this.images.set([]);
      this.addMessages([message], true);
      if (firstMessage) {
        this.chat.set(await this.api.get());
      }

      if (!this.chat()?.emailPromptAnswered) {
        this.emailDialog()?.nativeElement.showModal();
      }
    } catch (error) {
      this.sendError.set(errorMessage(toApiError(error)));
    } finally {
      this.sending.set(false);
    }
  }

  /** The answer of the first-message dialog, and the composer's checkbox. */
  protected async setEmailNotifications(enabled: boolean): Promise<void> {
    this.emailDialog()?.nativeElement.close();
    this.settingError.set(null);
    try {
      this.chat.set(await this.api.setEmailNotifications(enabled));
    } catch (error) {
      this.settingError.set(errorMessage(toApiError(error)));
    }
  }

  protected showDateBefore(index: number): boolean {
    const messages = this.messages();
    return (
      index === 0 ||
      messages[index].createdAt.slice(0, 10) !== messages[index - 1].createdAt.slice(0, 10)
    );
  }

  private async load(): Promise<void> {
    try {
      const [chat, page] = await Promise.all([this.api.get(), this.api.messages()]);
      this.chat.set(chat);
      this.cursor.set(page.nextCursor);
      this.addMessages(page.items, true);
      this.markRead();
    } catch (error) {
      this.loadError.set(errorMessage(toApiError(error)));
    } finally {
      this.loading.set(false);
    }
  }

  private async refresh(): Promise<void> {
    try {
      const page = await this.api.messages();
      this.addMessages(page.items, this.isAtBottom());
      this.markRead();
    } catch {
      // The next event or reconnect tries again.
    }
  }

  /** The next older page, keeping the messages on screen where they are. */
  private async loadOlder(): Promise<void> {
    const cursor = this.cursor();
    if (!cursor || this.loadingOlder() || this.loading()) {
      return;
    }

    this.loadingOlder.set(true);
    const list = this.list()?.nativeElement;
    const heightBefore = list?.scrollHeight ?? 0;
    try {
      const page = await this.api.messages(cursor);
      this.cursor.set(page.nextCursor);
      this.messages.update((messages) => mergeMessages(messages, page.items));
      requestAnimationFrame(() => {
        if (list) {
          list.scrollTop += list.scrollHeight - heightBefore;
        }
      });
    } catch (error) {
      this.loadError.set(errorMessage(toApiError(error)));
    } finally {
      this.loadingOlder.set(false);
    }
  }

  private addMessages(incoming: readonly ChatMessage[], scrollToBottom: boolean): void {
    this.messages.update((messages) => mergeMessages(messages, incoming));
    if (scrollToBottom) {
      requestAnimationFrame(() => {
        const list = this.list()?.nativeElement;
        if (list) {
          list.scrollTop = list.scrollHeight;
        }
      });
    }
  }

  private isAtBottom(): boolean {
    const list = this.list()?.nativeElement;
    return !list || list.scrollHeight - list.scrollTop - list.clientHeight < BOTTOM_SLACK;
  }

  /** Tells the API the customer has seen the studio's messages, while the page is visible. */
  private markRead(): void {
    if (this.document.visibilityState !== 'visible') {
      return;
    }

    const unread = this.messages().filter(
      (message) => message.from === 'studio' && !message.readByCustomerAt,
    );
    const newest = unread.at(-1);
    if (!newest) {
      return;
    }

    const now = new Date().toISOString();
    this.messages.update((messages) =>
      messages.map((message) =>
        message.from === 'studio' && !message.readByCustomerAt
          ? { ...message, readByCustomerAt: now }
          : message,
      ),
    );
    this.api.markRead(newest.id).catch(() => {
      // Not critical: the badge corrects itself with the next event.
    });
  }

  private async upload(file: File): Promise<void> {
    const key = this.nextKey++;
    const preview = URL.createObjectURL(file);
    this.images.update((images) => [
      ...images,
      { key, preview, name: file.name, attachmentId: null, error: null },
    ]);
    const update = (change: Partial<PendingImage>) =>
      this.images.update((images) =>
        images.map((image) => (image.key === key ? { ...image, ...change } : image)),
      );

    if (file.size > MAX_IMAGE_BYTES) {
      update({ error: errorMessage('files.too_large') });
      return;
    }

    try {
      const uploaded = await this.api.upload(file);
      update({ attachmentId: uploaded.id });
    } catch (error) {
      update({ error: errorMessage(toApiError(error)) });
    }
  }
}
