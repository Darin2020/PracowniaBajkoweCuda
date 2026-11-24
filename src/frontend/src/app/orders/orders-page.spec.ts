import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, image, orderSummary } from '../testing/chat-testing';
import { OrdersPage } from './orders-page';
import { expectAccessible } from '../testing/axe';

describe('OrdersPage', () => {
  let http: HttpTestingController;
  let realtime: FakeRealtime;

  beforeEach(() => {
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter([{ path: 'orders', component: OrdersPage }]),
        { provide: ChatRealtime, useValue: realtime },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('lists the orders with number, status, price and a link', async () => {
    const harness = await RouterTestingHarness.create('/orders');
    http.expectOne('/api/orders').flush([orderSummary('o1', { thumbnail: image('p1') })]);
    await settle();
    harness.detectChanges();
    const element = harness.routeNativeElement!;

    const order = element.querySelector('.order')!;
    expect(order.getAttribute('href')).toBe('/pl/orders/o1');
    expect(order.textContent).toContain('BC-0012');
    expect(order.textContent).toContain('Fox with a scarf');
    expect(order.textContent).toContain('Awaiting payment');
    expect(order.textContent).toContain('€149.90');
    expect(order.querySelector('img')?.getAttribute('src')).toBe('/api/files/p1/thumbnail');
  });

  it('points to the chat when there are no orders, and reloads when an order changes', async () => {
    const harness = await RouterTestingHarness.create('/orders');
    http.expectOne('/api/orders').flush([]);
    await settle();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('a.button')?.getAttribute('href')).toBe(
      '/pl/chat',
    );

    realtime.orders.next(orderSummary('o1'));
    http.expectOne('/api/orders').flush([orderSummary('o1')]);
    await settle();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelectorAll('.order')).toHaveLength(1);
  });

  it('has no accessibility violations', async () => {
    const harness = await RouterTestingHarness.create('/orders');
    http
      .expectOne('/api/orders')
      .flush([orderSummary('o1', { thumbnail: image('p1') }), orderSummary('o2')]);
    await settle();
    harness.detectChanges();
    await expectAccessible(harness.routeNativeElement!);
  });
});
