import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ResendConfirmation } from '../auth/resend-confirmation';
import { errorMessage, toApiError } from '../core/api/api-error';
import { AuthService } from '../core/auth/auth.service';
import { ChatApi } from '../core/chat/chat-api.service';
import { CustomerChat } from '../core/chat/chat.models';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { Seo } from '../core/seo';
import { ChatComposer, SendMessage } from './chat-composer';
import { ChatSession } from './chat-session';
import { ChatThread } from './chat-thread';

/**
 * The customer's chat with the studio (docs/PLAN.md §3.4). History loads newest first and older pages load when the
 * top is reached; new messages arrive through SignalR. After the first message a dialog asks whether replies should
 * also come by email; later the composer has a checkbox for it (§3.6).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatComposer, ChatThread, ResendConfirmation, RouterLink],
  selector: 'app-chat-page',
  styleUrl: './chat-page.scss',
  templateUrl: './chat-page.html',
})
export class ChatPage {
  protected readonly auth = inject(AuthService);
  protected readonly realtime = inject(ChatRealtime);
  private readonly api = inject(ChatApi);
  private readonly document = inject(DOCUMENT);

  protected readonly title = $localize`:@@chat.title:Chat`;
  protected readonly studioName = $localize`:@@chat.studio:Pracownia Bajkowe Cuda`;

  protected readonly chat = signal<CustomerChat | null>(null);
  protected readonly session = new ChatSession(
    { page: (before) => this.api.messages(before), markRead: (id) => this.api.markRead(id) },
    'customer',
  );
  protected readonly settingError = signal<string | null>(null);

  private readonly thread = viewChild(ChatThread);
  private readonly emailDialog = viewChild<ElementRef<HTMLDialogElement>>('emailDialog');

  constructor() {
    inject(Seo).setNoIndexPage(this.title);
    const destroy = inject(DestroyRef);

    afterNextRender(() => {
      if (!this.auth.user()?.emailConfirmed) {
        return;
      }

      void this.load();
      const onVisible = () => this.markRead();
      this.document.addEventListener('visibilitychange', onVisible);
      destroy.onDestroy(() => this.document.removeEventListener('visibilitychange', onVisible));
    });

    this.realtime.messages.pipe(takeUntilDestroyed()).subscribe((message) => {
      const conversationId = this.chat()?.conversationId;
      // A customer only receives their own conversation's events; before the first message it has no id yet.
      if (conversationId && message.conversationId !== conversationId) {
        return;
      }

      const follow = this.thread()?.isAtBottom() ?? true;
      this.session.add([message]);
      if (follow || message.from === 'customer') {
        this.thread()?.scrollToBottom();
      }

      this.markRead();
    });

    this.realtime.reads
      .pipe(takeUntilDestroyed())
      .subscribe((receipt) => this.session.applyReceipt(receipt));

    // After a lost connection, fetch what may have arrived meanwhile.
    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => {
      void this.session.loadNewest().then(() => this.markRead());
    });
  }

  protected readonly send: SendMessage = async (body, attachmentIds) => {
    const firstMessage = !this.chat()?.conversationId;
    const message = await this.api.send(body, attachmentIds);
    this.session.add([message]);
    this.thread()?.scrollToBottom();
    if (firstMessage) {
      this.chat.set(await this.api.get());
    }

    if (!this.chat()?.emailPromptAnswered) {
      this.emailDialog()?.nativeElement.showModal();
    }
  };

  /** The answer of the first-message dialog, and the composer's checkbox. */
  protected async setEmailNotifications(enabled: boolean): Promise<void> {
    this.emailDialog()?.nativeElement.close();
    this.settingError.set(null);
    try {
      this.chat.set(await this.api.setEmailNotifications(enabled));
    } catch (error) {
      this.settingError.set(errorMessage(toApiError(error)));
    }
  }

  private async load(): Promise<void> {
    try {
      this.chat.set(await this.api.get());
    } catch (error) {
      this.session.error.set(errorMessage(toApiError(error)));
    }

    await this.session.loadNewest();
    this.thread()?.scrollToBottom();
    this.markRead();
  }

  /** Only while the page is visible: a message in a background tab is not "read". */
  private markRead(): void {
    if (this.document.visibilityState === 'visible') {
      this.session.markRead();
    }
  }
}
