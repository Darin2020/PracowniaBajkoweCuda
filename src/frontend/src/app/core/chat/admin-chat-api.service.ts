import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChatMessage, ChatPage } from './chat.models';

export interface ChatCustomer {
  readonly id: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly preferredLanguage: string;
  readonly isDeleted: boolean;
}

export interface ConversationSummary {
  readonly id: string;
  readonly customer: ChatCustomer;
  readonly lastMessageAt: string;
  readonly lastMessagePreview: string | null;
  readonly lastMessageImageCount: number;
  readonly lastMessageKind: 'text' | 'system' | null;
  readonly unreadCount: number;
}

export interface ConversationPage {
  readonly items: readonly ConversationSummary[];
  readonly nextCursor: string | null;
}

export interface ConversationDetail {
  readonly id: string;
  readonly customer: ChatCustomer;
  readonly createdAt: string;
  readonly lastMessageAt: string;
  readonly customerEmailNotificationsEnabled: boolean;
  readonly unreadCount: number;
}

/** The studio's side of the chat (`/api/admin/conversations`, docs/PLAN.md §6). Needs an admin session with 2FA. */
@Injectable({ providedIn: 'root' })
export class AdminChatApi {
  private readonly http = inject(HttpClient);

  list(search?: string | null, cursor?: string | null): Promise<ConversationPage> {
    let params = new HttpParams();
    if (search?.trim()) {
      params = params.set('search', search.trim());
    }

    if (cursor) {
      params = params.set('cursor', cursor);
    }

    return firstValueFrom(this.http.get<ConversationPage>('/api/admin/conversations', { params }));
  }

  get(id: string): Promise<ConversationDetail> {
    return firstValueFrom(
      this.http.get<ConversationDetail>(`/api/admin/conversations/${encodeURIComponent(id)}`),
    );
  }

  messages(id: string, before?: string | null, take = 30): Promise<ChatPage> {
    let params = new HttpParams().set('take', take);
    if (before) {
      params = params.set('before', before);
    }

    return firstValueFrom(
      this.http.get<ChatPage>(`/api/admin/conversations/${encodeURIComponent(id)}/messages`, {
        params,
      }),
    );
  }

  send(id: string, body: string, attachmentIds: readonly string[]): Promise<ChatMessage> {
    return firstValueFrom(
      this.http.post<ChatMessage>(`/api/admin/conversations/${encodeURIComponent(id)}/messages`, {
        body,
        attachmentIds,
      }),
    );
  }

  async markRead(id: string, upToMessageId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/admin/conversations/${encodeURIComponent(id)}/read`, { upToMessageId }),
    );
  }
}
