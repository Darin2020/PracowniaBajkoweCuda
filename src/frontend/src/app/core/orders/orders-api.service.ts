import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AdminOrder,
  CreateOrder,
  CustomerOrder,
  OrderPage,
  OrderStatus,
  OrderStepAction,
  OrderSummary,
  UpdateOrder,
} from './order.models';

/** The customer's orders (`/api/orders`): only their own, never drafts. */
@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  list(): Promise<readonly OrderSummary[]> {
    return firstValueFrom(this.http.get<readonly OrderSummary[]>('/api/orders'));
  }

  get(id: string): Promise<CustomerOrder> {
    return firstValueFrom(this.http.get<CustomerOrder>(`/api/orders/${encodeURIComponent(id)}`));
  }
}

export interface OrderFilter {
  readonly status?: OrderStatus | null;
  readonly conversationId?: string | null;
  readonly cursor?: string | null;
}

/** The studio's orders (`/api/admin/orders`, Admin policy). */
@Injectable({ providedIn: 'root' })
export class AdminOrdersApi {
  private readonly http = inject(HttpClient);

  list(filter: OrderFilter = {}): Promise<OrderPage> {
    let params = new HttpParams();
    if (filter.status) {
      params = params.set('status', filter.status);
    }

    if (filter.conversationId) {
      params = params.set('conversationId', filter.conversationId);
    }

    if (filter.cursor) {
      params = params.set('cursor', filter.cursor);
    }

    return firstValueFrom(this.http.get<OrderPage>('/api/admin/orders', { params }));
  }

  get(id: string): Promise<AdminOrder> {
    return firstValueFrom(this.http.get<AdminOrder>(this.url(id)));
  }

  create(order: CreateOrder): Promise<AdminOrder> {
    return firstValueFrom(this.http.post<AdminOrder>('/api/admin/orders', order));
  }

  update(id: string, changes: UpdateOrder): Promise<AdminOrder> {
    return firstValueFrom(this.http.put<AdminOrder>(this.url(id), changes));
  }

  step(
    id: string,
    step: OrderStepAction,
    rowVersion: string,
    reason?: string | null,
  ): Promise<AdminOrder> {
    return firstValueFrom(
      this.http.post<AdminOrder>(`${this.url(id)}/${step}`, { rowVersion, reason: reason ?? null }),
    );
  }

  addPhoto(id: string, file: File): Promise<AdminOrder> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<AdminOrder>(`${this.url(id)}/photos`, form));
  }

  removePhoto(id: string, photoId: string): Promise<AdminOrder> {
    return firstValueFrom(
      this.http.delete<AdminOrder>(`${this.url(id)}/photos/${encodeURIComponent(photoId)}`),
    );
  }

  reorderPhotos(id: string, photoIds: readonly string[]): Promise<AdminOrder> {
    return firstValueFrom(this.http.put<AdminOrder>(`${this.url(id)}/photos/order`, { photoIds }));
  }

  private url(id: string): string {
    return `/api/admin/orders/${encodeURIComponent(id)}`;
  }
}
