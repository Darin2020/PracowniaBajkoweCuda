import { message } from '../../testing/chat-testing';
import { mergeMessages } from './chat.models';

describe('mergeMessages', () => {
  it('keeps the list sorted oldest first', () => {
    const merged = mergeMessages(
      [message('b', '2026-11-18T10:05:00Z')],
      [message('c', '2026-11-18T10:09:00Z'), message('a', '2026-11-18T10:01:00Z')],
    );

    expect(merged.map((item) => item.id)).toEqual(['a', 'b', 'c']);
  });

  it('does not duplicate a message that arrives twice, and keeps the newer copy', () => {
    const merged = mergeMessages(
      [message('a', '2026-11-18T10:01:00Z')],
      [message('a', '2026-11-18T10:01:00Z', { readByAdminAt: '2026-11-18T10:02:00Z' })],
    );

    expect(merged).toHaveLength(1);
    expect(merged[0].readByAdminAt).toBe('2026-11-18T10:02:00Z');
  });

  it('does not clear a read time that an older copy of the message does not know', () => {
    const merged = mergeMessages(
      [message('a', '2026-11-18T10:01:00Z', { readByAdminAt: '2026-11-18T10:02:00Z' })],
      [message('a', '2026-11-18T10:01:00Z', { readByAdminAt: null })],
    );

    expect(merged[0].readByAdminAt).toBe('2026-11-18T10:02:00Z');
  });
});
