import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { OrderStatus, statusLabel } from '../../core/orders/order.models';

/** The status of an order as a coloured pill (the colour only repeats the text). */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-order-status-badge',
  host: { '[class]': '"status--" + status()' },
  styles: `
    :host {
      display: inline-block;
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      background: var(--color-surface-tinted);
      color: var(--color-text);
      font-size: 0.8125rem;
      font-weight: 600;
      white-space: nowrap;
    }

    :host(.status--Draft) {
      background: transparent;
      box-shadow: inset 0 0 0 1px var(--color-border);
      color: var(--color-text-muted);
    }

    :host(.status--AwaitingPayment) {
      background: var(--color-info-surface);
    }

    :host(.status--Ready),
    :host(.status--Shipped),
    :host(.status--Completed) {
      background: var(--color-success-surface);
      color: var(--color-success);
    }

    :host(.status--Cancelled) {
      background: var(--color-error-surface);
      color: var(--color-error);
    }
  `,
  template: `{{ label() }}`,
})
export class OrderStatusBadge {
  readonly status = input.required<OrderStatus>();
  protected readonly label = computed(() => statusLabel(this.status()));
}
