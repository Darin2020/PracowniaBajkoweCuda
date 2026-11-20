import { DatePipe, DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ChatComposer, SendMessage } from '../chat/chat-composer';
import { ChatSession } from '../chat/chat-session';
import { ChatThread } from '../chat/chat-thread';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AdminChatApi, ConversationDetail } from '../core/chat/admin-chat-api.service';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { LOCALE_NAMES, SUPPORTED_LOCALES } from '../core/locales';
import { AdminOrdersApi } from '../core/orders/orders-api.service';
import { OrderSummary } from '../core/orders/order.models';
import { Seo } from '../core/seo';
import { OrderStatusBadge } from '../shared/orders/order-status-badge';
import { OrderForm, SaveOrder } from './order-form';
import { imagesOf } from './order-images';

/**
 * `/admin/conversations/:id` (docs/PLAN.md §3.4): one customer's conversation from the studio's side, with the
 * customer beside it (on a phone, above it, folded), with the customer's orders and "Create order": a draft whose
 * reference image is picked from the images of the conversation (or uploaded).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatComposer, ChatThread, DatePipe, OrderForm, OrderStatusBadge, RouterLink],
  selector: 'app-conversation-page',
  styleUrl: './conversation-page.scss',
  template: `
    <section class="conversation container">
      <header class="conversation__header">
        <a routerLink="/admin/conversations" i18n="@@admin.conversation.back"
          >← All conversations</a
        >
        <h1>{{ customerName() }}</h1>
        @if (realtime.state() === 'reconnecting' || realtime.state() === 'disconnected') {
          <p class="conversation__status" role="status" i18n="@@chat.reconnecting">Reconnecting…</p>
        }
      </header>

      @if (detailError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      <div class="conversation__body">
        <div class="conversation__chat">
          <app-chat-thread
            [session]="session"
            side="studio"
            [otherName]="customerName()"
            [label]="customerName()"
          >
            <p class="hint" i18n="@@admin.conversation.empty">No messages yet.</p>
          </app-chat-thread>
          @if (detail()?.customer?.isDeleted) {
            <p class="hint" i18n="@@admin.conversation.deletedHint">
              This account has been deleted; replies cannot reach it.
            </p>
          } @else {
            <app-chat-composer [send]="send" />
          }
        </div>

        <aside class="side">
          @if (detail(); as conversation) {
            <details class="customer panel" [open]="wide">
              <summary i18n="@@admin.conversation.customer">Customer</summary>
              <dl>
                <dt i18n="@@admin.conversation.name">Name</dt>
                <dd>{{ customerName() }}</dd>
                @if (conversation.customer.email; as email) {
                  <dt i18n="@@auth.email">Email address</dt>
                  <dd>
                    <a [href]="'mailto:' + email">{{ email }}</a>
                  </dd>
                }
                <dt i18n="@@account.language">Language of emails</dt>
                <dd>{{ language(conversation.customer.preferredLanguage) }}</dd>
                <dt i18n="@@admin.conversation.since">Writing since</dt>
                <dd>{{ conversation.createdAt | date: 'longDate' }}</dd>
                <dt i18n="@@admin.conversation.emails">Email notifications</dt>
                <dd>
                  @if (conversation.customerEmailNotificationsEnabled) {
                    <ng-container i18n="@@admin.conversation.emailsOn">On</ng-container>
                  } @else {
                    <ng-container i18n="@@admin.conversation.emailsOff">Off</ng-container>
                  }
                </dd>
              </dl>
            </details>
          }

          <details class="panel" [open]="wide || creating()">
            <summary>
              <ng-container i18n="@@admin.conversation.orders">Orders</ng-container>
              @if (orders().length > 0) {
                ({{ orders().length }})
              }
            </summary>
            @if (ordersError(); as message) {
              <p class="alert alert--error" role="alert">{{ message }}</p>
            }
            @if (orders().length > 0) {
              <ul class="panel-orders">
                @for (order of orders(); track order.id) {
                  <li>
                    <a [routerLink]="['/admin/orders', order.id]">
                      <span class="panel-orders__number">{{ order.number }}</span>
                      <span class="panel-orders__title">{{ order.title }}</span>
                    </a>
                    <app-order-status-badge [status]="order.status" />
                  </li>
                }
              </ul>
            } @else if (!ordersError()) {
              <p class="hint" i18n="@@admin.conversation.noOrders">No orders yet.</p>
            }

            @if (creating()) {
              <h2 class="panel__title" i18n="@@admin.conversation.newOrder">New order</h2>
              <app-order-form
                [candidates]="conversationImages()"
                submitLabel="Create draft"
                i18n-submitLabel="@@admin.conversation.createDraft"
                [save]="createOrder"
                (cancelled)="creating.set(false)"
              />
            } @else if (!detail()?.customer?.isDeleted) {
              <p>
                <button
                  class="button button--primary"
                  type="button"
                  (click)="creating.set(true)"
                  i18n="@@admin.conversation.createOrder"
                >
                  Create order
                </button>
              </p>
            }
          </details>
        </aside>
      </div>
    </section>
  `,
})
export class ConversationPage {
  protected readonly realtime = inject(ChatRealtime);
  private readonly api = inject(AdminChatApi);
  private readonly document = inject(DOCUMENT);
  private readonly ordersApi = inject(AdminOrdersApi);
  private readonly router = inject(Router);

  /** The route parameter. */
  readonly id = input.required<string>();

  protected readonly detail = signal<ConversationDetail | null>(null);
  protected readonly detailError = signal<string | null>(null);
  protected readonly orders = signal<readonly OrderSummary[]>([]);
  protected readonly ordersError = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly session = new ChatSession(
    {
      page: (before) => this.api.messages(this.id(), before),
      markRead: (upTo) => this.api.markRead(this.id(), upTo),
    },
    'studio',
  );
  /** On wide screens the customer panel starts open. */
  protected readonly wide =
    typeof matchMedia === 'function' && matchMedia('(min-width: 60rem)').matches;

  /** The images of the loaded history, newest first: the choice for a reference image. */
  protected readonly conversationImages = computed(() => imagesOf(this.session.messages()));

  private readonly thread = viewChild(ChatThread);
  private readonly fallbackName = $localize`:@@admin.conversation.customer:Customer`;
  private readonly deletedName = $localize`:@@admin.conversations.deleted:Deleted account`;
  private readonly seo = inject(Seo);

  constructor() {
    this.seo.setNoIndexPage($localize`:@@admin.conversations.title:Conversations`);
    const destroy = inject(DestroyRef);

    afterNextRender(() => {
      void this.load();
      const onVisible = () => this.markRead();
      this.document.addEventListener('visibilitychange', onVisible);
      destroy.onDestroy(() => this.document.removeEventListener('visibilitychange', onVisible));
    });

    this.realtime.messages.pipe(takeUntilDestroyed()).subscribe((message) => {
      if (message.conversationId !== this.id()) {
        return;
      }

      const follow = this.thread()?.isAtBottom() ?? true;
      this.session.add([message]);
      if (follow || message.from === 'studio') {
        this.thread()?.scrollToBottom();
      }

      this.markRead();
    });

    this.realtime.reads.pipe(takeUntilDestroyed()).subscribe((receipt) => {
      if (receipt.conversationId === this.id()) {
        this.session.applyReceipt(receipt);
      }
    });

    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => {
      void this.session.loadNewest().then(() => this.markRead());
      void this.loadOrders();
    });

    this.realtime.orders.pipe(takeUntilDestroyed()).subscribe((order) => {
      if (order.conversationId === this.id()) {
        void this.loadOrders();
      }
    });
  }

  protected customerName(): string {
    const customer = this.detail()?.customer;
    if (!customer) {
      return this.fallbackName;
    }

    return customer.isDeleted ? this.deletedName : customer.displayName || this.fallbackName;
  }

  protected language(code: string): string {
    return SUPPORTED_LOCALES.find((locale) => locale === code)
      ? LOCALE_NAMES[code as keyof typeof LOCALE_NAMES]
      : code;
  }

  protected readonly send: SendMessage = async (body, attachmentIds) => {
    const message = await this.api.send(this.id(), body, attachmentIds);
    this.session.add([message]);
    this.thread()?.scrollToBottom();
  };

  protected readonly createOrder: SaveOrder = async (value) => {
    const order = await this.ordersApi.create({
      conversationId: this.id(),
      title: value.title,
      description: value.description || null,
      price: value.price ?? 0,
      referenceAttachmentId: value.referenceId,
      adminNote: value.adminNote || null,
    });
    this.creating.set(false);
    // The draft is checked and confirmed on its own page.
    await this.router.navigate(['/admin/orders', order.id]);
  };

  private async loadOrders(): Promise<void> {
    try {
      this.orders.set((await this.ordersApi.list({ conversationId: this.id() })).items);
      this.ordersError.set(null);
    } catch (error) {
      this.ordersError.set(errorMessage(toApiError(error)));
    }
  }

  private async load(): Promise<void> {
    void this.loadOrders();
    try {
      this.detail.set(await this.api.get(this.id()));
      this.seo.setNoIndexPage(this.customerName());
    } catch (error) {
      this.detailError.set(errorMessage(toApiError(error)));
    }

    await this.session.loadNewest();
    this.thread()?.scrollToBottom();
    this.markRead();
  }

  /** Only while the page is visible: the customer sees "Read" when an admin has really looked. */
  private markRead(): void {
    if (this.document.visibilityState === 'visible') {
      this.session.markRead();
    }
  }
}
