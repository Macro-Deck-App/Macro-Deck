import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy, ViewChild, inject } from '@angular/core';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, dismissModal } from '@shared';

@Component({
  selector: 'shared-confirmation-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirmation-modal.component.html',
  styleUrls: ['./confirmation-modal.component.scss']
})
export class ConfirmationModalComponent {
  private readonly localization = inject(LocalizationService);

  private headingOverride?: string;
  private messageOverride?: string;
  private confirmTextOverride?: string;
  private cancelTextOverride?: string;

  @Input() set heading(value: string) {
    this.headingOverride = value;
  }
  get heading(): string {
    return this.headingOverride ?? this.localization.translateKey(AppStrings.Dialogs.Confirm.DefaultHeading);
  }

  @Input() set message(value: string) {
    this.messageOverride = value;
  }
  get message(): string {
    return this.messageOverride ?? this.localization.translateKey(AppStrings.Dialogs.Confirm.DefaultMessage);
  }

  @Input() set confirmText(value: string) {
    this.confirmTextOverride = value;
  }
  get confirmText(): string {
    return this.confirmTextOverride ?? this.localization.translateKey(Strings.Common.Confirm);
  }

  @Input() set cancelText(value: string) {
    this.cancelTextOverride = value;
  }
  get cancelText(): string {
    return this.cancelTextOverride ?? this.localization.translateKey(Strings.Common.Cancel);
  }
  @Input() altText = '';
  @Input() danger = false;

  @Output() confirm = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @Output() alt = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  onConfirm(): void {
    dismissModal(this.modal, () => this.confirm.emit());
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }

  onAlt(): void {
    dismissModal(this.modal, () => this.alt.emit());
  }
}
