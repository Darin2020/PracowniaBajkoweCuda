import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatApi } from '../core/chat/chat-api.service';
import {
  ACCEPTED_IMAGE_TYPES,
  MAX_IMAGES,
  MAX_IMAGE_BYTES,
  MAX_MESSAGE_LENGTH,
} from '../core/chat/chat.models';

/** A photo in the composer: uploading, uploaded (with its attachment id) or refused. */
interface PendingImage {
  readonly key: number;
  readonly preview: string;
  readonly name: string;
  readonly attachmentId: string | null;
  readonly error: string | null;
}

/** Sends a message: resolves when it was sent, rejects with the API's error otherwise. */
export type SendMessage = (body: string, attachmentIds: readonly string[]) => Promise<void>;

/**
 * Writing a message: text and up to five photos, which upload as soon as they are chosen
 * (`POST /api/chat/attachments`, the same for customers and admins). The page decides where the message goes.
 * Extra controls (the customer's email checkbox) are projected under the text box.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  selector: 'app-chat-composer',
  styleUrl: './chat-composer.scss',
  template: `
    <form class="composer" (ngSubmit)="submit()">
      @if (images().length > 0) {
        <ul class="composer__images">
          @for (image of images(); track image.key) {
            <li class="composer__image" [class.composer__image--error]="image.error">
              <img [src]="image.preview" alt="" />
              @if (!image.attachmentId && !image.error) {
                <span class="composer__badge" role="status" i18n="@@chat.uploading"
                  >Uploading…</span
                >
              }
              @if (image.error; as message) {
                <span class="composer__badge composer__badge--error" role="alert">{{
                  message
                }}</span>
              }
              <button
                type="button"
                class="composer__remove"
                (click)="removeImage(image.key)"
                aria-label="Remove photo {{ image.name }}"
                i18n-aria-label="@@chat.removePhoto"
              >
                ×
              </button>
            </li>
          }
        </ul>
      }

      @if (error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      <div class="composer__row">
        <label
          class="composer__attach button"
          [class.composer__attach--disabled]="images().length >= maxImages"
        >
          <span class="visually-hidden" i18n="@@chat.addPhotos">Add photos</span>
          <svg aria-hidden="true" viewBox="0 0 24 24" width="22" height="22">
            <path d="M4 7h3l2-2h6l2 2h3v12H4z" />
            <circle cx="12" cy="13" r="3.5" />
          </svg>
          <input
            type="file"
            class="visually-hidden"
            [accept]="acceptedTypes"
            multiple
            [disabled]="images().length >= maxImages"
            (change)="addFiles($any($event.target))"
          />
        </label>

        <label class="visually-hidden" for="chat-text" i18n="@@chat.messageLabel">Message</label>
        <textarea
          id="chat-text"
          class="input composer__text"
          rows="1"
          [maxlength]="maxLength"
          placeholder="Write a message…"
          i18n-placeholder="@@chat.placeholder"
          [ngModel]="text()"
          (ngModelChange)="onText($event)"
          name="text"
          (keydown)="onKeydown($event)"
        ></textarea>

        <button class="button button--primary composer__send" type="submit" [disabled]="!canSend()">
          <span i18n="@@chat.send">Send</span>
        </button>
      </div>

      <ng-content />
    </form>
  `,
})
export class ChatComposer {
  private readonly api = inject(ChatApi);

  readonly send = input.required<SendMessage>();

  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly maxImages = MAX_IMAGES;
  protected readonly acceptedTypes = ACCEPTED_IMAGE_TYPES;

  protected readonly text = signal('');
  protected readonly images = signal<PendingImage[]>([]);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly uploading = computed(() =>
    this.images().some((image) => !image.attachmentId && !image.error),
  );
  protected readonly canSend = computed(
    () =>
      !this.sending() &&
      !this.uploading() &&
      (this.text().trim().length > 0 || this.images().some((image) => image.attachmentId)) &&
      this.text().trim().length <= MAX_MESSAGE_LENGTH,
  );

  private nextKey = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      for (const image of this.images()) {
        URL.revokeObjectURL(image.preview);
      }
    });
  }

  protected onText(value: string): void {
    this.text.set(value);
    this.error.set(null);
  }

  /** Enter sends, Shift+Enter starts a new line; on touch keyboards Enter is a new line and the button sends. */
  protected onKeydown(event: KeyboardEvent): void {
    if (
      event.key === 'Enter' &&
      !event.shiftKey &&
      !event.isComposing &&
      matchMedia('(pointer: fine)').matches
    ) {
      event.preventDefault();
      void this.submit();
    }
  }

  protected addFiles(input: HTMLInputElement): void {
    const files = Array.from(input.files ?? []);
    input.value = '';
    const room = MAX_IMAGES - this.images().length;
    if (files.length > room) {
      this.error.set(errorMessage('chat.too_many_attachments'));
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

  protected async submit(): Promise<void> {
    if (!this.canSend()) {
      return;
    }

    this.sending.set(true);
    this.error.set(null);
    try {
      const ids = this.images().flatMap((image) =>
        image.attachmentId ? [image.attachmentId] : [],
      );
      await this.send()(this.text().trim(), ids);
      this.text.set('');
      for (const image of this.images()) {
        URL.revokeObjectURL(image.preview);
      }

      this.images.set([]);
    } catch (error) {
      // The text and the photos stay, so nothing typed is lost.
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.sending.set(false);
    }
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
      update({ attachmentId: (await this.api.upload(file)).id });
    } catch (error) {
      update({ error: errorMessage(toApiError(error)) });
    }
  }
}
