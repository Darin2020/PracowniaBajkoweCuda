import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { ChatMessage } from '../core/chat/chat.models';
import { FakeRealtime, message } from '../testing/chat-testing';
import { ANNA, provideApiTesting } from '../testing/auth-testing';
import { ChatPage } from './chat-page';

describe('ChatPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(async () => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([]),
        { provide: ChatRealtime, useValue: realtime },
        // Dates need locale data, which the localized builds bring; tests run with the default locale.
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    // jsdom has no modal dialogs.
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.open = true;
    };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) {
      this.open = false;
    };

    const loading = TestBed.inject(AuthService).ensureLoaded();
    http.expectOne('/api/auth/me').flush({ authenticated: true, ...ANNA });
    await loading;
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function open(chat: object, items: ChatMessage[], nextCursor: string | null = null) {
    const fixture = TestBed.createComponent(ChatPage);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/chat').flush(chat);
    await settle();
    http.expectOne((request) => request.url === '/api/chat/messages').flush({ items, nextCursor });
    await settle();
    fixture.detectChanges();
    return fixture;
  }

  const element = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
  const bodies = (root: HTMLElement) =>
    Array.from(root.querySelectorAll('.message__body')).map((item) => item.textContent?.trim());

  it('shows the history oldest first and marks the studio replies read', async () => {
    const fixture = await open(
      {
        conversationId: 'c1',
        emailNotificationsEnabled: false,
        emailPromptAnswered: true,
        unreadCount: 1,
      },
      [
        message('m2', '2026-11-18T10:05:00Z', {
          from: 'studio',
          body: 'What size?',
          readByCustomerAt: null,
        }),
        message('m1', '2026-11-18T10:00:00Z', { body: 'A fox, please' }),
      ],
    );

    expect(bodies(element(fixture))).toEqual(['A fox, please', 'What size?']);
    const read = http.expectOne('/api/chat/read');
    expect(read.request.body).toEqual({ upToMessageId: 'm2' });
    read.flush(null);
  });

  it('invites to describe the toy before the first message', async () => {
    const fixture = await open(
      {
        conversationId: null,
        emailNotificationsEnabled: false,
        emailPromptAnswered: false,
        unreadCount: 0,
      },
      [],
    );

    expect(element(fixture).querySelector('.chat__empty h2')?.textContent).toContain(
      'Tell us about your toy',
    );
    expect(element(fixture).querySelector('.composer__email')).toBeNull();
  });

  it('asks about email replies after the first message', async () => {
    const fixture = await open(
      {
        conversationId: null,
        emailNotificationsEnabled: false,
        emailPromptAnswered: false,
        unreadCount: 0,
      },
      [],
    );
    const textarea = element(fixture).querySelector<HTMLTextAreaElement>('#chat-text')!;
    textarea.value = 'Hello!';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    element(fixture)
      .querySelector<HTMLFormElement>('.composer')!
      .dispatchEvent(new Event('submit'));
    const sent: TestRequest = http.expectOne('/api/chat/messages');
    expect(sent.request.body).toEqual({ body: 'Hello!', attachmentIds: [] });
    sent.flush(message('m1', '2026-11-18T10:00:00Z', { body: 'Hello!' }));
    await settle();
    http.expectOne('/api/chat').flush({
      conversationId: 'c1',
      emailNotificationsEnabled: false,
      emailPromptAnswered: false,
      unreadCount: 0,
    });
    await settle();
    fixture.detectChanges();

    const dialog = element(fixture).querySelector<HTMLDialogElement>('.email-dialog')!;
    expect(dialog.open).toBe(true);
    expect(bodies(element(fixture))).toEqual(['Hello!']);

    dialog.querySelector<HTMLButtonElement>('.button--primary')!.click();
    const setting = http.expectOne('/api/chat/email-notifications');
    expect(setting.request.body).toEqual({ enabled: true });
    setting.flush({
      conversationId: 'c1',
      emailNotificationsEnabled: true,
      emailPromptAnswered: true,
      unreadCount: 0,
    });
    await settle();
    fixture.detectChanges();

    expect(dialog.open).toBe(false);
    expect(
      element(fixture).querySelector<HTMLInputElement>('.composer__email input')!.checked,
    ).toBe(true);
  });

  it('adds messages that arrive in real time, once, and shows when the studio has read', async () => {
    const fixture = await open(
      {
        conversationId: 'c1',
        emailNotificationsEnabled: false,
        emailPromptAnswered: true,
        unreadCount: 0,
      },
      [message('m1', '2026-11-18T10:00:00Z', { body: 'Mine' })],
    );

    const reply = message('m2', '2026-11-18T10:01:00Z', {
      from: 'studio',
      body: 'Reply',
      readByCustomerAt: null,
    });
    realtime.messages.next(reply);
    realtime.messages.next(reply);
    realtime.messages.next(
      message('x', '2026-11-18T10:02:00Z', { conversationId: 'other', body: 'Not mine' }),
    );
    http.expectOne('/api/chat/read').flush(null);
    realtime.reads.next({
      conversationId: 'c1',
      customerId: ANNA.id,
      upToMessageId: 'm1',
      by: 'studio',
      readAt: '2026-11-18T10:03:00Z',
      marked: 1,
    });
    fixture.detectChanges();

    expect(bodies(element(fixture))).toEqual(['Mine', 'Reply']);
    expect(element(fixture).querySelector('.message--mine .message__meta')?.textContent).toContain(
      'Read',
    );
  });

  it('keeps the text and shows the error when sending fails', async () => {
    const fixture = await open(
      {
        conversationId: 'c1',
        emailNotificationsEnabled: false,
        emailPromptAnswered: true,
        unreadCount: 0,
      },
      [],
    );
    const textarea = element(fixture).querySelector<HTMLTextAreaElement>('#chat-text')!;
    textarea.value = 'Too fast';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    element(fixture)
      .querySelector<HTMLFormElement>('.composer')!
      .dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/chat/messages')
      .flush({ code: 'rate_limited' }, { status: 429, statusText: 'Too Many Requests' });
    await settle();
    fixture.detectChanges();

    expect(element(fixture).querySelector('.composer [role=alert]')?.textContent).toContain(
      'Too many attempts',
    );
    expect(textarea.value).toBe('Too fast');
  });
});
