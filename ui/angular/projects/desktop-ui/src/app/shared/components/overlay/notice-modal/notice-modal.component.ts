import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';

import { ButtonComponent } from '../../button/button.component';
import { CheckboxComponent } from '../../forms/checkbox/checkbox.component';
import { ModalComponent, dismissModal } from '../modal/modal.component';
import { LocalizationService } from '../../../localization';

@Component({
  selector: 'shared-notice-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, CheckboxComponent, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal [heading]="heading" [showFooter]="true" (close)="onClose()">
      <p class="notice-message">{{ message }}</p>

      <div modal-footer class="notice-footer">
        @if (showDontShowAgain) {
          <shared-checkbox
            class="notice-suppress"
            [label]="dontShowAgainLabel"
            [ngModel]="dontShowAgain()"
            (ngModelChange)="dontShowAgain.set($event)" />
        }
        <shared-button variant="primary" (click)="onClose()">{{ confirmText }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./notice-modal.component.scss'],
})
export class NoticeModalComponent {
  private readonly localization = inject(LocalizationService);

  private headingOverride?: string;
  private confirmTextOverride?: string;
  private dontShowAgainLabelOverride?: string;

  @Input() set heading(value: string) {
    this.headingOverride = value;
  }
  get heading(): string {
    return this.headingOverride ?? this.localization.translateKey(AppStrings.Dialogs.Notice.DefaultHeading);
  }

  @Input() message = '';

  @Input() set confirmText(value: string) {
    this.confirmTextOverride = value;
  }
  get confirmText(): string {
    return this.confirmTextOverride ?? this.localization.translateKey(AppStrings.Dialogs.Notice.GotIt);
  }

  @Input() showDontShowAgain = true;

  @Input() set dontShowAgainLabel(value: string) {
    this.dontShowAgainLabelOverride = value;
  }
  get dontShowAgainLabel(): string {
    return this.dontShowAgainLabelOverride
      ?? this.localization.translateKey(AppStrings.Dialogs.Notice.DontShowAgain);
  }

  @Output() closed = new EventEmitter<boolean>();

  protected readonly dontShowAgain = signal(false);

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  onClose(): void {
    dismissModal(this.modal, () => this.closed.emit(this.dontShowAgain()));
  }
}
