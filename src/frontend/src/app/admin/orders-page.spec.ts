import { HttpTestingController } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ChatRealtime } from '../core/chat/chat-realtime.service';
import { provideApiTesting } from '../testing/auth-testing';
import { FakeRealtime, orderSummary } from '../testing/chat-testing';
import { AdminOrdersPage } from './orders-page';

describe('AdminOrdersPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideApiTesting(),
        provideRouter(
          [{ path: 'admin/orders', component: AdminOrdersPage }],
          withComponentInputBinding(),
        ),
        { provide: ChatRealtime, useValue: new FakeRealtime() },
        { provide: LOCALE_ID, useValue: 'en-US' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('lists every order with the customer, and filters by status through the address', async () => {
    const harness = await RouterTestingHarness.create('/admin/orders');
    http
      .expectOne((request) => request.url === '/api/admin/orders' && !request.params.has('status'))
      .flush({ items: [orderSummary('o1', { status: 'Draft' })], nextCursor: null });
    await settle();
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('.order')?.getAttribute('href')).toBe('/pl/admin/orders/o1');
    expect(element.querySelector('.order')?.textContent).toContain('Anna');
    expect(element.querySelector('.order')?.textContent).toContain('Draft');

    const select = element.querySelector<HTMLSelectElement>('#order-status')!;
    select.value = 'Ready';
    select.dispatchEvent(new Event('change'));
    await settle();
    await settle();
    http
      .expectOne(
        (request) =>
          request.url === '/api/admin/orders' && request.params.get('status') === 'Ready',
      )
      .flush({ items: [], nextCursor: null });
    await settle();
    harness.detectChanges();

    expect(element.textContent).toContain('No orders here');
  });
});
