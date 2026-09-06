import {
  AfterViewInit, Component, ElementRef, EventEmitter, OnDestroy, Output, ViewChild, computed, effect, inject,
  signal, ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { FileOpenService, FolderService } from '../../../services';
import { Folder, FolderDropPosition, FolderMoveDirection, FolderMoveRequest, AppStrings, WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ModalComponent, InputComponent, ProfileService, dismissModal, ErrorBannerComponent, ToastService, LocalizationService, TranslatePipe, FolderViewService } from '@shared';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { ArchivePreviewModalComponent } from '../../portable/archive-preview-modal/archive-preview-modal.component';
import { ExportOptionsModalComponent, ExportOptionToggle, EXPORT_ICONS_TOGGLE, EXPORT_SUBFOLDERS_TOGGLE } from '../../portable/export-options-modal/export-options-modal.component';
import { ImportPasswordModalComponent } from '../../portable/import-password-modal/import-password-modal.component';
import { ShellDrop, ShellDropTargetDirective } from '../../shell-drop/shell-drop-target.directive';
import { ArchiveDropKind } from '../../../domain/archive-drop.util';
import { ArchiveImportSession } from '../../../domain/archive-import-session';
import { savedFileDetail } from '../../../services/file-save.service';
import { FolderDragService } from '../../../services/folder-drag.service';
import { FolderFocusRuleService } from '../../../services/folder-focus-rule.service';
import { PortabilityService, PortableExportOptions } from '../../../services/portability.service';
import { FolderItemComponent } from './folder-item/folder-item.component';
import { FolderContextMenuComponent, type FolderContextMenuAction } from './folder-context-menu/folder-context-menu.component';
import { FolderFocusRuleModalComponent } from '../folder-focus-rule-modal/folder-focus-rule-modal.component';
import { FolderViewPickerComponent } from '../folder-view-picker/folder-view-picker.component';

type FolderNavigateDirection = 'up' | 'down' | 'left' | 'right' | 'home' | 'end';

@Component({
  selector: 'app-folder-navigation',
  standalone: true,
  imports: [CommonModule, FormsModule, FolderItemComponent, FolderContextMenuComponent, ConfirmationModalComponent, ButtonComponent, ButtonGroupComponent, ModalComponent, InputComponent, ErrorBannerComponent, ShellDropTargetDirective, ArchivePreviewModalComponent, ExportOptionsModalComponent, ImportPasswordModalComponent, FolderFocusRuleModalComponent, TranslatePipe, FolderViewPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [FolderDragService],
  templateUrl: './folder-navigation.component.html',
  styleUrls: ['./folder-navigation.component.scss']
})
export class FolderNavigationComponent implements AfterViewInit, OnDestroy {
  protected readonly folderService = inject(FolderService);
  protected readonly profileService = inject(ProfileService);
  protected readonly folderDrag = inject(FolderDragService);
  private readonly localization = inject(LocalizationService);
  private readonly portability = inject(PortabilityService);
  private readonly fileOpen = inject(FileOpenService);
  private readonly toasts = inject(ToastService);
  private readonly focusRules = inject(FolderFocusRuleService);
  private readonly folderViews = inject(FolderViewService);

  @ViewChild('folderList') private folderListRef?: ElementRef<HTMLElement>;
  private readonly droppedSub: Subscription;

  isRenaming = false;
  isCreating = false;
  renameValue = '';
  renamingFolderId: string | null = null;
  createParentId: string | null = null;

  readonly createViewId = signal<string>(WIDGET_GRID_VIEW_ID);

  readonly createViewConfiguration = signal<string | null>(null);

  @Output() readonly changeView = new EventEmitter<string>();

  protected readonly canChangeView = computed(() =>
    this.folderViews.folderViews().length > 1 && this.folderService.customFolderViewsSupported());

  protected readonly folderToDelete = signal<string | null>(null);

  protected readonly deleteMessage = computed(() => {
    const folderId = this.folderToDelete();
    if (!folderId) return '';

    const scope = this.folderService.deletionScope(folderId);
    if (!scope) {
      return this.localization.translateKey(AppStrings.Widgets.Folder.DeleteConfirmGeneric);
    }

    const parts: string[] = [];
    if (scope.subfolderCount > 0) {
      parts.push(this.localization.translateKey(AppStrings.Widgets.Folder.SubfolderCount, { count: scope.subfolderCount }));
    }
    if (scope.widgetCount > 0) {
      parts.push(this.localization.translateKey(AppStrings.Widgets.Folder.WidgetCount, { count: scope.widgetCount }));
    }

    let message: string;
    if (parts.length === 0) {
      message = this.localization.translateKey(AppStrings.Widgets.Folder.DeleteConfirmNamed, { name: scope.name });
    } else if (parts.length === 1) {
      message = this.localization.translateKey(
        AppStrings.Widgets.Folder.DeleteConfirmNamedOnePart, { name: scope.name, part: parts[0] });
    } else {
      message = this.localization.translateKey(
        AppStrings.Widgets.Folder.DeleteConfirmNamedTwoParts, { name: scope.name, part1: parts[0], part2: parts[1] });
    }

    const pinnedClause = scope.pinnedCount > 0
      ? ' ' + this.localization.translateKey(AppStrings.Widgets.Folder.PinnedClause, { count: scope.pinnedCount })
      : '';

    return message + pinnedClause + ' ' + this.localization.translateKey(AppStrings.Widgets.Folder.CannotBeUndone);
  });

  protected readonly exportFolderId = signal<string | null>(null);
  protected readonly focusRuleFolderId = signal<string | null>(null);

  protected readonly archiveImport = new ArchiveImportSession(
    this.portability,
    (source, password) =>
      this.portability.importFolder(this.profileService.selectedProfileId()!, this.importParentId, source, password),
    outcome => this.onFolderImported(outcome.folderId, outcome.count ?? 1)
  );

  private readonly exportError = signal<string | null>(null);

  protected readonly portableError = computed(() => this.exportError() ?? this.archiveImport.error());

  private importParentId: string | null = null;

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChild('folderImportInput') private folderImportInput?: ElementRef<HTMLInputElement>;

  constructor() {
    this.droppedSub = this.folderDrag.dropped.subscribe(request => void this.executeMove(request));
  }

  ngAfterViewInit(): void {
    if (this.folderListRef) {
      this.folderDrag.attach(this.folderListRef.nativeElement);
    }
  }

  ngOnDestroy(): void {
    this.droppedSub.unsubscribe();
  }

  trackByFolderId(index: number, folder: Folder): string {
    return folder.id;
  }

  onContainerClick(): void {
    this.folderService.closeContextMenu();
  }

  canDeleteSelectedFolder(): boolean {
    const folderId = this.folderService.contextMenu().folderId;
    if (!folderId) return false;
    return this.folderService.canDeleteFolder(folderId);
  }

  canSetContextFolderStart(): boolean {
    const folderId = this.folderService.contextMenu().folderId;
    if (!folderId) return false;
    const folder = this.folderService.getFolderById(folderId);
    return !!folder && folder.parentId === null && !folder.isDefault;
  }

  hasChildren(folderId: string): boolean {
    return this.folderService.getChildFolders(folderId).length > 0;
  }

  focusRuleCount(): number {
    const folderId = this.folderService.contextMenu().folderId;
    return folderId ? this.focusRules.rulesForFolder(folderId).length : 0;
  }

  getDropPosition(folderId: string): FolderDropPosition | null {
    const state = this.folderDrag.state();
    return state.targetFolderId === folderId ? state.dropPosition : null;
  }

  onContextMenu(event: { folderId: string; x: number; y: number }): void {
    if (this.profileService.isCurrentProfileLocked()) {
      return;
    }
    this.folderService.openContextMenu(event.folderId, event.x, event.y);
  }

  canMoveUp(): boolean {
    return this.canResolveMove('up');
  }

  canMoveDown(): boolean {
    return this.canResolveMove('down');
  }

  canMoveInto(): boolean {
    return this.canResolveMove('into');
  }

  canMoveToParent(): boolean {
    return this.canResolveMove('out');
  }

  private canResolveMove(direction: FolderMoveDirection): boolean {
    const folderId = this.folderService.contextMenu().folderId;
    return !!folderId && this.folderService.resolveMove(folderId, direction) !== null;
  }

  onMove(event: { folderId: string; direction: FolderMoveDirection }): void {
    void this.performMove(event.folderId, event.direction);
  }

  private async performMove(folderId: string, direction: FolderMoveDirection): Promise<void> {
    const request = this.folderService.resolveMove(folderId, direction);
    if (request) await this.executeMove(request);
  }

  onNavigate(event: { folderId: string; direction: FolderNavigateDirection }): void {
    const rows = this.visibleRows();
    const index = rows.findIndex(row => row.dataset['folderId'] === event.folderId);
    if (index < 0) return;

    switch (event.direction) {
      case 'up':
        this.focusRow(rows[index - 1]);
        return;
      case 'down':
        this.focusRow(rows[index + 1]);
        return;
      case 'home':
        this.focusRow(rows[0]);
        return;
      case 'end':
        this.focusRow(rows[rows.length - 1]);
        return;
      case 'right': {
        const folder = this.folderService.getFolderById(event.folderId);
        if (folder && this.hasChildren(event.folderId) && !folder.isExpanded) {
          this.folderService.toggleFolderExpanded(event.folderId);
        } else {
          this.focusRow(rows[index + 1]);
        }
        return;
      }
      case 'left': {
        const folder = this.folderService.getFolderById(event.folderId);
        if (folder?.isExpanded && this.hasChildren(event.folderId)) {
          this.folderService.toggleFolderExpanded(event.folderId);
        } else if (folder?.parentId) {
          this.focusRow(rows.find(row => row.dataset['folderId'] === folder.parentId));
        }
        return;
      }
    }
  }

  private async executeMove(request: FolderMoveRequest): Promise<void> {
    const result = await this.folderService.moveFolder(request);
    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Widgets.Folder.MoveFailed), { variant: 'error' });
    }
  }

  private visibleRows(): HTMLElement[] {
    const listEl = this.folderListRef?.nativeElement;
    if (!listEl) return [];
    return Array.from(listEl.querySelectorAll<HTMLElement>('.folder-item[data-folder-id]'));
  }

  private focusRow(row: HTMLElement | undefined): void {
    row?.focus();
  }

  async onContextMenuAction(action: FolderContextMenuAction): Promise<void> {
    const folderId = this.folderService.contextMenu().folderId;
    if (!folderId) return;

    switch (action) {
      case 'new-folder':
        this.startCreateFolder(folderId);
        break;
      case 'import-inside':
        this.startImport(folderId);
        break;
      case 'export':
        this.startExport(folderId);
        break;
      case 'focus-rule':
        this.focusRuleFolderId.set(folderId);
        break;
      case 'change-view':
        this.changeView.emit(folderId);
        break;
      case 'move-up':
        await this.performMove(folderId, 'up');
        break;
      case 'move-down':
        await this.performMove(folderId, 'down');
        break;
      case 'move-into':
        await this.performMove(folderId, 'into');
        break;
      case 'move-to-parent':
        await this.performMove(folderId, 'out');
        break;
      case 'set-start':
        await this.folderService.setStartFolder(folderId);
        break;
      case 'rename':
        this.startRename(folderId);
        break;
      case 'duplicate':
        const duplicateResult = await this.folderService.duplicateFolder(folderId);
        if (!duplicateResult.success) {
          console.error('Failed to duplicate folder:', duplicateResult.error?.message);
        }
        break;
      case 'delete':
        this.folderToDelete.set(folderId);
        break;
    }
  }

  async confirmFolderDelete(): Promise<void> {
    const folderId = this.folderToDelete();
    if (!folderId) return;

    const result = await this.folderService.deleteFolder(folderId);
    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Widgets.Folder.DeleteFailed), { variant: 'error' });
    }

    this.cancelFolderDelete();
  }

  cancelFolderDelete(): void {
    this.folderToDelete.set(null);
  }

  createRootFolder(): void {
    this.startCreateFolder(null);
  }

  startCreateFolder(parentId: string | null): void {
    this.createParentId = parentId;
    this.renameValue = '';
    this.isCreating = true;
  }

  async confirmCreate(): Promise<void> {
    const name = this.renameValue.trim() || this.localization.translateKey(AppStrings.Widgets.Folder.NewFolder);
    const result = await this.folderService.createFolder(name,
      this.createParentId,
      this.createViewId(),
      this.createViewConfiguration());

    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.CreateFailed),
        { variant: 'error' });
    }

    this.cancelCreate();
  }

  cancelCreate(): void {
    dismissModal(this.modal, () => {
      this.isCreating = false;
      this.createParentId = null;
      this.renameValue = '';
      this.createViewId.set(WIDGET_GRID_VIEW_ID);
      this.createViewConfiguration.set(null);
    });
  }

  startRename(folderId: string): void {
    const folder = this.folderService.getFolderById(folderId);
    if (folder) {
      this.renamingFolderId = folderId;
      this.renameValue = folder.name;
      this.isRenaming = true;
    }
  }

  async confirmRename(): Promise<void> {
    if (this.renamingFolderId && this.renameValue.trim()) {
      const result = await this.folderService.renameFolder(this.renamingFolderId, this.renameValue.trim());

      if (!result.success) {
        this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.RenameFailed),
          { variant: 'error' });
      }
    }
    this.cancelRename();
  }

  cancelRename(): void {
    dismissModal(this.modal, () => {
      this.isRenaming = false;
      this.renamingFolderId = null;
      this.renameValue = '';
    });
  }

  startExport(folderId: string): void {
    this.exportError.set(null);
    this.exportFolderId.set(folderId);
  }

  exportToggles(): ExportOptionToggle[] {
    const folderId = this.exportFolderId();
    const subfolders = !!folderId && this.hasChildren(folderId);
    return subfolders ? [EXPORT_SUBFOLDERS_TOGGLE, EXPORT_ICONS_TOGGLE] : [EXPORT_ICONS_TOGGLE];
  }

  async onExportConfirmed(options: PortableExportOptions): Promise<void> {
    const folderId = this.exportFolderId();
    this.exportFolderId.set(null);
    if (!folderId) {
      return;
    }

    const result = await this.portability.exportFolder(folderId, options);
    if (!result.ok) {
      this.exportError.set(result.error ?? this.localization.translateKey(AppStrings.Widgets.Folder.ExportFailed));
      return;
    }
    if (!result.canceled) {
      this.toasts.show(this.localization.translateKey(AppStrings.Widgets.Folder.FolderExported), { detail: savedFileDetail(result) });
    }
  }

  cancelExport(): void {
    this.exportFolderId.set(null);
  }

  startImport(parentId: string | null): void {
    this.archiveImport.dismissError();
    this.importParentId = parentId;
    this.folderImportInput?.nativeElement.click();
  }

  protected readonly folderDropKinds: readonly ArchiveDropKind[] = ['folder'];

  private readonly claimOpenedFolder = effect(() => {
    if (!this.fileOpen.pending().some(entry => entry.kind === 'folder')) {
      return;
    }
    if (!this.profileService.selectedProfileId()) {
      return;
    }

    const opened = this.fileOpen.claim('folder');
    if (opened) {
      this.importParentId = null;
      void this.archiveImport.beginPath(opened.path);
    }
  });

  async onArchiveDropped(drop: ShellDrop): Promise<void> {
    if (!this.profileService.selectedProfileId()) {
      return;
    }

    this.archiveImport.dismissError();
    this.importParentId = null;
    await this.archiveImport.beginPath(drop.path);
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (file && this.profileService.selectedProfileId()) {
      await this.archiveImport.beginFile(file);
    }
  }

  dismissPortableError(): void {
    this.exportError.set(null);
    this.archiveImport.dismissError();
  }

  private onFolderImported(folderId: string | undefined, count: number): void {
    if (this.importParentId) {
      this.folderService.expandFolder(this.importParentId);
    }
    if (folderId) {
      this.folderService.selectFolder(folderId);
    }
    this.toasts.show(this.localization.translateKey(AppStrings.Widgets.Folder.FoldersImportedCount, { count }));
  }
}
