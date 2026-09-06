import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { Automation } from '../../../domain/automation.interface';

export interface AutomationEditResult {
  name: string;
  description: string;
}

@Component({
  selector: 'app-automation-edit-dialog',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ButtonGroupComponent, InputComponent, ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './automation-edit-dialog.component.html',
  styleUrls: ['./automation-edit-dialog.component.scss'],
})
export class AutomationEditDialogComponent implements OnInit {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) mode: 'create' | 'edit' = 'create';
  @Input() automation: Automation | null = null;

  @Output() save = new EventEmitter<AutomationEditResult>();
  @Output() cancel = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly name = signal('');
  protected readonly description = signal('');

  get title(): string {
    return this.localization.translateKey(
      this.mode === 'create' ? AppStrings.Automations.NewAutomationHeading : AppStrings.Automations.RenameAutomationHeading
    );
  }

  get saveLabel(): string {
    return this.mode === 'create'
      ? this.localization.translateKey(AppStrings.Automations.CreateAutomationAction)
      : this.localization.translateKey(AppStrings.Automations.SaveChangesAction);
  }

  ngOnInit(): void {
    if (this.automation) {
      this.name.set(this.automation.name);
      this.description.set(this.automation.description);
    }
  }

  protected submit(): void {
    const name = this.name().trim();
    if (!name) {
      return;
    }

    dismissModal(this.modal, () => this.save.emit({ name, description: this.description().trim() }));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }
}
