import { ChatMessage } from '../core/chat/chat.models';

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
