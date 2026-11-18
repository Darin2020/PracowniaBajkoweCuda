// Shapes of /api/chat and the SignalR events (docs/PLAN.md §6). Keep in step with ChatModels.cs.

export interface ChatImage {
  readonly id: string;
  readonly url: string;
  readonly thumbnailUrl: string;
  readonly width: number;
  readonly height: number;
}

export interface ChatMessage {
  readonly id: string;
  readonly conversationId: string;
  readonly from: 'customer' | 'studio';
  readonly kind: 'text' | 'system';
  readonly body: string;
  readonly systemEventType: string | null;
  readonly systemEventPayload: unknown;
  readonly orderId: string | null;
  readonly createdAt: string;
  readonly readByCustomerAt: string | null;
  readonly readByAdminAt: string | null;
  readonly images: readonly ChatImage[];
}

export interface ChatPage {
  readonly items: readonly ChatMessage[];
  readonly nextCursor: string | null;
}

export interface CustomerChat {
  readonly conversationId: string | null;
  readonly emailNotificationsEnabled: boolean;
  readonly emailPromptAnswered: boolean;
  readonly unreadCount: number;
}

export interface ReadReceipt {
  readonly conversationId: string;
  readonly customerId: string;
  readonly upToMessageId: string;
  readonly by: 'customer' | 'studio';
  readonly readAt: string;
  readonly marked: number;
}

/** An uploaded image waiting to be sent (`POST /api/chat/attachments`). */
export interface UploadedImage {
  readonly id: string;
  readonly url: string;
  readonly thumbnailUrl: string;
  readonly width: number;
  readonly height: number;
}

export const MAX_MESSAGE_LENGTH = 2000;
export const MAX_IMAGES = 5;
export const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
export const ACCEPTED_IMAGE_TYPES = 'image/jpeg,image/png,image/webp';

/**
 * Adds messages to a list sorted oldest first, without duplicates: the same message can arrive through the API and
 * through SignalR (and from the user's other tabs). A newer copy replaces the old one, keeping known read times.
 */
export function mergeMessages(
  current: readonly ChatMessage[],
  incoming: readonly ChatMessage[],
): ChatMessage[] {
  const byId = new Map(current.map((message) => [message.id, message]));
  for (const message of incoming) {
    const known = byId.get(message.id);
    // Read times only ever get set: a copy that does not know them yet must not clear them.
    byId.set(
      message.id,
      known
        ? {
            ...message,
            readByCustomerAt: message.readByCustomerAt ?? known.readByCustomerAt,
            readByAdminAt: message.readByAdminAt ?? known.readByAdminAt,
          }
        : message,
    );
  }

  return [...byId.values()].sort(
    (a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id),
  );
}
