import { signal } from '@angular/core';
import { errorMessage, toApiError } from '../core/api/api-error';
import { ChatMessage, ChatPage, ReadReceipt, mergeMessages } from '../core/chat/chat.models';

/** Which side the page shows the conversation for: the customer, or the studio (any admin). */
export type ChatSide = 'customer' | 'studio';

/** Where a conversation's history comes from and how reading is reported (the customer's or the admin's API). */
export interface ChatSource {
  page(before?: string | null): Promise<ChatPage>;
  markRead(upToMessageId: string): Promise<void>;
}

/**
 * The state of one open conversation, shared by the customer's chat and the admin's conversation page: the messages
 * (oldest first, merged by id), paging back in time, live messages and read receipts.
 */
export class ChatSession {
  readonly messages = signal<ChatMessage[]>([]);
  readonly loading = signal(true);
  readonly loadingOlder = signal(false);
  readonly error = signal<string | null>(null);
  private cursor: string | null = null;

  constructor(
    private readonly source: ChatSource,
    readonly reader: ChatSide,
  ) {}

  /** The newest page (also after a reconnect: it fills what may have been missed). */
  async loadNewest(): Promise<void> {
    try {
      const page = await this.source.page();
      if (this.messages().length === 0) {
        this.cursor = page.nextCursor;
      }

      this.add(page.items);
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.loading.set(false);
    }
  }

  get hasOlder(): boolean {
    return this.cursor !== null;
  }

  /** The next older page; resolves to whether anything was added. */
  async loadOlder(): Promise<boolean> {
    if (!this.cursor || this.loadingOlder() || this.loading()) {
      return false;
    }

    this.loadingOlder.set(true);
    try {
      const page = await this.source.page(this.cursor);
      this.cursor = page.nextCursor;
      this.add(page.items);
      return page.items.length > 0;
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
      return false;
    } finally {
      this.loadingOlder.set(false);
    }
  }

  add(messages: readonly ChatMessage[]): void {
    this.messages.update((current) => mergeMessages(current, messages));
  }

  /** The other side has read up to a message: show it under this side's messages. */
  applyReceipt(receipt: ReadReceipt): void {
    if (receipt.by === this.reader) {
      return;
    }

    const upTo = this.messages().find((message) => message.id === receipt.upToMessageId);
    if (!upTo) {
      return;
    }

    const field = receipt.by === 'studio' ? 'readByAdminAt' : 'readByCustomerAt';
    this.messages.update((messages) =>
      messages.map((message) =>
        !message[field] && message.createdAt <= upTo.createdAt
          ? { ...message, [field]: receipt.readAt }
          : message,
      ),
    );
  }

  /** Marks the other side's messages read on this side, and tells the API (once, for the newest). */
  markRead(): void {
    const field = this.reader === 'customer' ? 'readByCustomerAt' : 'readByAdminAt';
    const unread = this.messages().filter(
      (message) => message.from !== this.reader && !message[field],
    );
    const newest = unread.at(-1);
    if (!newest) {
      return;
    }

    const now = new Date().toISOString();
    this.messages.update((messages) =>
      messages.map((message) =>
        message.from !== this.reader && !message[field] ? { ...message, [field]: now } : message,
      ),
    );
    this.source.markRead(newest.id).catch(() => {
      // Not critical: the badge corrects itself with the next event.
    });
  }
}
