import { CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AdminChatApi } from '../core/chat/admin-chat-api.service';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { ACCEPTED_IMAGE_TYPES, ChatImage, MAX_IMAGE_BYTES } from '../core/chat/chat.models';
import { AdminOrdersApi } from '../core/orders/orders-api.service';
import {
  AdminOrder,
  MAX_ORDER_PHOTOS,
  OrderStepAction,
  REASON_MAX_LENGTH,
  isFinal,
  nextStep,
  photosOpen,
  stepLabel,
  termsEditable,
} from '../core/orders/order.models';
import { Seo } from '../core/seo';
import { OrderStatusBadge } from '../shared/orders/order-status-badge';
import { OrderTimeline } from '../shared/orders/order-timeline';
import { PhotoGallery } from '../shared/orders/photo-gallery';
import { OrderForm, OrderFormValue, SaveOrder } from './order-form';
import { conversationImages } from './order-images';

/**
 * `/admin/orders/:id` (docs/PLAN.md §3.5): one order for the studio: the next step and cancelling, editing (the
 * terms only before payment), the photos of the finished toy while it is made or ready, and the timeline. Every
 * change sends the version the admin saw; if someone else changed the order meanwhile, the page reloads it.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    FormsModule,
    OrderForm,
    OrderStatusBadge,
    OrderTimeline,
    PhotoGallery,
    RouterLink,
  ],
  selector: 'app-admin-order-page',
  styleUrls: ['../orders/order-page.scss', './order-page.scss'],
  template: `
    <section class="page page--wide container">
      <a routerLink="/admin/orders" i18n="@@admin.orders.back">← All orders</a>

      @if (loadError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      @if (order(); as item) {
        <header class="order-header">
          <p class="order-header__number">{{ item.number }}</p>
          <h1>{{ item.title }}</h1>
          <p class="order-header__meta">
            <app-order-status-badge [status]="item.status" />
            <span class="order-header__price">{{
              item.price.amount | currency: item.price.currency
            }}</span>
          </p>
          <p>
            <ng-container i18n="@@admin.order.customer">Customer:</ng-container>&ngsp;
            @if (item.conversationId) {
              <a [routerLink]="['/admin/conversations', item.conversationId]">{{
                item.customerName || deletedName
              }}</a>
            } @else {
              {{ item.customerName || deletedName }}
            }
          </p>
        </header>

        @if (error(); as message) {
          <p class="alert alert--error" role="alert">{{ message }}</p>
        }

        @if (!isFinal(item.status) && !editing()) {
          <section class="card steps">
            <h2 i18n="@@admin.order.nextStep">Next step</h2>
            @if (next(); as step) {
              <div class="actions">
                <button
                  class="button button--primary"
                  type="button"
                  [disabled]="busy() || (step === 'mark-ready' && item.photos.length === 0)"
                  (click)="apply(step)"
                >
                  {{ label(step) }}
                </button>
              </div>
              @if (step === 'confirm') {
                <p class="hint" i18n="@@admin.order.confirmHint">
                  The customer then sees the order and gets the payment instructions in the chat and
                  by email.
                </p>
              }
              @if (step === 'mark-ready' && item.photos.length === 0) {
                <p class="hint" i18n="@@admin.order.photoFirst">
                  Add at least one photo of the finished toy first.
                </p>
              }
            }

            @if (cancelling()) {
              <form class="form" (ngSubmit)="cancel()">
                <div class="field">
                  <label for="cancel-reason" i18n="@@admin.order.reason"
                    >Reason (the customer sees it)</label
                  >
                  <textarea
                    id="cancel-reason"
                    class="input"
                    name="reason"
                    rows="3"
                    required
                    [maxlength]="reasonMax"
                    [(ngModel)]="reason"
                  ></textarea>
                </div>
                <div class="actions">
                  <button
                    class="button button--danger"
                    type="submit"
                    [disabled]="busy()"
                    i18n="@@admin.order.cancelConfirm"
                  >
                    Cancel the order
                  </button>
                  <button
                    class="link-button"
                    type="button"
                    (click)="cancelling.set(false)"
                    i18n="@@admin.order.keep"
                  >
                    Keep the order
                  </button>
                </div>
              </form>
            } @else {
              <p>
                <button
                  class="link-button link-button--danger"
                  type="button"
                  (click)="cancelling.set(true)"
                >
                  {{ label('cancel') }}
                </button>
              </p>
            }
          </section>
        }

        @if (item.status === 'Cancelled' && item.cancellationReason) {
          <div class="alert alert--error">
            <strong i18n="@@order.cancelledBecause">The order was cancelled:</strong>
            <p class="pre">{{ item.cancellationReason }}</p>
          </div>
        }

        @if (editing()) {
          <section class="card">
            <h2 i18n="@@admin.order.edit">Edit the order</h2>
            @if (!termsEditable(item.status)) {
              <p class="hint" i18n="@@admin.order.termsLocked">
                The order has been paid: only the internal note can change.
              </p>
            }
            <app-order-form
              [initial]="formValue()"
              [candidates]="candidates()"
              [termsEditable]="termsEditable(item.status)"
              submitLabel="Save"
              i18n-submitLabel="@@common.save"
              [save]="saveChanges"
              (cancelled)="editing.set(false)"
            />
          </section>
        } @else {
          <div class="order-body">
            @if (item.referenceImage; as image) {
              <section>
                <h2 i18n="@@order.reference">Reference</h2>
                <app-photo-gallery [images]="[image]" />
              </section>
            }
            <section>
              <h2 i18n="@@order.description">Description</h2>
              @if (item.description) {
                <p class="pre">{{ item.description }}</p>
              } @else {
                <p class="hint">—</p>
              }
              @if (item.adminNote) {
                <h2 class="note-title" i18n="@@order.form.note">Internal note</h2>
                <p class="pre note">{{ item.adminNote }}</p>
              }
            </section>
          </div>
          @if (!isFinal(item.status)) {
            <p>
              <button class="button" type="button" (click)="edit()" i18n="@@admin.order.editButton">
                Edit
              </button>
            </p>
          }
        }

        <section>
          <h2 i18n="@@admin.order.photos">Photos of the finished toy</h2>
          @if (photosOpen(item.status)) {
            @if (item.photos.length === 0) {
              <p class="hint" i18n="@@admin.order.noPhotos">No photos yet.</p>
            }
            <ul class="photos">
              @for (photo of item.photos; track photo.id; let position = $index; let last = $last) {
                <li class="photo">
                  <img [src]="photo.image.thumbnailUrl" alt="" width="128" height="128" />
                  <div class="photo__actions">
                    <button
                      class="button photo__button"
                      type="button"
                      [disabled]="busy() || position === 0"
                      (click)="move(position, -1)"
                      aria-label="Move photo {{ position + 1 }} earlier"
                      i18n-aria-label="@@admin.order.moveEarlier"
                    >
                      ←
                    </button>
                    <button
                      class="button photo__button"
                      type="button"
                      [disabled]="busy() || last"
                      (click)="move(position, 1)"
                      aria-label="Move photo {{ position + 1 }} later"
                      i18n-aria-label="@@admin.order.moveLater"
                    >
                      →
                    </button>
                    <button
                      class="button button--danger photo__button"
                      type="button"
                      [disabled]="busy()"
                      (click)="removePhoto(photo.id)"
                      aria-label="Remove photo {{ position + 1 }}"
                      i18n-aria-label="@@admin.order.removePhoto"
                    >
                      ×
                    </button>
                  </div>
                </li>
              }
            </ul>
            @if (item.photos.length < maxPhotos) {
              <label class="button upload">
                @if (uploading() > 0) {
                  <span role="status" i18n="@@chat.uploading">Uploading…</span>
                } @else {
                  <span i18n="@@admin.order.addPhotos">Add photos</span>
                }
                <input
                  type="file"
                  class="visually-hidden"
                  multiple
                  [accept]="acceptedTypes"
                  [disabled]="uploading() > 0"
                  (change)="addPhotos($any($event.target))"
                />
              </label>
            }
          } @else if (item.photos.length > 0) {
            <app-photo-gallery [images]="photoImages()" />
          } @else {
            <p class="hint" i18n="@@admin.order.photosLater">
              Photos are added while the toy is being made.
            </p>
          }
        </section>

        <section>
          <h2 i18n="@@order.timeline">Progress</h2>
          @if (item.timeline.length > 0) {
            <app-order-timeline [steps]="item.timeline" />
          } @else {
            <p class="hint" i18n="@@admin.order.draftHint">
              This is a draft: the customer does not see it yet.
            </p>
          }
        </section>
      } @else if (!loadError()) {
        <p role="status" i18n="@@common.loading">Loading…</p>
      }
    </section>
  `,
})
export class AdminOrderPage {
  private readonly api = inject(AdminOrdersApi);
  private readonly chat = inject(AdminChatApi);
  private readonly seo = inject(Seo);

  /** The route parameter. */
  readonly id = input.required<string>();

  protected readonly order = signal<AdminOrder | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly uploading = signal(0);
  protected readonly editing = signal(false);
  protected readonly cancelling = signal(false);
  protected readonly candidates = signal<readonly ChatImage[]>([]);
  protected readonly reason = signal('');

  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly maxPhotos = MAX_ORDER_PHOTOS;
  protected readonly acceptedTypes = ACCEPTED_IMAGE_TYPES;
  protected readonly deletedName = $localize`:@@admin.conversations.deleted:Deleted account`;
  protected readonly isFinal = isFinal;
  protected readonly termsEditable = termsEditable;
  protected readonly photosOpen = photosOpen;

  protected readonly next = computed(() => {
    const order = this.order();
    return order ? nextStep(order.status) : null;
  });
  protected readonly photoImages = computed(() =>
    (this.order()?.photos ?? []).map((photo) => photo.image),
  );
  protected readonly formValue = computed<OrderFormValue | null>(() => {
    const order = this.order();
    return order
      ? {
          title: order.title,
          description: order.description,
          price: order.price.amount,
          referenceId: order.referenceImage?.id ?? null,
          adminNote: order.adminNote ?? '',
        }
      : null;
  });

  constructor() {
    this.seo.setNoIndexPage($localize`:@@admin.orders.title:Orders`);
    effect(() => void this.load(this.id()));

    const realtime = inject(ChatRealtime);
    realtime.orders.pipe(takeUntilDestroyed()).subscribe((summary) => {
      // Someone else's change; ours already updated the page. Not while editing, so typing is not lost.
      if (summary.id === this.id() && !this.busy() && !this.editing()) {
        void this.load(this.id());
      }
    });
    realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => {
      if (!this.editing()) {
        void this.load(this.id());
      }
    });
  }

  protected label(step: OrderStepAction): string {
    return stepLabel(step);
  }

  protected apply(step: OrderStepAction): Promise<void> {
    return this.change((order) => this.api.step(order.id, step, order.rowVersion));
  }

  protected async cancel(): Promise<void> {
    if (!this.reason().trim()) {
      this.error.set(errorMessage('orders.reason_required'));
      return;
    }

    await this.change((order) =>
      this.api.step(order.id, 'cancel', order.rowVersion, this.reason().trim()),
    );
    if (!this.error()) {
      this.cancelling.set(false);
      this.reason.set('');
    }
  }

  protected async edit(): Promise<void> {
    this.error.set(null);
    this.editing.set(true);
    const order = this.order();
    const own = order?.referenceImage ? [order.referenceImage] : [];
    this.candidates.set(own);
    if (order?.conversationId) {
      try {
        this.candidates.set([
          ...own,
          ...(await conversationImages(this.chat, order.conversationId)),
        ]);
      } catch {
        // Without the conversation's images the admin can still upload one.
      }
    }
  }

  protected readonly saveChanges: SaveOrder = async (value) => {
    const order = this.order();
    if (!order) {
      return;
    }

    const editable = termsEditable(order.status);
    try {
      this.order.set(
        await this.api.update(order.id, {
          ...(editable
            ? {
                title: value.title,
                description: value.description,
                price: value.price ?? undefined,
                ...(value.referenceId
                  ? { referenceAttachmentId: value.referenceId }
                  : { clearReferenceImage: order.referenceImage !== null }),
              }
            : {}),
          adminNote: value.adminNote,
          rowVersion: order.rowVersion,
        }),
      );
      this.editing.set(false);
    } catch (error) {
      if (toApiError(error).code === 'orders.conflict') {
        await this.load(order.id);
      }

      throw error;
    }
  };

  protected async addPhotos(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.error.set(null);
    // One after another: each answer is the order with the photo added, and the order of the files is kept.
    this.uploading.set(files.length);
    for (const file of files) {
      const order = this.order();
      if (!order) {
        break;
      }

      if (file.size > MAX_IMAGE_BYTES) {
        this.error.set(errorMessage('files.too_large'));
      } else {
        try {
          this.order.set(await this.api.addPhoto(order.id, file));
        } catch (error) {
          this.error.set(errorMessage(toApiError(error)));
        }
      }

      this.uploading.update((count) => count - 1);
    }

    this.uploading.set(0);
  }

  protected removePhoto(photoId: string): Promise<void> {
    return this.change((order) => this.api.removePhoto(order.id, photoId));
  }

  protected move(position: number, delta: number): Promise<void> {
    return this.change((order) => {
      const ids = order.photos.map((photo) => photo.id);
      const [moved] = ids.splice(position, 1);
      ids.splice(position + delta, 0, moved);
      return this.api.reorderPhotos(order.id, ids);
    });
  }

  private async change(run: (order: AdminOrder) => Promise<AdminOrder>): Promise<void> {
    const order = this.order();
    if (!order || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.order.set(await run(order));
    } catch (error) {
      const problem = toApiError(error);
      this.error.set(errorMessage(problem));
      if (problem.code === 'orders.conflict' || problem.status === 409) {
        await this.load(order.id, true);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string, keepError = false): Promise<void> {
    try {
      const order = await this.api.get(id);
      if (id !== this.id()) {
        return;
      }

      this.order.set(order);
      this.loadError.set(null);
      if (!keepError) {
        this.error.set(null);
      }

      this.seo.setNoIndexPage(`${order.number} · ${order.title}`);
    } catch (error) {
      this.loadError.set(errorMessage(toApiError(error)));
    }
  }
}
