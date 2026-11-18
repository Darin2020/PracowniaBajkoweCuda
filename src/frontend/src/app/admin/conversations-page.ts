import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AdminChatApi, ConversationSummary } from '../core/chat/admin-chat-api.service';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { Seo } from '../core/seo';

/** Typing pauses this long (ms) before the search runs. */
const SEARCH_DELAY = 300;
/** Bursts of events (a message, its read receipt, the badge) refresh the list once. */
const REFRESH_DELAY = 400;

/**
 * `/admin/conversations` (docs/PLAN.md §3.4): every customer's conversation, unread first, then the newest, with a
 * preview of the last message; search by name or email. It refreshes itself when messages arrive or are read.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink],
  selector: 'app-conversations-page',
  styleUrl: './conversations-page.scss',
  template: `
    <section class="page page--wide container">
      <h1>{{ title }}</h1>

      <div class="field">
        <label for="conversation-search" i18n="@@admin.conversations.search"
          >Search by name or email</label
        >
        <input
          id="conversation-search"
          class="input"
          type="search"
          autocomplete="off"
          [value]="search()"
          (input)="onSearch($any($event.target).value)"
        />
      </div>

      @if (error(); as message) {
        <p class="alert alert--error" role="alert">{{ message }}</p>
      }

      @if (loading() && items().length === 0) {
        <p role="status" i18n="@@common.loading">Loading…</p>
      } @else if (items().length === 0 && !error()) {
        <p class="hint" i18n="@@admin.conversations.none">No conversations yet.</p>
      }

      <ul class="conversations">
        @for (conversation of items(); track conversation.id) {
          <li>
            <a
              class="conversation"
              [class.conversation--unread]="conversation.unreadCount > 0"
              [routerLink]="['/admin/conversations', conversation.id]"
            >
              <span class="conversation__top">
                <span class="conversation__name">{{ name(conversation) }}</span>
                <time class="conversation__time" [attr.datetime]="conversation.lastMessageAt">{{
                  conversation.lastMessageAt | date: 'short'
                }}</time>
              </span>
              <span class="conversation__preview">
                @if (conversation.lastMessageKind === 'system') {
                  <ng-container i18n="@@chat.orderUpdate">Update about your order</ng-container>
                } @else {
                  {{ conversation.lastMessagePreview }}
                  @if (conversation.lastMessageImageCount > 0) {
                    <span class="conversation__photos" i18n="@@admin.conversations.photos"
                      >Photos: {{ conversation.lastMessageImageCount }}</span
                    >
                  }
                }
              </span>
              @if (conversation.unreadCount > 0) {
                <span class="conversation__badge">
                  {{ conversation.unreadCount }}
                  <span class="visually-hidden" i18n="@@nav.unread">unread messages</span>
                </span>
              }
            </a>
          </li>
        }
      </ul>

      @if (cursor()) {
        <p>
          <button
            class="button"
            type="button"
            [disabled]="loading()"
            (click)="more()"
            i18n="@@admin.conversations.more"
          >
            Show more
          </button>
        </p>
      }
    </section>
  `,
})
export class ConversationsPage {
  private readonly api = inject(AdminChatApi);

  protected readonly title = $localize`:@@admin.conversations.title:Conversations`;
  private readonly deletedName = $localize`:@@admin.conversations.deleted:Deleted account`;

  protected readonly items = signal<ConversationSummary[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    void this.load();

    const realtime = inject(ChatRealtime);
    merge(realtime.messages, realtime.reads, realtime.reconnected)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        clearTimeout(this.refreshTimer);
        this.refreshTimer = setTimeout(() => void this.load(), REFRESH_DELAY);
      });

    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.searchTimer);
      clearTimeout(this.refreshTimer);
    });
  }

  protected name(conversation: ConversationSummary): string {
    return conversation.customer.isDeleted ? this.deletedName : conversation.customer.displayName;
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(), SEARCH_DELAY);
  }

  protected async more(): Promise<void> {
    await this.load(this.cursor());
  }

  /** The first page (or, with a cursor, the next one added below). */
  private async load(cursor: string | null = null): Promise<void> {
    this.loading.set(true);
    try {
      const page = await this.api.list(this.search(), cursor);
      this.items.update((items) => (cursor ? [...items, ...page.items] : [...page.items]));
      this.cursor.set(page.nextCursor);
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(toApiError(error)));
    } finally {
      this.loading.set(false);
    }
  }
}
