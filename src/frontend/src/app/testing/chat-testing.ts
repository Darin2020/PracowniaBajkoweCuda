import { Subject } from 'rxjs';
import { ChatMessage, ReadReceipt } from '../core/chat/chat.models';
import { AdminOrder, CustomerOrder, OrderSummary } from '../core/orders/order.models';

/** Stands in for ChatRealtime: tests push the server's events. */
export class FakeRealtime {
  readonly messages = new Subject<ChatMessage>();
  readonly reads = new Subject<ReadReceipt>();
  readonly reconnected = new Subject<void>();
  readonly orders = new Subject<OrderSummary>();
  readonly state = () => 'connected';
}

/** A chat message for tests; `change` overrides any field. */
export function message(
  id: string,
  createdAt: string,
  change: Partial<ChatMessage> = {},
): ChatMessage {
  return {
    id,
    conversationId: 'c1',
    from: 'customer',
    kind: 'text',
    body: `Message ${id}`,
    systemEventType: null,
    systemEventPayload: null,
    orderId: null,
    createdAt,
    readByCustomerAt: createdAt,
    readByAdminAt: null,
    images: [],
    ...change,
  };
}

/** An image of `/api/files` for tests. */
export const image = (id: string) => ({
  id,
  url: `/api/files/${id}`,
  thumbnailUrl: `/api/files/${id}/thumbnail`,
  width: 800,
  height: 600,
});

/** A row of an order list for tests. */
export function orderSummary(id: string, change: Partial<OrderSummary> = {}): OrderSummary {
  return {
    id,
    number: 'BC-0012',
    title: 'Fox with a scarf',
    status: 'AwaitingPayment',
    price: { amount: 149.9, currency: 'EUR' },
    createdAt: '2026-11-18T10:00:00Z',
    customerId: 'u1',
    customerName: 'Anna',
    conversationId: 'c1',
    thumbnail: null,
    ...change,
  };
}

/** The customer's view of an order for tests. */
export function customerOrder(id: string, change: Partial<CustomerOrder> = {}): CustomerOrder {
  return {
    id,
    number: 'BC-0012',
    title: 'Fox with a scarf',
    description: 'Orange fox, 30 cm',
    status: 'AwaitingPayment',
    price: { amount: 149.9, currency: 'EUR' },
    createdAt: '2026-11-18T10:00:00Z',
    cancellationReason: null,
    referenceImage: image('ref'),
    photos: [],
    timeline: [{ status: 'AwaitingPayment', at: '2026-11-18T11:00:00Z', comment: null }],
    paymentInstructions: 'Transfer 149.90 EUR, title BC-0012',
    ...change,
  };
}

/** The studio's view of an order for tests. */
export function adminOrder(id: string, change: Partial<AdminOrder> = {}): AdminOrder {
  return {
    id,
    number: 'BC-0012',
    title: 'Fox with a scarf',
    description: 'Orange fox, 30 cm',
    status: 'Draft',
    price: { amount: 149.9, currency: 'EUR' },
    adminNote: 'Use the soft felt',
    cancellationReason: null,
    createdAt: '2026-11-18T10:00:00Z',
    customerId: 'u1',
    customerName: 'Anna',
    conversationId: 'c1',
    referenceImage: null,
    photos: [],
    timeline: [],
    rowVersion: 'v1',
    ...change,
  };
}
