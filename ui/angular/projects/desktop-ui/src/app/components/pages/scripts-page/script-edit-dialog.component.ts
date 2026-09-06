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

import { AppStrings, Script } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

export interface ScriptEditResult {
  name: string;
  description: string;
}

@Component({
  selector: 'app-script-edit-dialog',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ButtonGroupComponent, InputComponent, ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './script-edit-dialog.component.html',
  styleUrls: ['./script-edit-dialog.component.scss'],
})
export class ScriptEditDialogComponent implements OnInit {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) mode: 'create' | 'edit' = 'create';
  @Input() script: Script | null = null;

  @Output() save = new EventEmitter<ScriptEditResult>();
  @Output() cancel = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly name = signal('');
  protected readonly description = signal('');

  get title(): string {
    return this.localization.translateKey(
      this.mode === 'create' ? AppStrings.Scripts.NewScriptHeading : AppStrings.Scripts.RenameScriptHeading
    );
  }

  get saveLabel(): string {
    return this.mode === 'create'
      ? this.localization.translateKey(AppStrings.Scripts.CreateScriptAction)
      : this.localization.translateKey(AppStrings.Scripts.SaveChangesAction);
  }

  ngOnInit(): void {
    if (this.script) {
      this.name.set(this.script.name);
      this.description.set(this.script.description);
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
