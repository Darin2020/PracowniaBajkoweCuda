import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, customerOrder, image, orderSummary } from '../testing/chat-testing';
import { OrderPage } from './order-page';

describe('OrderPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([{ path: 'orders/:id', component: OrderPage }], withComponentInputBinding()),
        { provide: ChatRealtime, useValue: realtime },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function open(order = customerOrder('o1')) {
    const harness = await RouterTestingHarness.create('/orders/o1');
    http.expectOne('/api/orders/o1').flush(order);
    await settle();
    harness.detectChanges();
    return harness;
  }

  it('shows the order with the payment instructions while payment is awaited', async () => {
    const element = (await open()).routeNativeElement!;

    expect(element.querySelector('h1')?.textContent).toContain('Fox with a scarf');
    expect(element.textContent).toContain('BC-0012');
    expect(element.textContent).toContain('Awaiting payment');
    expect(element.textContent).toContain('Transfer 149.90 EUR, title BC-0012');
    expect(element.textContent).toContain('Orange fox, 30 cm');
    expect(element.querySelector('app-order-timeline')?.textContent).toContain('Awaiting payment');
  });

  it('shows the photos of the finished toy and the cancellation reason', async () => {
    const element = (
      await open(
        customerOrder('o1', {
          status: 'Cancelled',
          paymentInstructions: null,
          cancellationReason: 'Out of fabric',
          photos: [image('p1'), image('p2')],
        }),
      )
    ).routeNativeElement!;

    expect(element.textContent).not.toContain('How to pay');
    expect(element.querySelector('.alert--error')?.textContent).toContain('Out of fabric');
    const thumbnails = Array.from(element.querySelectorAll('app-photo-gallery img')).map((img) =>
      img.getAttribute('src'),
    );
    expect(thumbnails).toEqual([
      '/api/files/ref/thumbnail',
      '/api/files/p1/thumbnail',
      '/api/files/p2/thumbnail',
    ]);
  });

  it('reloads when this order changes', async () => {
    const harness = await open();

    realtime.orders.next(orderSummary('other'));
    realtime.orders.next(orderSummary('o1', { status: 'InProgress' }));
    http
      .expectOne('/api/orders/o1')
      .flush(customerOrder('o1', { status: 'InProgress', paymentInstructions: null }));
    await settle();
    harness.detectChanges();

    expect(harness.routeNativeElement!.textContent).toContain('In progress');
  });

  it('says so when the order is not available', async () => {
    const harness = await RouterTestingHarness.create('/orders/o1');
    http
      .expectOne('/api/orders/o1')
      .flush({ code: 'orders.not_found' }, { status: 404, statusText: 'Not Found' });
    await settle();
    harness.detectChanges();

    expect(harness.routeNativeElement!.querySelector('[role="alert"]')?.textContent).toContain(
      'This order does not exist',
    );
  });
});
