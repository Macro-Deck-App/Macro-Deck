import { ChangeDetectionStrategy, Component, ElementRef, HostListener, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { ButtonComponent, ContextMenuComponent, ContextMenuItem, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, ToastService, TranslatePipe, dismissModal } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { FileDropDirective } from '../../file-drop/file-drop.directive';
import { FileBrowserDialogComponent } from '../../forms/file-browser-dialog/file-browser-dialog.component';
import { IconDropTargetDirective } from '../../icon-drop/icon-drop-target.directive';
import { IconGridComponent } from '../../icon-grid/icon-grid.component';
import { IconTileClick } from '../../icon-grid/icon-tile.component';
import { ImportProgressComponent } from '../../import-progress/import-progress.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { CopyTextModalComponent } from '../../overlay/copy-text-modal/copy-text-modal.component';
import { RailItemComponent } from '../../rail-page/rail-item.component';
import { RailPageComponent } from '../../rail-page/rail-page.component';
import { archiveDropKind } from '../../../domain/archive-drop.util';
import { savedFileDetail } from '../../../services/file-save.service';
import { IconModel, IconPackModel, IconPackService } from '../../../services/icon-pack.service';
import { TextClipboardService, clipboardFailureDetail } from '../../../services/text-clipboard.service';
import { PackEditDialogComponent, PackEditResult } from './pack-edit-dialog.component';

interface IconContextMenuState {
  icon: IconModel;
  x: number;
  y: number;
}

interface PackContextMenuState {
  pack: IconPackModel;
  x: number;
  y: number;
}

@Component({
  selector: 'app-icon-packs-page',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ConfirmationModalComponent,
    ContextMenuComponent,
    CopyTextModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    FileBrowserDialogComponent,
    FileDropDirective,
    IconDropTargetDirective,
    IconGridComponent,
    ImportProgressComponent,
    InputComponent,
    LoadingStateComponent,
    ModalComponent,
    PackEditDialogComponent,
    RailItemComponent,
    RailPageComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon-packs-page.component.html',
  styleUrls: ['./icon-packs-page.component.scss'],
})
export class IconPacksPageComponent {
  protected readonly iconPacks = inject(IconPackService);
  private readonly toasts = inject(ToastService);
  private readonly clipboard = inject(TextClipboardService);
  private readonly localization = inject(LocalizationService);

  @ViewChild('fileInput') fileInput?: ElementRef<HTMLInputElement>;
  @ViewChild('folderInput') folderInput?: ElementRef<HTMLInputElement>;
  @ViewChild('packInput') packInput?: ElementRef<HTMLInputElement>;
  @ViewChild(ModalComponent) private renameModal?: ModalComponent;

  protected readonly selectedPackId = signal<string | null>(null);
  protected readonly searchQuery = signal('');

  protected readonly packDialogMode = signal<'create' | 'edit' | null>(null);
  protected readonly packPendingDeletion = signal<IconPackModel | null>(null);
  protected readonly packMenu = signal<PackContextMenuState | null>(null);
  protected readonly iconMenu = signal<IconContextMenuState | null>(null);
  protected readonly iconPendingRename = signal<IconModel | null>(null);
  protected readonly renameValue = signal('');
  protected readonly showPathBrowser = signal(false);
  protected readonly manualCopy = signal<{ value: string; message: string } | null>(null);

  protected readonly selectedIconIds = signal<ReadonlySet<string>>(new Set());
  protected readonly iconsPendingDeletion = signal<IconModel[] | null>(null);
  private selectionAnchorId: string | null = null;

  protected readonly selectedPack = computed(() =>
    this.iconPacks.packs().find(p => p.id === this.selectedPackId()) ?? null
  );

  protected readonly icons = computed(() => {
    const packId = this.selectedPackId();
    return packId ? this.iconPacks.iconsFor(packId)() : [];
  });

  protected readonly filteredIcons = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const icons = this.icons();
    if (!query) {
      return icons;
    }

    return icons.filter(icon => icon.name.toLowerCase().includes(query));
  });

  protected readonly packMenuItems = computed<ContextMenuItem[]>(() => {
    const pack = this.packMenu()?.pack;
    const readOnly = pack?.isReadOnly ?? false;
    const t = (key: string): string => this.localization.translateKey(key);
    return [
      { id: 'edit', label: t(AppStrings.IconPacks.EditPackAction), icon: 'icon-pencil', disabled: readOnly },
      { id: 'import', label: t(AppStrings.IconPacks.ImportIconsAction), icon: 'icon-download', disabled: readOnly },
      { id: 'export', label: t(AppStrings.IconPacks.ExportPackAction), icon: 'icon-upload', dividerAfter: true },
      { id: 'delete', label: t(AppStrings.IconPacks.DeletePackAction), icon: 'icon-trash', danger: true, dividerAfter: false,
        disabled: readOnly || (pack?.isDefault ?? false) || !(pack?.canDelete ?? true) },
    ];
  });

  protected readonly iconMenuItems = computed<ContextMenuItem[]>(() => {
    const readOnly = this.selectedPack()?.isReadOnly ?? false;
    const menuIcon = this.iconMenu()?.icon;
    const selection = this.selectedIconIds();
    const bulk = menuIcon && selection.has(menuIcon.id) && selection.size > 1 ? selection.size : 0;
    return [
      { id: 'rename', label: this.localization.translateKey(Strings.Common.Rename), icon: 'icon-pencil', disabled: readOnly || bulk > 0 },
      { id: 'copy-id', label: this.localization.translateKey(AppStrings.IconPacks.CopyIconIdAction), icon: 'icon-copy', dividerAfter: true },
      {
        id: 'delete',
        label: this.localization.translateKey(AppStrings.IconPacks.DeleteIconAction, { count: bulk > 0 ? bulk : 1 }),
        icon: 'icon-trash',
        danger: true,
        disabled: readOnly,
      },
    ];
  });

  protected readonly importMenu = signal<{ x: number; y: number } | null>(null);

  protected readonly importMenuItems: ContextMenuItem[] = [
    { id: 'files', label: this.localization.translateKey(AppStrings.IconPacks.ImportFilesAction), icon: 'icon-file-text' },
    { id: 'folder', label: this.localization.translateKey(AppStrings.IconPacks.ImportFolderAction), icon: 'icon-folder' },
    { id: 'path', label: this.localization.translateKey(AppStrings.IconPacks.ImportFromHostPathAction), icon: 'icon-code' },
  ];

  protected readonly addPackMenu = signal<{ x: number; y: number } | null>(null);

  protected readonly addPackMenuItems: ContextMenuItem[] = [
    { id: 'create', label: this.localization.translateKey(AppStrings.IconPacks.CreatePackAction), icon: 'icon-plus' },
    { id: 'import', label: this.localization.translateKey(AppStrings.IconPacks.ImportPackAction), icon: 'icon-download' },
  ];

  constructor() {
    void this.iconPacks.loadPacks();

    effect(() => {
      const packs = this.iconPacks.packs();
      const selected = this.selectedPackId();
      if (packs.length === 0) {
        this.selectedPackId.set(null);
        return;
      }

      if (!selected || !packs.some(p => p.id === selected)) {
        this.selectedPackId.set(packs.find(p => p.isDefault)?.id ?? packs[0].id);
      }
    });
  }

  protected selectPack(pack: IconPackModel): void {
    this.selectedPackId.set(pack.id);
    this.searchQuery.set('');
    this.clearSelection();
  }

  protected onIconClick(click: IconTileClick): void {
    const id = click.icon.id;
    const toggle = click.toggle || (!click.range && this.selectedIconIds().size > 0);
    if (toggle) {
      const next = new Set(this.selectedIconIds());
      if (!next.delete(id)) {
        next.add(id);
      }

      this.selectedIconIds.set(next);
      this.selectionAnchorId = id;
      return;
    }

    if (click.range && this.selectionAnchorId) {
      const icons = this.filteredIcons();
      const anchorIndex = icons.findIndex(i => i.id === this.selectionAnchorId);
      const targetIndex = icons.findIndex(i => i.id === id);
      if (anchorIndex >= 0 && targetIndex >= 0) {
        const [from, to] = anchorIndex <= targetIndex
          ? [anchorIndex, targetIndex]
          : [targetIndex, anchorIndex];
        this.selectedIconIds.set(new Set(icons.slice(from, to + 1).map(i => i.id)));
        return;
      }
    }

    this.selectedIconIds.set(new Set([id]));
    this.selectionAnchorId = id;
  }

  protected clearSelection(): void {
    this.selectedIconIds.set(new Set());
    this.selectionAnchorId = null;
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeyDown(event: KeyboardEvent): void {
    if (this.packDialogMode() || this.iconPendingRename() || this.iconsPendingDeletion()
      || this.packPendingDeletion() || this.showPathBrowser()) {
      return;
    }

    if (event.key === 'Escape' && this.selectedIconIds().size > 0) {
      this.clearSelection();
      return;
    }

    const isEditable = event.target instanceof HTMLElement
      && (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA'
        || event.target.isContentEditable);
    if ((event.key === 'Delete' || event.key === 'Backspace') && !isEditable) {
      this.requestDeleteSelection();
    }
  }

  protected requestDeleteSelection(): void {
    const selection = this.selectedIconIds();
    if (selection.size === 0 || this.selectedPack()?.isReadOnly) {
      return;
    }

    this.iconsPendingDeletion.set(this.icons().filter(i => selection.has(i.id)));
  }

  protected deleteIconsMessage(icons: IconModel[]): string {
    if (icons.length === 1) {
      return this.localization.translateKey(AppStrings.IconPacks.DeleteIconMessage, { name: icons[0].name });
    }

    return this.localization.translateKey(AppStrings.IconPacks.DeleteIconsMessage, { count: icons.length });
  }

  protected async confirmDeleteIcons(): Promise<void> {
    const icons = this.iconsPendingDeletion();
    this.iconsPendingDeletion.set(null);
    const packId = this.selectedPackId();
    if (!icons || icons.length === 0 || !packId) {
      return;
    }

    await this.iconPacks.deleteIcons(icons.map(i => i.id), packId);
    this.clearSelection();
  }

  protected openCreatePack(): void {
    this.packDialogMode.set('create');
  }

  protected openEditPack(): void {
    if (this.selectedPack()?.isReadOnly !== true) {
      this.packDialogMode.set('edit');
    }
  }

  protected async onPackDialogSave(result: PackEditResult): Promise<void> {
    if (this.packDialogMode() === 'create') {
      const pack = await this.iconPacks.createPack(result);
      if (pack) {
        this.selectedPackId.set(pack.id);
      }
    } else {
      const packId = this.selectedPackId();
      if (packId) {
        await this.iconPacks.updatePack(packId, result);
      }
    }

    this.packDialogMode.set(null);
  }

  protected requestDeletePack(pack: IconPackModel): void {
    if (!pack.isReadOnly && !pack.isDefault && pack.canDelete) {
      this.packPendingDeletion.set(pack);
    }
  }

  protected deletePackMessage(pack: IconPackModel): string {
    const key = pack.ownerKind === 'Store' ? AppStrings.IconPacks.DeleteStorePackMessage : AppStrings.IconPacks.DeletePackMessage;
    return this.localization.translateKey(key, { name: pack.name, count: pack.iconCount });
  }

  protected sourceLabel(pack: IconPackModel): string | null {
    return pack.ownerKind === 'Store' ? this.localization.translateKey(AppStrings.IconPacks.SourceStore) : null;
  }

  protected async confirmDeletePack(): Promise<void> {
    const pack = this.packPendingDeletion();
    this.packPendingDeletion.set(null);
    if (pack) {
      await this.iconPacks.deletePack(pack.id);
    }
  }

  protected openPackMenu(event: MouseEvent, pack: IconPackModel): void {
    event.preventDefault();
    this.selectPack(pack);
    this.packMenu.set({ pack, x: event.clientX, y: event.clientY });
  }

  protected async exportPack(pack: IconPackModel): Promise<void> {
    const result = await this.iconPacks.exportPack(pack.id);
    if (!result.ok) {
      this.toasts.show(this.localization.translateKey(AppStrings.IconPacks.PackExportFailed), { detail: result.error, variant: 'error' });
      return;
    }
    if (!result.canceled) {
      this.toasts.show(
        this.localization.translateKey(AppStrings.IconPacks.PackExported, { name: pack.name }),
        { detail: savedFileDetail(result) }
      );
    }
  }

  protected onPackMenuAction(action: string): void {
    const pack = this.packMenu()?.pack;
    this.packMenu.set(null);
    if (!pack) {
      return;
    }

    switch (action) {
      case 'edit':
        this.openEditPack();
        break;
      case 'import':
        this.pickFiles();
        break;
      case 'export':
        void this.exportPack(pack);
        break;
      case 'delete':
        this.requestDeletePack(pack);
        break;
    }
  }

  protected openIconMenu(state: IconContextMenuState): void {
    this.iconMenu.set(state);
  }

  protected async onIconMenuAction(action: string): Promise<void> {
    const icon = this.iconMenu()?.icon;
    this.iconMenu.set(null);
    if (!icon) {
      return;
    }

    switch (action) {
      case 'rename':
        this.renameValue.set(icon.name);
        this.iconPendingRename.set(icon);
        break;
      case 'copy-id':
        await this.copyIconId(icon);
        break;
      case 'delete': {
        const selection = this.selectedIconIds();
        if (selection.has(icon.id) && selection.size > 1) {
          this.requestDeleteSelection();
        } else {
          this.iconsPendingDeletion.set([icon]);
        }

        break;
      }
    }
  }

  private async copyIconId(icon: IconModel): Promise<void> {
    const result = await this.clipboard.copyText(icon.id);
    if (result.status === 'copied') {
      this.toasts.show(this.localization.translateKey(AppStrings.IconPacks.IconIdCopied), { detail: icon.id });
      return;
    }

    this.manualCopy.set({
      value: icon.id,
      message: `${clipboardFailureDetail(result.reason)} ${this.localization.translateKey(AppStrings.IconPacks.CopyManualInstruction)}`,
    });
  }

  protected confirmRename(): void {
    const icon = this.iconPendingRename();
    const name = this.renameValue().trim();
    dismissModal(this.renameModal, () => {
      this.iconPendingRename.set(null);
      if (icon && name && name !== icon.name) {
        void this.iconPacks.renameIcon(icon.id, icon.packId, name);
      }
    });
  }

  protected cancelRename(): void {
    dismissModal(this.renameModal, () => this.iconPendingRename.set(null));
  }

  protected openImportMenu(event: MouseEvent): void {
    const trigger = event.currentTarget as HTMLElement;
    const rect = trigger.getBoundingClientRect();
    this.importMenu.set({ x: rect.left, y: rect.bottom + 4 });
  }

  protected onImportMenuAction(action: string): void {
    this.importMenu.set(null);
    switch (action) {
      case 'files':
        this.pickFiles();
        break;
      case 'folder':
        this.folderInput?.nativeElement.click();
        break;
      case 'path':
        this.showPathBrowser.set(true);
        break;
    }
  }

  protected pickFiles(): void {
    this.fileInput?.nativeElement.click();
  }

  protected async onFilesPicked(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.importFiles(files);
  }

  protected async importFiles(files: File[]): Promise<void> {
    const pack = this.selectedPack();
    if (!pack) {
      return;
    }

    await this.importFilesInto(pack, files);
  }

  protected async importFilesInto(pack: IconPackModel, files: File[]): Promise<void> {
    if (pack.isReadOnly || files.length === 0) {
      return;
    }

    await this.iconPacks.import(pack.id, files);
  }

  protected openAddPackMenu(event: MouseEvent): void {
    const trigger = event.currentTarget as HTMLElement;
    const rect = trigger.getBoundingClientRect();
    this.addPackMenu.set({ x: rect.left, y: rect.bottom + 4 });
  }

  protected onAddPackMenuAction(action: string): void {
    this.addPackMenu.set(null);
    switch (action) {
      case 'create':
        this.openCreatePack();
        break;
      case 'import':
        this.pickPackFiles();
        break;
    }
  }

  protected pickPackFiles(): void {
    this.packInput?.nativeElement.click();
  }

  protected async onPacksPicked(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (files.length > 0) {
      await this.iconPacks.importPacks(files);
    }
  }

  protected async onIconPathsDropped(paths: string[]): Promise<void> {
    const archives = paths.filter(path => archiveDropKind(path) === 'iconPack');
    const rest = paths.filter(path => archiveDropKind(path) !== 'iconPack');

    for (const archive of archives) {
      await this.restoreArchive(archive);
    }

    const pack = this.selectedPack();
    if (pack && !pack.isReadOnly && rest.length > 0) {
      await this.iconPacks.importFromPath(pack.id, rest);
    }
  }

  protected async onIconPathsDroppedInto(pack: IconPackModel, paths: string[]): Promise<void> {
    if (pack.isReadOnly || paths.length === 0) {
      return;
    }

    await this.iconPacks.importFromPath(pack.id, paths);
  }

  private async restoreArchive(path: string): Promise<void> {
    const name = await this.iconPacks.restoreFromPath(path);
    if (name) {
      this.toasts.show(this.localization.translateKey(AppStrings.IconPacks.PackImported), { detail: name });
    } else {
      this.toasts.show(this.localization.translateKey(AppStrings.IconPacks.PackImportFailed), { variant: 'error' });
    }
  }

  protected async onPathPicked(path: string): Promise<void> {
    this.showPathBrowser.set(false);
    const pack = this.selectedPack();
    if (pack && !pack.isReadOnly && path) {
      await this.iconPacks.importFromPath(pack.id, [path]);
    }
  }
}
