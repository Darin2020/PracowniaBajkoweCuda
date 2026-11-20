import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { OrdersApi } from '../core/orders/orders-api.service';
import { OrderSummary } from '../core/orders/order.models';
import { Seo } from '../core/seo';
import { OrderStatusBadge } from '../shared/orders/order-status-badge';

/**
 * `/orders` (docs/PLAN.md §3.5): the customer's orders, newest first, with thumbnail, number, status and price.
 * Updates itself when the studio moves an order on.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, OrderStatusBadge, RouterLink],
  selector: 'app-orders-page',
  styleUrl: './orders-page.scss',
  template: `
    <section class="page page--wide container">
      <h1>{{ title }}</h1>

      @if (error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      @if (loading()) {
        <p role="status" i18n="@@common.loading">Loading…</p>
      } @else if (orders().length === 0 && !error()) {
        <p class="hint" i18n="@@orders.none">
          You have no orders yet. Tell us about your idea in the chat and we will prepare one for
          you.
        </p>
        <p>
          <a class="button button--primary" routerLink="/chat" i18n="@@orders.toChat"
            >Open the chat</a
          >
        </p>
      }

      <ul class="orders">
        @for (order of orders(); track order.id) {
          <li>
            <a class="order" [routerLink]="['/orders', order.id]">
              @if (order.thumbnail; as image) {
                <img
                  class="order__image"
                  [src]="image.thumbnailUrl"
                  alt=""
                  width="64"
                  height="64"
                />
              } @else {
                <span class="order__image order__image--empty" aria-hidden="true"></span>
              }
              <span class="order__text">
                <span class="order__number">{{ order.number }}</span>
                <span class="order__title">{{ order.title }}</span>
                <time class="order__date" [attr.datetime]="order.createdAt">{{
                  order.createdAt | date: 'mediumDate'
                }}</time>
              </span>
              <span class="order__side">
                <app-order-status-badge [status]="order.status" />
                <span class="order__price">{{
                  order.price.amount | currency: order.price.currency
                }}</span>
              </span>
            </a>
          </li>
        }
      </ul>
    </section>
  `,
})
export class OrdersPage {
  private readonly api = inject(OrdersApi);

  protected readonly title = $localize`:@@orders.title:My orders`;
  protected readonly orders = signal<readonly OrderSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    void this.load();

    const realtime = inject(ChatRealtime);
    merge(realtime.orders, realtime.reconnected)
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.orders.set(await this.api.list());
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.loading.set(false);
    }
  }
}
