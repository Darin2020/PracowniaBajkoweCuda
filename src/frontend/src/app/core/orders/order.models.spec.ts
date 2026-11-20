import { message } from '../../testing/chat-testing';
import {
  isFinal,
  nextStep,
  orderEvent,
  orderEventTitle,
  photosOpen,
  termsEditable,
} from './order.models';

describe('order models', () => {
  it('knows the next step of every status', () => {
    expect(nextStep('Draft')).toBe('confirm');
    expect(nextStep('AwaitingPayment')).toBe('mark-paid');
    expect(nextStep('InProgress')).toBe('mark-ready');
    expect(nextStep('Ready')).toBe('mark-shipped');
    expect(nextStep('Shipped')).toBe('complete');
    expect(nextStep('Completed')).toBeNull();
    expect(nextStep('Cancelled')).toBeNull();
  });

  it('follows the editing rules of the domain', () => {
    expect(termsEditable('Draft')).toBe(true);
    expect(termsEditable('AwaitingPayment')).toBe(true);
    expect(termsEditable('InProgress')).toBe(false);
    expect(photosOpen('InProgress')).toBe(true);
    expect(photosOpen('Ready')).toBe(true);
    expect(photosOpen('Shipped')).toBe(false);
    expect(isFinal('Completed')).toBe(true);
    expect(isFinal('Shipped')).toBe(false);
  });

  it('reads the payload of an order system message', () => {
    const event = orderEvent(
      message('m1', '2026-11-18T10:00:00Z', {
        kind: 'system',
        from: 'studio',
        body: '',
        orderId: 'o1',
        systemEventType: 'order.confirmed',
        systemEventPayload: {
          orderId: 'o1',
          number: 'BC-0012',
          title: 'Fox',
          amount: '149.90',
          currency: 'EUR',
          status: 'AwaitingPayment',
          paymentInstructions: 'Pay to ...',
          reason: null,
        },
      }),
    );

    expect(event).toEqual({
      type: 'order.confirmed',
      orderId: 'o1',
      number: 'BC-0012',
      title: 'Fox',
      amount: 149.9,
      currency: 'EUR',
      paymentInstructions: 'Pay to ...',
      reason: null,
    });
    expect(orderEventTitle(event!)).toBe('Order BC-0012 has been confirmed');
  });

  it('ignores text messages and copes with a missing payload', () => {
    expect(orderEvent(message('m1', '2026-11-18T10:00:00Z'))).toBeNull();

    const event = orderEvent(
      message('m2', '2026-11-18T10:00:00Z', {
        kind: 'system',
        systemEventType: 'order.shipped',
        systemEventPayload: null,
      }),
    );
    expect(event?.amount).toBeNull();
    expect(event?.orderId).toBeNull();
  });
});
