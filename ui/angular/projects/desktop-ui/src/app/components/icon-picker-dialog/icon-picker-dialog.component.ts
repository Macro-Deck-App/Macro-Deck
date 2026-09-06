import {
  ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Output, ViewChild, computed, effect, inject, signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { IconModel, IconPackService, isTerminalBatchState } from '../../services/icon-pack.service';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { EmptyStateComponent } from '../feedback/empty-state/empty-state.component';
import { IconGridComponent } from '../icon-grid/icon-grid.component';
import { ImportProgressComponent } from '../import-progress/import-progress.component';
import { FileDropDirective } from '../file-drop/file-drop.directive';
import { IconDropTargetDirective } from '../icon-drop/icon-drop-target.directive';

@Component({
  selector: 'shared-icon-picker-dialog',
  standalone: true,
  imports: [FormsModule, ModalComponent, SelectComponent, InputComponent, ButtonComponent, ButtonGroupComponent,
    EmptyStateComponent, IconGridComponent, ImportProgressComponent, FileDropDirective,
    IconDropTargetDirective, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon-picker-dialog.component.html',
  styleUrls: ['./icon-picker-dialog.component.scss']
})
export class IconPickerDialogComponent {
  protected readonly iconPacks = inject(IconPackService);
  private readonly localization = inject(LocalizationService);

  @Output() iconPicked = new EventEmitter<IconModel>();
  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  protected readonly selectedPackId = signal<string | null>(null);
  protected readonly searchQuery = signal('');
  protected readonly selectedIcon = signal<IconModel | null>(null);
  protected readonly importing = signal(false);

  protected readonly packOptions = computed<SelectOption[]>(() =>
    this.iconPacks.packs().map(pack => ({ value: pack.id, label: pack.name })));

  protected readonly selectedPack = computed(() =>
    this.iconPacks.packs().find(p => p.id === this.selectedPackId()) ?? null);

  protected readonly canImport = computed(() => this.selectedPack()?.isReadOnly === false);

  protected readonly importBusy = computed(() => this.importing()
    || [...this.iconPacks.activeBatches().values()]
      .some(b => b.packId === this.selectedPackId() && !isTerminalBatchState(b.state)));

  protected readonly emptyMessage = computed(() => this.localization.translateKey(
    this.canImport()
      ? AppStrings.IconPacks.Picker.EmptyImportable
      : AppStrings.IconPacks.Picker.EmptyReadOnly,
  ));

  protected readonly dropHintText = computed(() => this.localization.translateKey(
    AppStrings.IconPacks.Picker.DropToImport, { packName: this.selectedPack()?.name ?? '' },
  ));

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.Heading));
  protected readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.SearchPlaceholder));
  protected readonly noIconsHeading = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.NoIconsHeading));
  protected readonly importIconsLabel = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.ImportIcons));
  protected readonly importingLabel = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.ImportingIcons));
  protected readonly useIconLabel = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Picker.UseIcon));

  protected readonly icons = computed(() => {
    const packId = this.selectedPackId();
    if (!packId) {
      return [];
    }

    const icons = this.iconPacks.iconsFor(packId)();
    const query = this.searchQuery().trim().toLowerCase();
    return query ? icons.filter(i => i.name.toLowerCase().includes(query)) : icons;
  });

  constructor() {
    void this.iconPacks.loadPacks();

    effect(() => {
      const packs = this.iconPacks.packs();
      const selected = this.selectedPackId();
      if (packs.length > 0 && (!selected || !packs.some(p => p.id === selected))) {
        this.selectedPackId.set(packs.find(p => p.isDefault)?.id ?? packs[0].id);
      }
    });
  }

  protected onPackChange(packId: string | null): void {
    this.selectedPackId.set(packId);
    this.selectedIcon.set(null);
  }

  protected onIconClick(icon: IconModel): void {
    if (icon.processingState === 'Ready') {
      this.selectedIcon.set(icon);
    }
  }

  protected pick(): void {
    const icon = this.selectedIcon();
    if (icon) {
      dismissModal(this.modal, () => this.iconPicked.emit(icon));
    }
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }

  protected pickFiles(): void {
    this.fileInput?.nativeElement.click();
  }

  protected async onImportFiles(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.onFilesDropped(files);
  }

  protected async onFilesDropped(files: File[]): Promise<void> {
    const packId = this.selectedPackId();
    if (files.length === 0 || !packId || !this.canImport()) {
      return;
    }

    this.importing.set(true);
    try {
      await this.iconPacks.import(packId, files);
    } finally {
      this.importing.set(false);
    }
  }

  protected async onPathsDropped(paths: string[]): Promise<void> {
    const packId = this.selectedPackId();
    if (paths.length === 0 || !packId || !this.canImport()) {
      return;
    }

    this.importing.set(true);
    try {
      await this.iconPacks.importFromPath(packId, paths);
    } finally {
      this.importing.set(false);
    }
  }
}
