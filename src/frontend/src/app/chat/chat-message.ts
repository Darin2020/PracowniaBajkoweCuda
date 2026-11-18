import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ChatMessage } from '../core/chat/chat.models';
import { ChatSide } from './chat-session';

/**
 * One message of a conversation, from the point of view of `side`: its own messages on the right with "Sent" /
 * "Read", the other side's on the left with their name. System messages are a small centred note.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  selector: 'app-chat-message',
  styleUrl: './chat-message.scss',
  template: `
    @let item = message();
    @if (item.kind === 'system') {
      <p class="message message--system">
        <span i18n="@@chat.orderUpdate">Update about your order</span>
        <time [attr.datetime]="item.createdAt">{{ item.createdAt | date: 'shortTime' }}</time>
      </p>
    } @else {
      <article class="message" [class.message--mine]="mine()">
        @if (!mine()) {
          <p class="message__author">{{ otherName() }}</p>
        }
        @if (item.images.length > 0) {
          <div class="message__images" [class.message__images--single]="item.images.length === 1">
            @for (image of item.images; track image.id; let position = $index) {
              <button type="button" class="message__image" (click)="openImage.emit(position)">
                <img
                  [src]="image.thumbnailUrl"
                  [width]="image.width"
                  [height]="image.height"
                  loading="lazy"
                  alt="Photo {{ position + 1 }} of {{ item.images.length }}"
                  i18n-alt="@@chat.photoAlt"
                />
              </button>
            }
          </div>
        }
        @if (item.body) {
          <p class="message__body">{{ item.body }}</p>
        }
        <p class="message__meta">
          <time [attr.datetime]="item.createdAt">{{ item.createdAt | date: 'shortTime' }}</time>
          @if (mine()) {
            @if (readByOtherSide()) {
              <span i18n="@@chat.read">Read</span>
            } @else {
              <span i18n="@@chat.sent">Sent</span>
            }
          }
        </p>
      </article>
    }
  `,
})
export class ChatMessageView {
  readonly message = input.required<ChatMessage>();
  readonly side = input.required<ChatSide>();
  /** How the other side is named above their messages. */
  readonly otherName = input.required<string>();

  readonly openImage = output<number>();

  protected readonly mine = computed(() => this.message().from === this.side());
  protected readonly readByOtherSide = computed(() =>
    this.side() === 'customer' ? this.message().readByAdminAt : this.message().readByCustomerAt,
  );
}
