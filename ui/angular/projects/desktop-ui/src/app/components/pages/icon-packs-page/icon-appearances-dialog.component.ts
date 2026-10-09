import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy, Component, ElementRef, ViewChild, computed, inject, input, output, signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings, ICON_APPEARANCE_KINDS, iconAppearanceKindLabel, iconAppearanceVariantKey, iconAppearanceVariantName,
} from '@macro-deck/runtime';
import {
  ButtonComponent, ButtonGroupComponent, ContextMenuComponent, ContextMenuItem, IconImageService, InputComponent,
  LocalizationService, ModalComponent, ToastService, TranslatePipe, dismissModal,
} from '@shared';
import { IconAppearanceModel, IconModel, IconPackModel, IconPackService } from '../../../services/icon-pack.service';
import { IconGridComponent } from '../../icon-grid/icon-grid.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';

const CUSTOM_KIND = 'custom';

@Component({
  selector: 'app-icon-appearances-dialog',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ButtonGroupComponent,
    ConfirmationModalComponent,
    ContextMenuComponent,
    IconGridComponent,
    InputComponent,
    ModalComponent,
    NgTemplateOutlet,
    SelectComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon-appearances-dialog.component.html',
  styleUrls: ['./icon-appearances-dialog.component.scss'],
})
export class IconAppearancesDialogComponent {
  private readonly iconPacks = inject(IconPackService);
  private readonly iconImage = inject(IconImageService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);

  readonly icon = input.required<IconModel>();
  readonly pack = input.required<IconPackModel>();
  readonly closed = output<void>();

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;
  @ViewChild('mergeDialog') private mergeDialog?: ModalComponent;

  protected readonly busy = signal(false);
  protected readonly addMenu = signal<{ x: number; y: number } | null>(null);
  protected readonly pendingRemoval = signal<IconAppearanceModel | null>(null);
  protected readonly merging = signal(false);
  protected readonly mergeKey = signal('');
  protected readonly mergeSourceId = signal<string | null>(null);
  protected readonly naming = signal(false);
  protected readonly customName = signal('');
  private pendingKey: string | null = null;

  protected readonly readOnly = computed(() => this.pack().isReadOnly);
  protected readonly appearances = computed(() => this.icon().appearances ?? []);

  protected readonly title = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Appearances.DialogTitle, { name: this.icon().name }));

  protected readonly missingKinds = computed(() => {
    const present = new Set(this.appearances().map(appearance => appearance.key));
    return ICON_APPEARANCE_KINDS.filter(kind => !present.has(kind.key));
  });

  protected readonly addMenuItems = computed<ContextMenuItem[]>(() => [
    ...this.missingKinds().map(kind => ({ id: kind.key, label: this.localization.translateKey(kind.label) })),
    { id: CUSTOM_KIND, label: this.localization.translateKey(AppStrings.IconPacks.Appearances.AddCustom) },
  ]);

  protected readonly mergeKindOptions = computed<SelectOption[]>(() => [
    ...this.missingKinds().map(kind => ({ value: kind.key, label: this.localization.translateKey(kind.label) })),
    { value: CUSTOM_KIND, label: this.localization.translateKey(AppStrings.IconPacks.Appearances.AddCustom) },
  ]);

  protected readonly customKey = computed(() => iconAppearanceVariantKey(this.customName()));

  protected readonly customPreview = computed(() => {
    const key = this.customKey();
    const name = key === null ? null : iconAppearanceVariantName(key);
    return name === null ? null
      : this.localization.translateKey(AppStrings.IconPacks.Appearances.CustomName.Preview, { label: name });
  });

  protected readonly customInvalid = computed(() => this.customName().trim().length > 0 && this.customKey() === null);

  protected readonly mergeNeedsName = computed(() => this.mergeKey() === CUSTOM_KIND);

  protected readonly mergeResolvedKey = computed(() => this.mergeNeedsName() ? this.customKey() : this.mergeKey());

  protected readonly mergeCandidates = computed(() => {
    const iconId = this.icon().id;
    return this.iconPacks.iconsFor(this.pack().id)().filter(candidate =>
      candidate.id !== iconId && (candidate.appearances?.length ?? 0) === 0);
  });

  protected readonly mergeHint = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Appearances.MergeHint, { name: this.icon().name }));

  protected kindLabel(key: string): string {
    const kind = iconAppearanceKindLabel(key);
    return this.localization.translateKey(kind.label, kind.args);
  }

  protected defaultImageUrl(): string | null {
    return this.iconImage.getIconUrl(this.icon().id, 128, this.icon().contentHash);
  }

  protected appearanceImageUrl(appearance: IconAppearanceModel): string | null {
    return this.iconImage.getIconUrl(appearance.id, 128, appearance.contentHash);
  }

  protected previewAlt(kind: string): string {
    return this.localization.translateKey(AppStrings.IconPacks.Appearances.PreviewAlt, { kind, name: this.icon().name });
  }

  protected failedLabel(appearance: IconAppearanceModel): string {
    return appearance.processingError
      ? this.localization.translateKey(AppStrings.IconPacks.Tile.ImportFailedWithReason, { reason: appearance.processingError })
      : this.localization.translateKey(AppStrings.IconPacks.Tile.ImportFailed);
  }

  protected removeMessage(appearance: IconAppearanceModel): string {
    return this.localization.translateKey(AppStrings.IconPacks.Appearances.RemoveConfirmMessage, {
      kind: this.kindLabel(appearance.key),
      name: this.icon().name,
    });
  }

  protected openAddMenu(event: MouseEvent): void {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.addMenu.set({ x: rect.left, y: rect.bottom + 4 });
  }

  protected onAddMenuAction(key: string): void {
    this.addMenu.set(null);
    if (key === CUSTOM_KIND) {
      this.customName.set('');
      this.naming.set(true);
      return;
    }

    this.pickFile(key);
  }

  protected confirmName(): void {
    const key = this.customKey();
    if (key === null) {
      return;
    }

    this.naming.set(false);
    this.pickFile(key);
  }

  protected pickFile(key: string): void {
    this.pendingKey = key;
    this.fileInput?.nativeElement.click();
  }

  protected async onFilePicked(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    const key = this.pendingKey;
    input.value = '';
    this.pendingKey = null;
    if (!file || !key) {
      return;
    }

    await this.run(
      () => this.iconPacks.addAppearance(this.icon().id, key, file),
      AppStrings.IconPacks.Appearances.AddFailed);
  }

  protected async confirmRemove(): Promise<void> {
    const appearance = this.pendingRemoval();
    this.pendingRemoval.set(null);
    if (!appearance) {
      return;
    }

    await this.run(
      () => this.iconPacks.removeAppearance(this.icon().id, appearance.id),
      AppStrings.IconPacks.Appearances.RemoveFailed);
  }

  protected openMerge(): void {
    this.mergeKey.set(this.missingKinds()[0]?.key ?? CUSTOM_KIND);
    this.customName.set('');
    this.mergeSourceId.set(null);
    this.merging.set(true);
  }

  protected cancelMerge(): void {
    dismissModal(this.mergeDialog, () => this.merging.set(false));
  }

  protected async confirmMerge(): Promise<void> {
    const sourceId = this.mergeSourceId();
    const key = this.mergeResolvedKey();
    if (!sourceId || !key) {
      return;
    }

    const merged = await this.run(
      () => this.iconPacks.mergeAppearance(this.icon().id, sourceId, key),
      AppStrings.IconPacks.Appearances.MergeFailed);
    if (merged) {
      this.cancelMerge();
    }
  }

  private async run(change: () => Promise<boolean>, failure: string): Promise<boolean> {
    this.busy.set(true);
    try {
      const ok = await change();
      if (!ok) {
        this.toasts.show(this.localization.translateKey(failure), { variant: 'error' });
      }
      return ok;
    } finally {
      this.busy.set(false);
    }
  }
}
