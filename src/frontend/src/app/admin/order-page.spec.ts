import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { AdminOrder } from '../core/orders/order.models';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, adminOrder, image, message, orderSummary } from '../testing/chat-testing';
import { AdminOrderPage } from './order-page';

describe('AdminOrderPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [{ path: 'admin/orders/:id', component: AdminOrderPage }],
          withComponentInputBinding(),
        ),
        { provide: ChatRealtime, useValue: realtime },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function open(order: AdminOrder = adminOrder('o1')) {
    const harness = await RouterTestingHarness.create('/admin/orders/o1');
    http.expectOne('/api/admin/orders/o1').flush(order);
    await settle();
    harness.detectChanges();
    return harness;
  }

  const button = (element: HTMLElement, text: string) =>
    Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((item) =>
      item.textContent?.includes(text),
    )!;

  it('confirms a draft with the version the admin saw', async () => {
    const harness = await open();
    const element = harness.routeNativeElement!;
    expect(element.textContent).toContain('the customer does not see it yet');
    expect(element.textContent).toContain('Use the soft felt');

    button(element, 'Confirm and send to the customer').click();
    const request = http.expectOne('/api/admin/orders/o1/confirm');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ rowVersion: 'v1', reason: null });
    request.flush(
      adminOrder('o1', {
        status: 'AwaitingPayment',
        rowVersion: 'v2',
        timeline: [{ status: 'AwaitingPayment', at: '2026-11-18T11:00:00Z', comment: null }],
      }),
    );
    await settle();
    harness.detectChanges();

    expect(element.textContent).toContain('Payment received');
  });

  it('needs a photo before "ready", and orders the photos', async () => {
    const harness = await open(adminOrder('o1', { status: 'InProgress' }));
    const element = harness.routeNativeElement!;
    expect(button(element, 'Mark as ready').disabled).toBe(true);
    expect(element.textContent).toContain('Add at least one photo');

    http.expectNone('/api/admin/orders/o1/mark-ready');
    harness.detectChanges();

    // With two photos the second can move first.
    const withPhotos = adminOrder('o1', {
      status: 'InProgress',
      photos: [
        { id: 'a', image: image('pa') },
        { id: 'b', image: image('pb') },
      ],
    });
    realtime.orders.next(orderSummary('o1', { status: 'InProgress' }));
    http.expectOne('/api/admin/orders/o1').flush(withPhotos);
    await settle();
    harness.detectChanges();

    expect(button(element, 'Mark as ready').disabled).toBe(false);
    element.querySelectorAll<HTMLButtonElement>('.photo')[1].querySelector('button')!.click();
    const reorder = http.expectOne('/api/admin/orders/o1/photos/order');
    expect(reorder.request.body).toEqual({ photoIds: ['b', 'a'] });
    reorder.flush(withPhotos);
    await settle();
  });

  it('asks for a reason before cancelling', async () => {
    const harness = await open(adminOrder('o1', { status: 'AwaitingPayment' }));
    const element = harness.routeNativeElement!;

    button(element, 'Cancel the order').click();
    harness.detectChanges();
    await settle();
    const reason = element.querySelector<HTMLTextAreaElement>('#cancel-reason')!;
    reason.value = 'The customer changed their mind';
    reason.dispatchEvent(new Event('input'));
    harness.detectChanges();
    element.querySelector<HTMLFormElement>('.steps form')!.dispatchEvent(new Event('submit'));

    const request = http.expectOne('/api/admin/orders/o1/cancel');
    expect(request.request.body).toEqual({
      rowVersion: 'v1',
      reason: 'The customer changed their mind',
    });
    request.flush(
      adminOrder('o1', {
        status: 'Cancelled',
        cancellationReason: 'The customer changed their mind',
      }),
    );
    await settle();
    harness.detectChanges();

    expect(element.querySelector('.alert--error')?.textContent).toContain('changed their mind');
    expect(element.querySelector('.steps')).toBeNull();
  });

  it('reloads the order after someone else changed it', async () => {
    const harness = await open();
    const element = harness.routeNativeElement!;

    button(element, 'Confirm and send to the customer').click();
    http
      .expectOne('/api/admin/orders/o1/confirm')
      .flush({ code: 'orders.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http
      .expectOne('/api/admin/orders/o1')
      .flush(adminOrder('o1', { status: 'AwaitingPayment', rowVersion: 'v2' }));
    await settle();
    harness.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Someone else');
    expect(element.textContent).toContain('Payment received');
  });

  it('edits the terms, offering the images of the conversation', async () => {
    const harness = await open();
    const element = harness.routeNativeElement!;

    button(element, 'Edit').click();
    http
      .expectOne((request) => request.url === '/api/admin/conversations/c1/messages')
      .flush({
        items: [message('m1', '2026-11-18T09:00:00Z', { images: [image('i1')] })],
        nextCursor: null,
      });
    await settle();
    harness.detectChanges();

    const choices = element.querySelectorAll<HTMLInputElement>('.order-form__choice input');
    expect(choices).toHaveLength(2);
    choices[1].click();
    const price = element.querySelector<HTMLInputElement>('input[name="price"]')!;
    price.value = '120.5';
    price.dispatchEvent(new Event('input'));
    harness.detectChanges();
    element
      .querySelector<HTMLFormElement>('app-order-form form')!
      .dispatchEvent(new Event('submit'));

    const request = http.expectOne(
      (item) => item.method === 'PUT' && item.url === '/api/admin/orders/o1',
    );
    expect(request.request.body).toEqual({
      title: 'Fox with a scarf',
      description: 'Orange fox, 30 cm',
      price: 120.5,
      referenceAttachmentId: 'i1',
      adminNote: 'Use the soft felt',
      rowVersion: 'v1',
    });
    request.flush(adminOrder('o1', { price: { amount: 120.5, currency: 'EUR' } }));
    await settle();
    harness.detectChanges();

    expect(element.querySelector('app-order-form')).toBeNull();
    expect(element.textContent).toContain('€120.50');
  });
});
