import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ANNA, provideApiTesting } from '../../testing/auth-testing';
import { AuthService } from '../auth/auth.service';
import { CHAT_CONNECTION_FACTORY, ChatConnection, ChatRealtime } from './chat-realtime.service';
import { ChatMessage } from './chat.models';
import { message } from '../../testing/chat-testing';

/** Plays the server's side of a SignalR connection. */
class FakeConnection implements ChatConnection {
  readonly handlers = new Map<string, (...args: never[]) => void>();
  started = 0;
  stopped = 0;
  reconnecting?: () => void;
  reconnected?: () => void;
  closed?: () => void;

  on(event: string, handler: (...args: never[]) => void): void {
    this.handlers.set(event, handler);
  }

  onreconnecting(handler: () => void): void {
    this.reconnecting = handler;
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  onclose(handler: () => void): void {
    this.closed = handler;
  }

  start(): Promise<void> {
    this.started++;
    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.stopped++;
    return Promise.resolve();
  }

  emit(event: string, ...args: unknown[]): void {
    (this.handlers.get(event) as (...values: unknown[]) => void)(...args);
  }
}

describe('ChatRealtime', () => {
  let connections: FakeConnection[];
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    connections = [];
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([]),
        {
          provide: CHAT_CONNECTION_FACTORY,
          useValue: () => {
            const connection = new FakeConnection();
            connections.push(connection);
            return Promise.resolve(connection);
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  const settle = async () => {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
  };

  async function signIn(me: object) {
    const realtime = TestBed.inject(ChatRealtime);
    const loading = auth.ensureLoaded();
    http.expectOne('/api/auth/me').flush(me);
    await loading;
    await settle();
    return realtime;
  }

  it('connects when a confirmed customer is signed in and passes the events on', async () => {
    const realtime = await signIn({ authenticated: true, ...ANNA });
    const received: ChatMessage[] = [];
    realtime.messages.subscribe((item) => received.push(item));

    expect(connections).toHaveLength(1);
    expect(connections[0].started).toBe(1);
    expect(realtime.state()).toBe('connected');

    connections[0].emit('MessageReceived', message('m1', '2026-11-18T10:00:00Z'));
    connections[0].emit('UnreadCountChanged', 3);

    expect(received.map((item) => item.id)).toEqual(['m1']);
    expect(auth.user()?.unreadCount).toBe(3);
  });

  it('does not connect for visitors or unconfirmed accounts', async () => {
    await signIn({ authenticated: true, ...ANNA, emailConfirmed: false });

    expect(connections).toHaveLength(0);
  });

  it('reports reconnecting and tells pages to reload after a reconnect', async () => {
    const realtime = await signIn({ authenticated: true, ...ANNA });
    let reloads = 0;
    realtime.reconnected.subscribe(() => reloads++);

    connections[0].reconnecting!();
    expect(realtime.state()).toBe('reconnecting');

    connections[0].reconnected!();
    expect(realtime.state()).toBe('connected');
    expect(reloads).toBe(1);
  });

  it('reconnects when an admin turns 2FA on, so the server adds them to the admins', async () => {
    await signIn({ authenticated: true, ...ANNA, roles: ['Admin'] });
    expect(connections).toHaveLength(1);

    const enabling = auth.refresh();
    http
      .expectOne('/api/auth/me')
      .flush({ authenticated: true, ...ANNA, roles: ['Admin'], twoFactorEnabled: true });
    await enabling;
    await settle();

    expect(connections[0].stopped).toBe(1);
    expect(connections).toHaveLength(2);
  });

  it('keeps the connection when only the unread count changes', async () => {
    await signIn({ authenticated: true, ...ANNA });

    auth.setUnreadCount(5);
    await settle();

    expect(connections).toHaveLength(1);
  });

  it('disconnects on sign-out', async () => {
    const realtime = await signIn({ authenticated: true, ...ANNA });

    auth.clear();
    await settle();

    expect(connections[0].stopped).toBe(1);
    expect(realtime.state()).toBe('disconnected');
  });
});
