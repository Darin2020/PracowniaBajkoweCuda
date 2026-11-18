import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChatMessage, ChatPage, CustomerChat, UploadedImage } from './chat.models';

/** The customer's chat (`/api/chat`, docs/PLAN.md §6). Always their own conversation: no ids to pass. */
@Injectable({ providedIn: 'root' })
export class ChatApi {
  private readonly http = inject(HttpClient);

  get(): Promise<CustomerChat> {
    return firstValueFrom(this.http.get<CustomerChat>('/api/chat'));
  }

  /** Newest first; `before` is the `nextCursor` of the previous page. */
  messages(before?: string | null, take = 30): Promise<ChatPage> {
    let params = new HttpParams().set('take', take);
    if (before) {
      params = params.set('before', before);
    }

    return firstValueFrom(this.http.get<ChatPage>('/api/chat/messages', { params }));
  }

  send(body: string, attachmentIds: readonly string[]): Promise<ChatMessage> {
    return firstValueFrom(
      this.http.post<ChatMessage>('/api/chat/messages', { body, attachmentIds }),
    );
  }

  async markRead(upToMessageId: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/chat/read', { upToMessageId }));
  }

  setEmailNotifications(enabled: boolean): Promise<CustomerChat> {
    return firstValueFrom(
      this.http.put<CustomerChat>('/api/chat/email-notifications', { enabled }),
    );
  }

  upload(file: File): Promise<UploadedImage> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<UploadedImage>('/api/chat/attachments', form));
  }
}
