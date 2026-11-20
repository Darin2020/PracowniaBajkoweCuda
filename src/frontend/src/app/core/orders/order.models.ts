import { ChatImage, ChatMessage } from '../chat/chat.models';

// Shapes of /api/orders, /api/admin/orders and the OrderUpdated event (docs/PLAN.md §6). Keep in step with
// OrderModels.cs.

export const ORDER_STATUSES = [
  'Draft',
  'AwaitingPayment',
  'InProgress',
  'Ready',
  'Shipped',
  'Completed',
  'Cancelled',
] as const;

export type OrderStatus = (typeof ORDER_STATUSES)[number];

export interface OrderPrice {
  readonly amount: number;
  readonly currency: string;
}

export interface OrderStep {
  readonly status: OrderStatus;
  readonly at: string;
  readonly comment: string | null;
}

/** A row of a list, and the payload of `OrderUpdated`. */
export interface OrderSummary {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly status: OrderStatus;
  readonly price: OrderPrice;
  readonly createdAt: string;
  readonly customerId: string;
  readonly customerName: string;
  readonly conversationId: string | null;
  readonly thumbnail: ChatImage | null;
}

export interface OrderPage {
  readonly items: readonly OrderSummary[];
  readonly nextCursor: string | null;
}

/** The customer's view: no drafts, no internal note. */
export interface CustomerOrder {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly description: string;
  readonly status: OrderStatus;
  readonly price: OrderPrice;
  readonly createdAt: string;
  readonly cancellationReason: string | null;
  readonly referenceImage: ChatImage | null;
  readonly photos: readonly ChatImage[];
  readonly timeline: readonly OrderStep[];
  readonly paymentInstructions: string | null;
}

export interface AdminOrderPhoto {
  readonly id: string;
  readonly image: ChatImage;
}

/** The studio's view, with the internal note and the concurrency token. */
export interface AdminOrder {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly description: string;
  readonly status: OrderStatus;
  readonly price: OrderPrice;
  readonly adminNote: string | null;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
  readonly customerId: string;
  readonly customerName: string;
  readonly conversationId: string | null;
  readonly referenceImage: ChatImage | null;
  readonly photos: readonly AdminOrderPhoto[];
  readonly timeline: readonly OrderStep[];
  readonly rowVersion: string;
}

export interface CreateOrder {
  readonly conversationId: string;
  readonly title: string;
  readonly description: string | null;
  readonly price: number;
  readonly referenceAttachmentId: string | null;
  readonly adminNote: string | null;
}

/** Only the fields that are set change; `rowVersion` is the version the admin saw. */
export interface UpdateOrder {
  readonly title?: string;
  readonly description?: string;
  readonly price?: number;
  readonly referenceAttachmentId?: string | null;
  readonly clearReferenceImage?: boolean;
  readonly adminNote?: string;
  readonly rowVersion: string;
}

/** The steps an admin can take, as the API spells them (`POST /api/admin/orders/{id}/{step}`). */
export type OrderStepAction =
  'confirm' | 'mark-paid' | 'mark-ready' | 'mark-shipped' | 'complete' | 'cancel';

export const TITLE_MAX_LENGTH = 200;
export const DESCRIPTION_MAX_LENGTH = 4000;
export const ADMIN_NOTE_MAX_LENGTH = 2000;
export const REASON_MAX_LENGTH = 1000;
export const MAX_ORDER_PHOTOS = 20;

/** The next step forward from a status (cancelling is separate: from every status that is not final). */
export function nextStep(status: OrderStatus): Exclude<OrderStepAction, 'cancel'> | null {
  switch (status) {
    case 'Draft':
      return 'confirm';
    case 'AwaitingPayment':
      return 'mark-paid';
    case 'InProgress':
      return 'mark-ready';
    case 'Ready':
      return 'mark-shipped';
    case 'Shipped':
      return 'complete';
    default:
      return null;
  }
}

export const isFinal = (status: OrderStatus): boolean =>
  status === 'Completed' || status === 'Cancelled';

/** Price and reference image (and the text) change only before payment (PLAN §3.5). */
export const termsEditable = (status: OrderStatus): boolean =>
  status === 'Draft' || status === 'AwaitingPayment';

/** Photos of the finished toy are managed while it is being made or is ready. */
export const photosOpen = (status: OrderStatus): boolean =>
  status === 'InProgress' || status === 'Ready';

export function statusLabel(status: OrderStatus): string {
  switch (status) {
    case 'Draft':
      return $localize`:@@order.status.draft:Draft`;
    case 'AwaitingPayment':
      return $localize`:@@order.status.awaitingPayment:Awaiting payment`;
    case 'InProgress':
      return $localize`:@@order.status.inProgress:In progress`;
    case 'Ready':
      return $localize`:@@order.status.ready:Ready`;
    case 'Shipped':
      return $localize`:@@order.status.shipped:Shipped`;
    case 'Completed':
      return $localize`:@@order.status.completed:Completed`;
    case 'Cancelled':
      return $localize`:@@order.status.cancelled:Cancelled`;
  }
}

export function stepLabel(step: OrderStepAction): string {
  switch (step) {
    case 'confirm':
      return $localize`:@@order.step.confirm:Confirm and send to the customer`;
    case 'mark-paid':
      return $localize`:@@order.step.markPaid:Payment received`;
    case 'mark-ready':
      return $localize`:@@order.step.markReady:Mark as ready`;
    case 'mark-shipped':
      return $localize`:@@order.step.markShipped:Mark as shipped`;
    case 'complete':
      return $localize`:@@order.step.complete:Complete`;
    case 'cancel':
      return $localize`:@@order.step.cancel:Cancel the order`;
  }
}

/** What a system message of the chat says about an order (its payload, written by OrderService). */
export interface OrderEvent {
  readonly type: string;
  readonly orderId: string | null;
  readonly number: string;
  readonly title: string;
  readonly amount: number | null;
  readonly currency: string;
  readonly paymentInstructions: string | null;
  readonly reason: string | null;
}

export function orderEvent(message: ChatMessage): OrderEvent | null {
  if (message.kind !== 'system' || !message.systemEventType?.startsWith('order.')) {
    return null;
  }

  const payload = (
    message.systemEventPayload && typeof message.systemEventPayload === 'object'
      ? message.systemEventPayload
      : {}
  ) as Record<string, unknown>;
  const text = (key: string): string | null =>
    typeof payload[key] === 'string' && payload[key] ? payload[key] : null;
  const amount = Number(text('amount'));

  return {
    type: message.systemEventType,
    orderId: message.orderId ?? text('orderId'),
    number: text('number') ?? '',
    title: text('title') ?? '',
    amount: Number.isFinite(amount) && text('amount') !== null ? amount : null,
    currency: text('currency') ?? 'EUR',
    paymentInstructions: text('paymentInstructions'),
    reason: text('reason'),
  };
}

/** The headline of an order's system message, in the reader's language. */
export function orderEventTitle(event: OrderEvent): string {
  const number = event.number;
  switch (event.type) {
    case 'order.confirmed':
      return $localize`:@@order.event.confirmed:Order ${number}:NUMBER: has been confirmed`;
    case 'order.paid':
      return $localize`:@@order.event.paid:Payment for order ${number}:NUMBER: has been received. Work begins!`;
    case 'order.ready':
      return $localize`:@@order.event.ready:Order ${number}:NUMBER: is ready`;
    case 'order.shipped':
      return $localize`:@@order.event.shipped:Order ${number}:NUMBER: has been shipped`;
    case 'order.completed':
      return $localize`:@@order.event.completed:Order ${number}:NUMBER: is completed. Thank you!`;
    case 'order.cancelled':
      return $localize`:@@order.event.cancelled:Order ${number}:NUMBER: has been cancelled`;
    default:
      return $localize`:@@chat.orderUpdate:Update about your order`;
  }
}
