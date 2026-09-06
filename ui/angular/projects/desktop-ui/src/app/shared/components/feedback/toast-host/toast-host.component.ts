import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../../../services/toast.service';
import { TranslatePipe } from '../../../localization';

@Component({
  selector: 'shared-toast-host',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="th" role="status" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="th-toast" [class.th-error]="toast.variant === 'error'">
          <svg class="th-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            @if (toast.variant === 'error') {
              <circle cx="12" cy="12" r="10"/>
              <path d="M12 8v5"/>
              <path d="M12 16h.01"/>
            } @else {
              <path d="M20 6 9 17l-5-5"/>
            }
          </svg>
          <div class="th-text">
            <span class="th-message">{{ toast.message }}</span>
            @if (toast.detail) {
              <span class="th-detail">{{ toast.detail }}</span>
            }
          </div>
          <button class="th-dismiss" type="button" [attr.aria-label]="'macrodeck.app:Feedback.Dismiss' | translate" (click)="toasts.dismiss(toast.id)">
            <svg class="th-dismiss-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
              stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M18 6 6 18"/>
              <path d="m6 6 12 12"/>
            </svg>
          </button>
        </div>
      }
    </div>
  `,
  styleUrls: ['./toast-host.component.scss'],
})
export class ToastHostComponent {
  protected readonly toasts = inject(ToastService);
}
