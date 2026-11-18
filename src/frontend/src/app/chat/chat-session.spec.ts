import { ChatPage } from '../core/chat/chat.models';
import { message } from '../testing/chat-testing';
import { ChatSession, ChatSource } from './chat-session';

class FakeSource implements ChatSource {
  readonly pages: ChatPage[] = [];
  readonly asked: (string | null | undefined)[] = [];
  readonly reads: string[] = [];

  page(before?: string | null): Promise<ChatPage> {
    this.asked.push(before);
    return Promise.resolve(this.pages.shift() ?? { items: [], nextCursor: null });
  }

  markRead(upToMessageId: string): Promise<void> {
    this.reads.push(upToMessageId);
    return Promise.resolve();
  }
}

describe('ChatSession', () => {
  it('pages back in time and keeps the messages oldest first', async () => {
    const source = new FakeSource();
    source.pages.push(
      {
        items: [message('m3', '2026-11-18T10:03:00Z'), message('m2', '2026-11-18T10:02:00Z')],
        nextCursor: 'm2',
      },
      { items: [message('m1', '2026-11-18T10:01:00Z')], nextCursor: null },
    );
    const session = new ChatSession(source, 'customer');

    await session.loadNewest();
    expect(session.hasOlder).toBe(true);
    expect(await session.loadOlder()).toBe(true);

    expect(source.asked).toEqual([undefined, 'm2']);
    expect(session.messages().map((item) => item.id)).toEqual(['m1', 'm2', 'm3']);
    expect(session.hasOlder).toBe(false);
    expect(await session.loadOlder()).toBe(false);
  });

  it('marks the other side’s messages read, once, up to the newest', async () => {
    const source = new FakeSource();
    source.pages.push({
      items: [
        message('s2', '2026-11-18T10:02:00Z', { from: 'customer', readByAdminAt: null }),
        message('s1', '2026-11-18T10:01:00Z', { from: 'customer', readByAdminAt: null }),
        message('mine', '2026-11-18T10:00:00Z', {
          from: 'studio',
          readByAdminAt: '2026-11-18T10:00:00Z',
        }),
      ],
      nextCursor: null,
    });
    const session = new ChatSession(source, 'studio');
    await session.loadNewest();

    session.markRead();
    session.markRead();

    expect(source.reads).toEqual(['s2']);
    expect(session.messages().every((item) => item.readByAdminAt)).toBe(true);
  });

  it('shows the other side’s read receipt under this side’s messages only', async () => {
    const source = new FakeSource();
    source.pages.push({
      items: [
        message('a', '2026-11-18T10:00:00Z', { from: 'studio', readByCustomerAt: null }),
        message('b', '2026-11-18T10:05:00Z', { from: 'studio', readByCustomerAt: null }),
      ],
      nextCursor: null,
    });
    const session = new ChatSession(source, 'studio');
    await session.loadNewest();

    session.applyReceipt({
      conversationId: 'c1',
      customerId: 'u1',
      upToMessageId: 'a',
      by: 'customer',
      readAt: '2026-11-18T10:06:00Z',
      marked: 1,
    });

    expect(session.messages().map((item) => item.readByCustomerAt)).toEqual([
      '2026-11-18T10:06:00Z',
      null,
    ]);
  });
});
