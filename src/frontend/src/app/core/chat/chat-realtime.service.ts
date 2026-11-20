import { isPlatformBrowser } from '@angular/common';
import {
  DestroyRef,
  Injectable,
  InjectionToken,
  PLATFORM_ID,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { ADMIN_ROLE } from '../auth/auth.models';
import { AuthService } from '../auth/auth.service';
import { OrderSummary } from '../orders/order.models';
import { ChatMessage, ReadReceipt } from './chat.models';

export type RealtimeState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** The part of a SignalR `HubConnection` the app uses (so tests can provide a fake). */
export interface ChatConnection {
  on(event: string, handler: (...args: never[]) => void): void;
  onreconnecting(handler: () => void): void;
  onreconnected(handler: () => void): void;
  onclose(handler: () => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

/**
 * Creates the connection to `/hubs/chat`. The SignalR client is loaded only when someone signs in, so visitors of
 * the public pages never download it.
 */
export const CHAT_CONNECTION_FACTORY = new InjectionToken<() => Promise<ChatConnection>>(
  'CHAT_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => async () => {
      const signalR = await import('@microsoft/signalr');
      return new signalR.HubConnectionBuilder()
        .withUrl('/hubs/chat')
        .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();
    },
  },
);

/** After automatic reconnecting gives up, start again this much later (while still signed in). */
const RESTART_DELAY = 30_000;

/**
 * The real-time side of the chat (docs/PLAN.md §4.3): one connection for the signed-in user, its state as a signal,
 * and the server's events as streams. It connects when a user who may chat signs in and disconnects on sign-out.
 * After a reconnect, `reconnected` tells pages to reload what they may have missed.
 */
@Injectable({ providedIn: 'root' })
export class ChatRealtime {
  private readonly auth = inject(AuthService);
  private readonly createConnection = inject(CHAT_CONNECTION_FACTORY);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  private connection: ChatConnection | null = null;
  /** Who the connection is for (user, roles, 2FA); a change means reconnecting. */
  private userId: string | null = null;
  private restartTimer: ReturnType<typeof setTimeout> | undefined;

  private readonly messageEvents = new Subject<ChatMessage>();
  private readonly readEvents = new Subject<ReadReceipt>();
  private readonly reconnectEvents = new Subject<void>();
  private readonly orderEvents = new Subject<OrderSummary>();

  readonly state = signal<RealtimeState>('disconnected');
  readonly messages: Observable<ChatMessage> = this.messageEvents.asObservable();
  readonly reads: Observable<ReadReceipt> = this.readEvents.asObservable();
  readonly reconnected: Observable<void> = this.reconnectEvents.asObservable();
  /** An order was created or changed (admins: every order; customers: their own, once confirmed). */
  readonly orders: Observable<OrderSummary> = this.orderEvents.asObservable();

  constructor() {
    if (!this.browser) {
      return;
    }

    effect(() => {
      const user = this.auth.user();
      // Customers chat once their address is confirmed; admins hear every conversation (the hub checks 2FA).
      const allowed = !!user && (user.emailConfirmed || user.roles.includes(ADMIN_ROLE));
      // The server decides on connecting what the connection receives (an admin joins the admins' group only with
      // 2FA), so turning 2FA on, or a new role, needs a new connection.
      const key = allowed
        ? `${user.id}:${user.roles.join(',')}:${user.twoFactorEnabled ? 'mfa' : ''}`
        : null;
      if (key !== this.userId) {
        this.userId = key;
        void this.reconnectFor(key);
      }
    });

    inject(DestroyRef).onDestroy(() => void this.disconnect());
  }

  private async reconnectFor(userId: string | null): Promise<void> {
    await this.disconnect();
    if (userId) {
      await this.connect(userId);
    }
  }

  private async connect(userId: string): Promise<void> {
    this.state.set('connecting');
    const connection = await this.createConnection();
    if (this.userId !== userId) {
      return;
    }

    this.connection = connection;
    connection.on('MessageReceived', (message: ChatMessage) => this.messageEvents.next(message));
    connection.on('MessagesRead', (receipt: ReadReceipt) => this.readEvents.next(receipt));
    connection.on('UnreadCountChanged', (count: number) => this.auth.setUnreadCount(count));
    connection.on('OrderUpdated', (order: OrderSummary) => {
      if (order.status !== 'Draft' && !this.auth.isAdmin()) {
        this.auth.setHasOrders();
      }

      this.orderEvents.next(order);
    });
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => {
      this.state.set('connected');
      this.reconnectEvents.next();
    });
    connection.onclose(() => {
      if (this.connection === connection) {
        this.state.set('disconnected');
        this.scheduleRestart(userId);
      }
    });

    try {
      await connection.start();
      if (this.connection === connection) {
        this.state.set('connected');
      }
    } catch {
      if (this.connection === connection) {
        this.state.set('disconnected');
        this.scheduleRestart(userId);
      }
    }
  }

  private scheduleRestart(userId: string): void {
    clearTimeout(this.restartTimer);
    this.restartTimer = setTimeout(() => {
      if (this.userId === userId) {
        void this.reconnectFor(userId).then(() => this.reconnectEvents.next());
      }
    }, RESTART_DELAY);
  }

  private async disconnect(): Promise<void> {
    clearTimeout(this.restartTimer);
    const connection = this.connection;
    this.connection = null;
    this.state.set('disconnected');
    if (connection) {
      try {
        await connection.stop();
      } catch {
        // Already gone.
      }
    }
  }
}
