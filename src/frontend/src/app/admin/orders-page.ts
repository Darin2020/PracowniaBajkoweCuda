import { CurrencyPipe, DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { AdminOrdersApi } from '../core/orders/orders-api.service';
import {
  ORDER_STATUSES,
  OrderStatus,
  OrderSummary,
  statusLabel,
} from '../core/orders/order.models';
import { Seo } from '../core/seo';
import { OrderStatusBadge } from '../shared/orders/order-status-badge';

/** Bursts of events refresh the list once. */
const REFRESH_DELAY = 400;

/**
 * `/admin/orders` (docs/PLAN.md §3.5): every order, newest first, filtered by status (`?status=` keeps the filter
 * in the address). Refreshes itself when an order changes.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, OrderStatusBadge, RouterLink],
  selector: 'app-admin-orders-page',
  styleUrl: '../orders/orders-page.scss',
  template: `
    <section class="page page--wide container">
      <h1>{{ title }}</h1>

      <div class="field">
        <label for="order-status" i18n="@@admin.orders.filter">Status</label>
        <select
          id="order-status"
          class="input"
          [value]="selected() ?? ''"
          (change)="filter($any($event.target).value)"
        >
          <option value="" i18n="@@admin.orders.all">All orders</option>
          @for (option of statuses; track option) {
            <option [value]="option" [selected]="selected() === option">{{ label(option) }}</option>
          }
        </select>
      </div>

      @if (error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      @if (loading() && items().length === 0) {
        <p role="status" i18n="@@common.loading">Loading…</p>
      } @else if (items().length === 0 && !error()) {
        <p class="hint" i18n="@@admin.orders.none">
          No orders here. Orders are created from a conversation.
        </p>
      }

      <ul class="orders">
        @for (order of items(); track order.id) {
          <li>
            <a class="order" [routerLink]="['/admin/orders', order.id]">
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
                <span class="order__customer"
                  >{{ order.customerName || deletedName }} ·
                  <time [attr.datetime]="order.createdAt">{{
                    order.createdAt | date: 'mediumDate'
                  }}</time></span
                >
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

      @if (cursor()) {
        <p>
          <button
            class="button"
            type="button"
            [disabled]="loading()"
            (click)="more()"
            i18n="@@admin.conversations.more"
          >
            Show more
          </button>
        </p>
      }
    </section>
  `,
})
export class AdminOrdersPage {
  private readonly api = inject(AdminOrdersApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** The `?status=` query parameter. */
  readonly status = input<string>();

  protected readonly title = $localize`:@@admin.orders.title:Orders`;
  protected readonly deletedName = $localize`:@@admin.conversations.deleted:Deleted account`;
  protected readonly statuses = ORDER_STATUSES;
  protected readonly items = signal<OrderSummary[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected selected(): OrderStatus | null {
    const value = this.status();
    return ORDER_STATUSES.find((status) => status === value) ?? null;
  }

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    // The first load, and again whenever the filter in the address changes.
    effect(() => {
      this.status();
      untracked(() => void this.load());
    });

    const realtime = inject(ChatRealtime);
    merge(realtime.orders, realtime.reconnected)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        clearTimeout(this.refreshTimer);
        this.refreshTimer = setTimeout(() => void this.load(), REFRESH_DELAY);
      });

    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected label(status: OrderStatus): string {
    return statusLabel(status);
  }

  protected async filter(value: string): Promise<void> {
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status: value || null },
      replaceUrl: true,
    });
  }

  protected async more(): Promise<void> {
    await this.load(this.cursor());
  }

  private async load(cursor: string | null = null): Promise<void> {
    this.loading.set(true);
    const status = this.selected();
    try {
      const page = await this.api.list({ status, cursor });
      if (status !== this.selected()) {
        return;
      }

      this.items.update((items) => (cursor ? [...items, ...page.items] : [...page.items]));
      this.cursor.set(page.nextCursor);
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.loading.set(false);
    }
  }
}
