import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { OrdersApi } from '../core/orders/orders-api.service';
import { CustomerOrder } from '../core/orders/order.models';
import { Seo } from '../core/seo';
import { OrderStatusBadge } from '../shared/orders/order-status-badge';
import { OrderTimeline } from '../shared/orders/order-timeline';
import { PhotoGallery } from '../shared/orders/photo-gallery';

/**
 * `/orders/:id` (docs/PLAN.md §3.5): one of the customer's orders: reference image, description, price, the payment
 * instructions while payment is awaited, the status timeline, the photos of the finished toy and the cancellation
 * reason. The link in the confirmation email leads here.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, OrderStatusBadge, OrderTimeline, PhotoGallery, RouterLink],
  selector: 'app-order-page',
  styleUrl: './order-page.scss',
  template: `
    <section class="page page--wide container">
      <a routerLink="/orders" i18n="@@orders.back">← My orders</a>

      @if (error(); as message) {
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
        </header>

        @if (item.status === 'Cancelled' && item.cancellationReason) {
          <div class="alert alert--error">
            <strong i18n="@@order.cancelledBecause">The order was cancelled:</strong>
            <p class="pre">{{ item.cancellationReason }}</p>
          </div>
        }

        @if (item.paymentInstructions) {
          <section class="card">
            <h2 i18n="@@order.payment">How to pay</h2>
            <p class="pre">{{ item.paymentInstructions }}</p>
          </section>
        }

        <div class="order-body">
          @if (item.referenceImage; as image) {
            <section>
              <h2 i18n="@@order.reference">Reference</h2>
              <app-photo-gallery [images]="[image]" />
            </section>
          }

          @if (item.description) {
            <section>
              <h2 i18n="@@order.description">Description</h2>
              <p class="pre">{{ item.description }}</p>
            </section>
          }
        </div>

        @if (item.photos.length > 0) {
          <section>
            <h2 i18n="@@order.photos">Photos of your toy</h2>
            <app-photo-gallery [images]="item.photos" />
          </section>
        }

        <section>
          <h2 i18n="@@order.timeline">Progress</h2>
          <app-order-timeline [steps]="item.timeline" />
        </section>

        <p class="hint">
          <ng-container i18n="@@order.questions">Questions about the order?</ng-container>&ngsp;
          <a routerLink="/chat" i18n="@@order.writeUs">Write to us in the chat</a>
        </p>
      } @else if (!error()) {
        <p role="status" i18n="@@common.loading">Loading…</p>
      }
    </section>
  `,
})
export class OrderPage {
  private readonly api = inject(OrdersApi);
  private readonly seo = inject(Seo);

  /** The route parameter. */
  readonly id = input.required<string>();

  protected readonly order = signal<CustomerOrder | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.seo.setNoIndexPage($localize`:@@orders.title:My orders`);
    effect(() => void this.load(this.id()));

    const realtime = inject(ChatRealtime);
    merge(realtime.orders, realtime.reconnected)
      .pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (!event || event.id === this.id()) {
          void this.load(this.id());
        }
      });
  }

  private async load(id: string): Promise<void> {
    try {
      const order = await this.api.get(id);
      if (id !== this.id()) {
        return;
      }

      this.order.set(order);
      this.error.set(null);
      this.seo.setNoIndexPage(`${order.number} · ${order.title}`);
    } catch (error) {
      this.order.set(null);
      this.error.set(errorMessage(toApiError(error)));
    }
  }
}
