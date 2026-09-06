import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslatePipe } from '../../../localization';

@Component({
  selector: 'shared-error-banner',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="eb" role="alert">
      <span class="eb-message">{{ message }}</span>
      @if (dismissible) {
        <button class="eb-dismiss" type="button" [attr.aria-label]="'macrodeck.app:Feedback.Dismiss' | translate" (click)="dismiss.emit()">
          <svg class="eb-dismiss-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M18 6 6 18"/>
            <path d="m6 6 12 12"/>
          </svg>
        </button>
      }
    </div>
  `,
  styleUrls: ['./error-banner.component.scss'],
})
export class ErrorBannerComponent {
  @Input({ required: true }) message = '';
  @Input() dismissible = true;
  @Output() dismiss = new EventEmitter<void>();
}
