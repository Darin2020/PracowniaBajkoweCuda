import { DatePipe, DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ChatComposer, SendMessage } from '../chat/chat-composer';
import { ChatSession } from '../chat/chat-session';
import { ChatThread } from '../chat/chat-thread';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AdminChatApi, ConversationDetail } from '../core/chat/admin-chat-api.service';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { LOCALE_NAMES, SUPPORTED_LOCALES } from '../core/locales';
import { Seo } from '../core/seo';

/**
 * `/admin/conversations/:id` (docs/PLAN.md §3.4): one customer's conversation from the studio's side, with the
 * customer beside it (on a phone, above it, folded). Orders and "Create order" join the side panel in Phase 5.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatComposer, ChatThread, DatePipe, RouterLink],
  selector: 'app-conversation-page',
  styleUrl: './conversation-page.scss',
  template: `
    <section class="conversation container">
      <header class="conversation__header">
        <a routerLink="/admin/conversations" i18n="@@admin.conversation.back"
          >← All conversations</a
        >
        <h1>{{ customerName() }}</h1>
        @if (realtime.state() === 'reconnecting' || realtime.state() === 'disconnected') {
          <p class="conversation__status" role="status" i18n="@@chat.reconnecting">Reconnecting…</p>
        }
      </header>

      @if (detailError(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      <div class="conversation__body">
        <div class="conversation__chat">
          <app-chat-thread
            [session]="session"
            side="studio"
            [otherName]="customerName()"
            [label]="customerName()"
          >
            <p class="hint" i18n="@@admin.conversation.empty">No messages yet.</p>
          </app-chat-thread>
          @if (detail()?.customer?.isDeleted) {
            <p class="hint" i18n="@@admin.conversation.deletedHint">
              This account has been deleted; replies cannot reach it.
            </p>
          } @else {
            <app-chat-composer [send]="send" />
          }
        </div>

        @if (detail(); as conversation) {
          <details class="customer" [open]="wide">
            <summary i18n="@@admin.conversation.customer">Customer</summary>
            <dl>
              <dt i18n="@@admin.conversation.name">Name</dt>
              <dd>{{ customerName() }}</dd>
              @if (conversation.customer.email; as email) {
                <dt i18n="@@auth.email">Email address</dt>
                <dd>
                  <a [href]="'mailto:' + email">{{ email }}</a>
                </dd>
              }
              <dt i18n="@@account.language">Language of emails</dt>
              <dd>{{ language(conversation.customer.preferredLanguage) }}</dd>
              <dt i18n="@@admin.conversation.since">Writing since</dt>
              <dd>{{ conversation.createdAt | date: 'longDate' }}</dd>
              <dt i18n="@@admin.conversation.emails">Email notifications</dt>
              <dd>
                @if (conversation.customerEmailNotificationsEnabled) {
                  <ng-container i18n="@@admin.conversation.emailsOn">On</ng-container>
                } @else {
                  <ng-container i18n="@@admin.conversation.emailsOff">Off</ng-container>
                }
              </dd>
            </dl>
          </details>
        }
      </div>
    </section>
  `,
})
export class ConversationPage {
  protected readonly realtime = inject(ChatRealtime);
  private readonly api = inject(AdminChatApi);
  private readonly document = inject(DOCUMENT);

  /** The route parameter. */
  readonly id = input.required<string>();

  protected readonly detail = signal<ConversationDetail | null>(null);
  protected readonly detailError = signal<string | null>(null);
  protected readonly session = new ChatSession(
    {
      page: (before) => this.api.messages(this.id(), before),
      markRead: (upTo) => this.api.markRead(this.id(), upTo),
    },
    'studio',
  );
  /** On wide screens the customer panel starts open. */
  protected readonly wide =
    typeof matchMedia === 'function' && matchMedia('(min-width: 60rem)').matches;

  private readonly thread = viewChild(ChatThread);
  private readonly fallbackName = $localize`:@@admin.conversation.customer:Customer`;
  private readonly deletedName = $localize`:@@admin.conversations.deleted:Deleted account`;
  private readonly seo = inject(Seo);

  constructor() {
    this.seo.setNoIndexPage($localize`:@@admin.conversations.title:Conversations`);
    const destroy = inject(DestroyRef);

    afterNextRender(() => {
      void this.load();
      const onVisible = () => this.markRead();
      this.document.addEventListener('visibilitychange', onVisible);
      destroy.onDestroy(() => this.document.removeEventListener('visibilitychange', onVisible));
    });

    this.realtime.messages.pipe(takeUntilDestroyed()).subscribe((message) => {
      if (message.conversationId !== this.id()) {
        return;
      }

      const follow = this.thread()?.isAtBottom() ?? true;
      this.session.add([message]);
      if (follow || message.from === 'studio') {
        this.thread()?.scrollToBottom();
      }

      this.markRead();
    });

    this.realtime.reads.pipe(takeUntilDestroyed()).subscribe((receipt) => {
      if (receipt.conversationId === this.id()) {
        this.session.applyReceipt(receipt);
      }
    });

    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => {
      void this.session.loadNewest().then(() => this.markRead());
    });
  }

  protected customerName(): string {
    const customer = this.detail()?.customer;
    if (!customer) {
      return this.fallbackName;
    }

    return customer.isDeleted ? this.deletedName : customer.displayName || this.fallbackName;
  }

  protected language(code: string): string {
    return SUPPORTED_LOCALES.find((locale) => locale === code)
      ? LOCALE_NAMES[code as keyof typeof LOCALE_NAMES]
      : code;
  }

  protected readonly send: SendMessage = async (body, attachmentIds) => {
    const message = await this.api.send(this.id(), body, attachmentIds);
    this.session.add([message]);
    this.thread()?.scrollToBottom();
  };

  private async load(): Promise<void> {
    try {
      this.detail.set(await this.api.get(this.id()));
      this.seo.setNoIndexPage(this.customerName());
    } catch (error) {
      this.detailError.set(errorMessage(toApiError(error)));
    }

    await this.session.loadNewest();
    this.thread()?.scrollToBottom();
    this.markRead();
  }

  /** Only while the page is visible: the customer sees "Read" when an admin has really looked. */
  private markRead(): void {
    if (this.document.visibilityState === 'visible') {
      this.session.markRead();
    }
  }
}
