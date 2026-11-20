import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatApi } from '../core/chat/chat-api.service';
import { ACCEPTED_IMAGE_TYPES, ChatImage, MAX_IMAGE_BYTES } from '../core/chat/chat.models';
import {
  ADMIN_NOTE_MAX_LENGTH,
  DESCRIPTION_MAX_LENGTH,
  TITLE_MAX_LENGTH,
} from '../core/orders/order.models';

/** What the form edits. `price` is in euros. */
export interface OrderFormValue {
  readonly title: string;
  readonly description: string;
  readonly price: number | null;
  readonly referenceId: string | null;
  readonly adminNote: string;
}

/** Saves the form: resolves when saved, rejects with the API's error otherwise (the form shows it). */
export type SaveOrder = (value: OrderFormValue) => Promise<void>;

let nextId = 0;

/**
 * The fields of an order for the studio, used to create a draft in a conversation and to edit an order. The
 * reference image is picked from the conversation's images (`candidates`) or uploaded. Once the order is paid only
 * the internal note can change (`termsEditable`).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  selector: 'app-order-form',
  styleUrl: './order-form.scss',
  template: `
    <form class="form" (ngSubmit)="submit()">
      <div class="field">
        <label [for]="prefix + 'title'" i18n="@@order.form.title">Title</label>
        <input
          [id]="prefix + 'title'"
          class="input"
          name="title"
          required
          [maxlength]="titleMax"
          [disabled]="!termsEditable()"
          [(ngModel)]="title"
        />
      </div>

      <div class="field">
        <label [for]="prefix + 'description'" i18n="@@order.form.description"
          >Description for the customer</label
        >
        <textarea
          [id]="prefix + 'description'"
          class="input"
          name="description"
          rows="4"
          [maxlength]="descriptionMax"
          [disabled]="!termsEditable()"
          [(ngModel)]="description"
        ></textarea>
      </div>

      <div class="field">
        <label [for]="prefix + 'price'" i18n="@@order.form.price">Price (EUR)</label>
        <input
          [id]="prefix + 'price'"
          class="input order-form__price"
          name="price"
          type="number"
          inputmode="decimal"
          min="0.01"
          step="0.01"
          required
          [disabled]="!termsEditable()"
          [(ngModel)]="price"
        />
      </div>

      <fieldset class="field order-form__reference" [disabled]="!termsEditable()">
        <legend class="field__label" i18n="@@order.form.reference">Reference image</legend>
        @if (images().length === 0) {
          <p class="hint" i18n="@@order.form.noImages">
            No images in the conversation yet. You can upload one.
          </p>
        }
        <div class="order-form__images">
          <label class="order-form__choice order-form__choice--none">
            <input
              type="radio"
              [name]="prefix + 'reference'"
              [checked]="referenceId() === null"
              (change)="referenceId.set(null)"
            />
            <span i18n="@@order.form.noReference">None</span>
          </label>
          @for (image of images(); track image.id; let position = $index) {
            <label class="order-form__choice">
              <input
                type="radio"
                [name]="prefix + 'reference'"
                [checked]="referenceId() === image.id"
                (change)="referenceId.set(image.id)"
              />
              <img
                [src]="image.thumbnailUrl"
                width="96"
                height="96"
                alt="Image {{ position + 1 }}"
                i18n-alt="@@order.form.imageAlt"
              />
            </label>
          }
        </div>
        <label class="button order-form__upload">
          @if (uploading()) {
            <span role="status" i18n="@@chat.uploading">Uploading…</span>
          } @else {
            <span i18n="@@order.form.upload">Upload an image</span>
          }
          <input
            type="file"
            class="visually-hidden"
            [accept]="acceptedTypes"
            [disabled]="uploading()"
            (change)="upload($any($event.target))"
          />
        </label>
      </fieldset>

      <div class="field">
        <label [for]="prefix + 'note'" i18n="@@order.form.note">Internal note</label>
        <textarea
          [id]="prefix + 'note'"
          class="input"
          name="note"
          rows="2"
          [maxlength]="noteMax"
          [(ngModel)]="adminNote"
          [attr.aria-describedby]="prefix + 'note-hint'"
        ></textarea>
        <p class="hint" [id]="prefix + 'note-hint'" i18n="@@order.form.noteHint">
          Only the studio sees this note.
        </p>
      </div>

      @if (error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      <div class="actions">
        <button class="button button--primary" type="submit" [disabled]="busy()">
          {{ submitLabel() }}
        </button>
        <button class="link-button" type="button" (click)="cancelled.emit()" i18n="@@common.cancel">
          Cancel
        </button>
      </div>
    </form>
  `,
})
export class OrderForm {
  private readonly api = inject(ChatApi);

  /** The values to start from (null: an empty form). */
  readonly initial = input<OrderFormValue | null>(null);
  /** Images the reference can be chosen from (the conversation's, newest first). */
  readonly candidates = input<readonly ChatImage[]>([]);
  readonly termsEditable = input(true);
  readonly submitLabel = input.required<string>();
  readonly save = input.required<SaveOrder>();
  readonly cancelled = output<void>();

  protected readonly prefix = `order-form-${nextId++}-`;
  protected readonly titleMax = TITLE_MAX_LENGTH;
  protected readonly descriptionMax = DESCRIPTION_MAX_LENGTH;
  protected readonly noteMax = ADMIN_NOTE_MAX_LENGTH;
  protected readonly acceptedTypes = ACCEPTED_IMAGE_TYPES;

  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly price = signal<number | null>(null);
  protected readonly adminNote = signal('');
  protected readonly referenceId = signal<string | null>(null);
  protected readonly uploaded = signal<ChatImage[]>([]);
  protected readonly uploading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Fresh uploads first, then the conversation's images; each once. */
  protected readonly images = computed(() => {
    const seen = new Set<string>();
    return [...this.uploaded(), ...this.candidates()].filter((image) =>
      seen.has(image.id) ? false : (seen.add(image.id), true),
    );
  });

  constructor() {
    effect(() => {
      const value = this.initial();
      untracked(() => {
        this.title.set(value?.title ?? '');
        this.description.set(value?.description ?? '');
        this.price.set(value?.price ?? null);
        this.adminNote.set(value?.adminNote ?? '');
        this.referenceId.set(value?.referenceId ?? null);
      });
    });
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    if (file.size > MAX_IMAGE_BYTES) {
      this.error.set(errorMessage('files.too_large'));
      return;
    }

    this.uploading.set(true);
    this.error.set(null);
    try {
      const image = await this.api.upload(file);
      this.uploaded.update((images) => [image, ...images]);
      this.referenceId.set(image.id);
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.uploading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.busy()) {
      return;
    }

    if (!this.title().trim()) {
      this.error.set(errorMessage('orders.title_required'));
      return;
    }

    const price = this.price() === null ? NaN : Number(this.price());
    if (!(price > 0) || Math.abs(Math.round(price * 100) - price * 100) > 1e-6) {
      this.error.set(errorMessage('orders.invalid_price'));
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.save()({
        title: this.title().trim(),
        description: this.description().trim(),
        price,
        referenceId: this.referenceId(),
        adminNote: this.adminNote().trim(),
      });
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.busy.set(false);
    }
  }
}
