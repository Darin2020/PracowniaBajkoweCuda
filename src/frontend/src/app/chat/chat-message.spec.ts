import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChatMessage } from '../core/chat/chat.models';
import { message } from '../testing/chat-testing';
import { ChatMessageView } from './chat-message';
import { ChatSide } from './chat-session';

describe('ChatMessageView', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'en-US' }],
    });
  });

  function render(item: ChatMessage, side: ChatSide) {
    const fixture = TestBed.createComponent(ChatMessageView);
    fixture.componentRef.setInput('message', item);
    fixture.componentRef.setInput('side', side);
    fixture.componentRef.setInput('otherName', 'Studio');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const confirmed = message('m1', '2026-11-18T10:00:00Z', {
    kind: 'system',
    from: 'studio',
    body: '',
    orderId: 'o1',
    systemEventType: 'order.confirmed',
    systemEventPayload: {
      orderId: 'o1',
      number: 'BC-0012',
      title: 'Fox with a scarf',
      amount: '149.90',
      currency: 'EUR',
      paymentInstructions: 'IBAN PL00 0000, title BC-0012',
    },
  });

  it('shows an order confirmation with the amount, the payment instructions and a link', () => {
    const element = render(confirmed, 'customer');

    expect(element.querySelector('.message__event')?.textContent).toContain(
      'Order BC-0012 has been confirmed',
    );
    expect(element.textContent).toContain('Fox with a scarf');
    expect(element.textContent).toContain('€149.90');
    expect(element.querySelector('.message__instructions')?.textContent).toContain(
      'IBAN PL00 0000',
    );
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/orders/o1');
  });

  it('links the studio to the admin order page', () => {
    const element = render(confirmed, 'studio');

    expect(element.querySelector('a')?.getAttribute('href')).toBe('/admin/orders/o1');
  });

  it('shows the reason of a cancellation', () => {
    const element = render(
      {
        ...confirmed,
        systemEventType: 'order.cancelled',
        systemEventPayload: { orderId: 'o1', number: 'BC-0012', reason: 'Out of fabric' },
      },
      'customer',
    );

    expect(element.querySelector('.message__event')?.textContent).toContain('has been cancelled');
    expect(element.querySelector('.message__instructions')?.textContent).toContain('Out of fabric');
  });
});
