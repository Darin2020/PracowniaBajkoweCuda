import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { OrderStep, statusLabel } from '../../core/orders/order.models';

/** The steps an order went through, oldest first, with the date of each. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  selector: 'app-order-timeline',
  styles: `
    ol {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      position: relative;
      padding-left: 1.5rem;

      &::before {
        position: absolute;
        top: 0.4rem;
        left: 0.25rem;
        width: 0.625rem;
        height: 0.625rem;
        border-radius: 50%;
        background: var(--color-primary);
        content: '';
      }

      &:not(:last-child)::after {
        position: absolute;
        top: 1.1rem;
        bottom: -0.75rem;
        left: 0.5rem;
        width: 2px;
        background: var(--color-border);
        content: '';
      }
    }

    .step__status {
      font-weight: 600;
    }

    time,
    .step__comment {
      display: block;
      color: var(--color-text-muted);
      font-size: 0.875rem;
    }

    .step__comment {
      overflow-wrap: anywhere;
      white-space: pre-wrap;
    }
  `,
  template: `
    <ol>
      @for (step of steps(); track $index) {
        <li>
          <span class="step__status">{{ label(step) }}</span>
          <time [attr.datetime]="step.at">{{ step.at | date: 'medium' }}</time>
          @if (step.comment) {
            <span class="step__comment">{{ step.comment }}</span>
          }
        </li>
      }
    </ol>
  `,
})
export class OrderTimeline {
  readonly steps = input.required<readonly OrderStep[]>();

  protected label(step: OrderStep): string {
    return statusLabel(step.status);
  }
}
