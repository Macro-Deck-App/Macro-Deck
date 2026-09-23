import {
  ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, ViewChild, computed, inject, signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, IconPackAiAssets } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { IconPackModel } from '../../../services/icon-pack.service';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';

export interface PackEditResult {
  name: string;
  description?: string;
  author?: string;
  version?: string;
  aiAssets: IconPackAiAssets;
}

@Component({
  selector: 'app-pack-edit-dialog',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ButtonGroupComponent, InputComponent, ModalComponent, SelectComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pack-edit-dialog.component.html',
  styleUrls: ['./pack-edit-dialog.component.scss'],
})
export class PackEditDialogComponent implements OnInit {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) mode: 'create' | 'edit' = 'create';
  @Input() pack: IconPackModel | null = null;

  @Output() save = new EventEmitter<PackEditResult>();
  @Output() cancel = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly author = signal('');
  protected readonly version = signal('');
  protected readonly aiAssets = signal<IconPackAiAssets>('NotDeclared');

  protected readonly aiOptions = computed<SelectOption[]>(() => [
    { value: 'NotDeclared', label: this.localization.translateKey(AppStrings.IconPacks.AiNotDeclared) },
    { value: 'None', label: this.localization.translateKey(AppStrings.IconPacks.AiNone) },
    { value: 'Generated', label: this.localization.translateKey(AppStrings.IconPacks.AiGenerated) },
  ]);

  get title(): string {
    return this.localization.translateKey(
      this.mode === 'create' ? AppStrings.IconPacks.NewPackHeading : AppStrings.IconPacks.EditPackHeading
    );
  }

  get saveLabel(): string {
    return this.mode === 'create'
      ? this.localization.translateKey(AppStrings.IconPacks.CreatePackAction)
      : this.localization.translateKey(AppStrings.IconPacks.SaveChangesAction);
  }

  ngOnInit(): void {
    if (this.pack) {
      this.name.set(this.pack.name);
      this.description.set(this.pack.description ?? '');
      this.author.set(this.pack.author ?? '');
      this.version.set(this.pack.version ?? '');
      this.aiAssets.set(this.pack.aiAssets ?? 'NotDeclared');
    }
  }

  protected submit(): void {
    const name = this.name().trim();
    if (!name) {
      return;
    }

    dismissModal(this.modal, () => this.save.emit({
      name,
      description: this.description().trim() || undefined,
      author: this.author().trim() || undefined,
      version: this.version().trim() || undefined,
      aiAssets: this.aiAssets(),
    }));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }
}
