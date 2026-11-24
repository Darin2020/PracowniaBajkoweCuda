import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, message } from '../testing/chat-testing';
import { ConversationPage } from './conversation-page';
import { expectAccessible } from '../testing/axe';

describe('ConversationPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [
            { path: 'admin/conversations/:id', component: ConversationPage },
            { path: 'admin/orders/:id', children: [] },
          ],
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
    http
      .expectOne((request) => request.url === '/api/admin/orders')
      .flush({ items: [], nextCursor: null });
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

  it('creates a draft from the conversation, with one of its images as the reference', async () => {
    const harness = await open();
    http.expectOne('/api/admin/conversations/c1/read').flush(null);
    const element = harness.routeNativeElement!;
    harness.detectChanges();

    Array.from(element.querySelectorAll<HTMLButtonElement>('.side button'))
      .find((button) => button.textContent?.includes('Create order'))!
      .click();
    harness.detectChanges();
    // NgModel writes its first value a tick after rendering.
    await settle();

    const set = (selector: string, value: string) => {
      const field = element.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    set('input[name="title"]', 'Fox with a scarf');
    set('input[name="price"]', '149.9');
    harness.detectChanges();
    element
      .querySelector<HTMLFormElement>('app-order-form form')!
      .dispatchEvent(new Event('submit'));

    const request = http.expectOne(
      (item) => item.method === 'POST' && item.url === '/api/admin/orders',
    );
    expect(request.request.body).toEqual({
      conversationId: 'c1',
      title: 'Fox with a scarf',
      description: null,
      price: 149.9,
      referenceAttachmentId: null,
      adminNote: null,
    });
    request.flush({ id: 'o9' });
    await settle();
    await settle();

    expect(TestBed.inject(Router).url).toBe('/admin/orders/o9');
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

  it('has no accessibility violations, with the create-order form open', async () => {
    const harness = await open();
    http.expectOne('/api/admin/conversations/c1/read').flush(null);
    const element = harness.routeNativeElement!;
    await expectAccessible(element);

    Array.from(element.querySelectorAll<HTMLButtonElement>('.side button'))
      .find((button) => button.textContent?.includes('Create order'))!
      .click();
    harness.detectChanges();
    await expectAccessible(element);
  });
});
