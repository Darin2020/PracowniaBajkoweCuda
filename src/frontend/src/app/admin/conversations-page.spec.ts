import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, message } from '../testing/chat-testing';
import { ConversationsPage } from './conversations-page';

const conversation = (id: string, name: string, unreadCount: number) => ({
  id,
  customer: {
    id: `u-${id}`,
    displayName: name,
    email: `${id}@example.test`,
    preferredLanguage: 'pl',
    isDeleted: false,
  },
  lastMessageAt: '2026-11-18T10:00:00Z',
  lastMessagePreview: `Hello from ${name}`,
  lastMessageImageCount: 0,
  lastMessageKind: 'text',
  unreadCount,
});

describe('ConversationsPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([]),
        { provide: ChatRealtime, useValue: realtime },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function open() {
    const fixture = TestBed.createComponent(ConversationsPage);
    http.expectOne('/api/admin/conversations').flush({
      items: [conversation('c1', 'Anna', 2), conversation('c2', 'Bob', 0)],
      nextCursor: null,
    });
    await settle();
    fixture.detectChanges();
    return fixture;
  }

  it('lists the conversations with unread counts and links', async () => {
    const fixture = await open();
    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.conversation'),
    );

    expect(rows.map((row) => row.querySelector('.conversation__name')?.textContent)).toEqual([
      'Anna',
      'Bob',
    ]);
    expect(rows[0].classList).toContain('conversation--unread');
    expect(rows[0].querySelector('.conversation__badge')?.textContent).toContain('2');
    expect(rows[0].getAttribute('href')).toBe('/pl/admin/conversations/c1');
  });

  it('searches after typing pauses', async () => {
    const fixture = await open();
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '#conversation-search',
    )!;

    input.value = 'bob';
    input.dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(300);

    const request = http.expectOne((candidate) => candidate.url === '/api/admin/conversations');
    expect(request.request.params.get('search')).toBe('bob');
    request.flush({ items: [conversation('c2', 'Bob', 0)], nextCursor: null });
  });

  it('refreshes once after a burst of chat events', async () => {
    await open();

    realtime.messages.next(message('m1', '2026-11-18T10:01:00Z'));
    realtime.messages.next(message('m2', '2026-11-18T10:02:00Z'));
    await vi.advanceTimersByTimeAsync(400);

    http.expectOne('/api/admin/conversations').flush({ items: [], nextCursor: null });
  });
});
