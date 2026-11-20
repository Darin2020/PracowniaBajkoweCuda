import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChatMessage } from '../core/chat/chat.models';
import { orderEvent, orderEventTitle } from '../core/orders/order.models';
import { ChatSide } from './chat-session';

/**
 * One message of a conversation, from the point of view of `side`: its own messages on the right with "Sent" /
 * "Read", the other side's on the left with their name. System messages (order steps) are a centred card in the
 * reader's language, with the payment instructions after a confirmation and a link to the order.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, RouterLink],
  selector: 'app-chat-message',
  styleUrl: './chat-message.scss',
  template: `
    @let item = message();
    @if (item.kind === 'system') {
      <article class="message message--system">
        @if (event(); as order) {
          <p class="message__event">{{ eventTitle() }}</p>
          @if (order.title) {
            <p class="message__order-title">{{ order.title }}</p>
          }
          @if (order.type === 'order.confirmed' && order.amount !== null) {
            <p>
              <ng-container i18n="@@order.event.amount">Amount to pay:</ng-container>&ngsp;
              <strong>{{ order.amount | currency: order.currency }}</strong>
            </p>
          }
          @if (order.type === 'order.confirmed' && order.paymentInstructions) {
            <p class="message__instructions">{{ order.paymentInstructions }}</p>
          }
          @if (order.type === 'order.cancelled' && order.reason) {
            <p class="message__instructions">{{ order.reason }}</p>
          }
          @if (order.orderId) {
            <a [routerLink]="orderLink(order.orderId)" i18n="@@order.event.open">View the order</a>
          }
        } @else {
          <p class="message__event" i18n="@@chat.orderUpdate">Update about your order</p>
        }
        <time class="message__meta" [attr.datetime]="item.createdAt">{{
          item.createdAt | date: 'shortTime'
        }}</time>
      </article>
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
  protected readonly event = computed(() => orderEvent(this.message()));
  protected readonly eventTitle = computed(() => {
    const event = this.event();
    return event ? orderEventTitle(event) : '';
  });

  protected orderLink(orderId: string): string[] {
    return this.side() === 'studio' ? ['/admin/orders', orderId] : ['/orders', orderId];
  }

  protected readonly readByOtherSide = computed(() =>
    this.side() === 'customer' ? this.message().readByAdminAt : this.message().readByCustomerAt,
  );
}
