import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject } from 'rxjs';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { ChatMessage, ReadReceipt } from '../core/chat/chat.models';
import { provideApiTesting } from '../testing/auth-testing';
import { message } from '../testing/chat-testing';
import { ConversationPage } from './conversation-page';

class FakeRealtime {
  readonly messages = new Subject<ChatMessage>();
  readonly reads = new Subject<ReadReceipt>();
  readonly reconnected = new Subject<void>();
  readonly state = () => 'connected';
}

describe('ConversationPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [{ path: 'admin/conversations/:id', component: ConversationPage }],
          withComponentInputBinding(),
        ),
        { provide: ChatRealtime, useValue: realtime },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function open() {
    const harness = await RouterTestingHarness.create('/admin/conversations/c1');
    await settle();
    http.expectOne('/api/admin/conversations/c1').flush({
      id: 'c1',
      customer: {
        id: 'u1',
        displayName: 'Anna',
        email: 'anna@example.test',
        preferredLanguage: 'pl',
        isDeleted: false,
      },
      createdAt: '2026-11-01T09:00:00Z',
      lastMessageAt: '2026-11-18T10:00:00Z',
      customerEmailNotificationsEnabled: true,
      unreadCount: 1,
    });
    await settle();
    http
      .expectOne((request) => request.url === '/api/admin/conversations/c1/messages')
      .flush({
        items: [
          message('m1', '2026-11-18T10:00:00Z', { body: 'A fox, please', readByAdminAt: null }),
        ],
        nextCursor: null,
      });
    await settle();
    harness.detectChanges();
    return harness;
  }

  it('shows the customer, their messages, and marks them read for the studio', async () => {
    const harness = await open();
    const element = harness.routeNativeElement!;

    expect(element.querySelector('h1')?.textContent).toContain('Anna');
    expect(element.querySelector('.customer')?.textContent).toContain('anna@example.test');
    expect(element.querySelector('.customer')?.textContent).toContain('Polski');
    expect(element.querySelector('.message__author')?.textContent).toContain('Anna');
    expect(element.querySelector('.message__body')?.textContent).toContain('A fox, please');
    http.expectOne('/api/admin/conversations/c1/read').flush(null);
  });

  it('adds live messages of this conversation only', async () => {
    const harness = await open();
    http.expectOne('/api/admin/conversations/c1/read').flush(null);

    realtime.messages.next(
      message('x', '2026-11-18T10:01:00Z', { conversationId: 'other', body: 'Elsewhere' }),
    );
    realtime.messages.next(
      message('m2', '2026-11-18T10:02:00Z', {
        conversationId: 'c1',
        body: 'Orange, please',
        readByAdminAt: null,
      }),
    );
    http.expectOne('/api/admin/conversations/c1/read').flush(null);
    harness.detectChanges();

    const bodies = Array.from(harness.routeNativeElement!.querySelectorAll('.message__body')).map(
      (item) => item.textContent?.trim(),
    );
    expect(bodies).toEqual(['A fox, please', 'Orange, please']);
  });

  it('sends a reply to this conversation', async () => {
    const harness = await open();
    http.expectOne('/api/admin/conversations/c1/read').flush(null);
    const element = harness.routeNativeElement!;
    const textarea = element.querySelector<HTMLTextAreaElement>('#chat-text')!;
    textarea.value = 'Sure, in two weeks';
    textarea.dispatchEvent(new Event('input'));
    harness.detectChanges();

    element.querySelector<HTMLFormElement>('.composer')!.dispatchEvent(new Event('submit'));
    const sent = http.expectOne(
      (request) =>
        request.method === 'POST' && request.url === '/api/admin/conversations/c1/messages',
    );
    expect(sent.request.body).toEqual({ body: 'Sure, in two weeks', attachmentIds: [] });
    sent.flush(
      message('m3', '2026-11-18T10:03:00Z', {
        conversationId: 'c1',
        from: 'studio',
        body: 'Sure, in two weeks',
      }),
    );
    await settle();
    harness.detectChanges();

    expect(element.querySelector('.message--mine .message__body')?.textContent).toContain(
      'Sure, in two weeks',
    );
  });
});
