import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject,
} from '@angular/core';

import { AppStrings, Strings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, dismissModal } from '@shared';
import { selectAllText } from '../../../util/select-all-text';

@Component({
  selector: 'shared-copy-text-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal [heading]="heading" [showFooter]="true" [zIndex]="zIndex" (close)="onClose()">
      <p class="copy-text-message">{{ message }}</p>
      <input
        #field
        class="copy-text-field"
        type="text"
        readonly
        [attr.aria-label]="textToCopyLabel"
        [value]="value"
        (focus)="select()"
        (click)="select()">

      <div modal-footer class="copy-text-footer">
        <shared-button variant="primary" (click)="onClose()">{{ confirmText }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./copy-text-modal.component.scss'],
})
export class CopyTextModalComponent implements AfterViewInit {
  private readonly localization = inject(LocalizationService);

  private headingOverride?: string;
  private confirmTextOverride?: string;

  @Input() set heading(value: string) {
    this.headingOverride = value;
  }
  get heading(): string {
    return this.headingOverride ?? this.localization.translateKey(AppStrings.Dialogs.CopyText.DefaultHeading);
  }

  @Input() message = '';
  @Input() value = '';

  @Input() set confirmText(value: string) {
    this.confirmTextOverride = value;
  }
  get confirmText(): string {
    return this.confirmTextOverride ?? this.localization.translateKey(Strings.Common.Done);
  }

  @Input() zIndex: number | null = null;

  protected get textToCopyLabel(): string {
    return this.localization.translateKey(AppStrings.Dialogs.CopyText.FieldAriaLabel);
  }

  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChild('field') private field?: ElementRef<HTMLInputElement>;

  ngAfterViewInit(): void {
    this.select();
  }

  protected select(): void {
    const field = this.field?.nativeElement;
    if (field) {
      selectAllText(field);
    }
  }

  onClose(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }
}
