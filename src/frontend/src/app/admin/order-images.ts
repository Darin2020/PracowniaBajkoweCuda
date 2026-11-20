import { AdminChatApi } from '../core/chat/admin-chat-api.service';
import { ChatImage, ChatMessage } from '../core/chat/chat.models';

/** How many pages of history are searched for images to pick a reference from. */
const PAGES = 3;

/** The images of a list of messages, newest first. */
export function imagesOf(messages: readonly ChatMessage[]): ChatImage[] {
  return messages
    .filter((message) => message.kind === 'text')
    .flatMap((message) => [...message.images].reverse())
    .reverse();
}

/** The images sent in a conversation (the newest pages of it), newest first. */
export async function conversationImages(
  api: AdminChatApi,
  conversationId: string,
): Promise<ChatImage[]> {
  const messages: ChatMessage[] = [];
  let cursor: string | null = null;
  for (let page = 0; page < PAGES; page++) {
    const result = await api.messages(conversationId, cursor, 50);
    messages.push(...result.items);
    cursor = result.nextCursor;
    if (!cursor) {
      break;
    }
  }

  // Pages are newest first; imagesOf expects oldest first.
  return imagesOf([...messages].reverse());
}
