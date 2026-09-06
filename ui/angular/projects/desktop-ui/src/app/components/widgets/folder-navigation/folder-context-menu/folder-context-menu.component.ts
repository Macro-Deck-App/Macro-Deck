import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy, inject, input, computed } from '@angular/core';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { ContextMenuComponent, ContextMenuItem, LocalizationService } from '@shared';

export type FolderContextMenuAction =
  | 'new-folder'
  | 'import-inside'
  | 'move-up'
  | 'move-down'
  | 'move-into'
  | 'move-to-parent'
  | 'set-start'
  | 'rename'
  | 'change-view'
  | 'duplicate'
  | 'export'
  | 'focus-rule'
  | 'delete';

@Component({
  selector: 'app-folder-context-menu',
  standalone: true,
  imports: [ContextMenuComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-context-menu
      [isOpen]="isOpen"
      [x]="x"
      [y]="y"
      [items]="menuItems()"
      (itemClick)="onItemClick($event)"
      (closed)="close.emit()">
    </shared-context-menu>
  `
})
export class FolderContextMenuComponent {
  private readonly localization = inject(LocalizationService);

  @Input() isOpen = false;
  @Input() x = 0;
  @Input() y = 0;
  
  canDelete = input<boolean>(true);
  canSetStart = input<boolean>(false);
  canMoveUp = input<boolean>(false);
  canMoveDown = input<boolean>(false);
  canMoveInto = input<boolean>(false);
  canMoveToParent = input<boolean>(false);

  focusRuleCount = input<number>(0);

  canChangeView = input<boolean>(false);

  @Output() action = new EventEmitter<FolderContextMenuAction>();
  @Output() close = new EventEmitter<void>();

  menuItems = computed<ContextMenuItem[]>(() => [
    { id: 'new-folder', label: this.t(AppStrings.Widgets.Folder.NewFolderInside), icon: 'icon-folder-plus' },
    { id: 'import-inside', label: this.t(AppStrings.Widgets.Folder.ImportFolderInside), icon: 'icon-download', dividerAfter: true },
    { id: 'move-up', label: this.t(AppStrings.Widgets.Folder.MoveUp), icon: 'icon-arrow-up', disabled: !this.canMoveUp() },
    { id: 'move-down', label: this.t(AppStrings.Widgets.Folder.MoveDown), icon: 'icon-arrow-down', disabled: !this.canMoveDown() },
    { id: 'move-into', label: this.t(AppStrings.Widgets.Folder.MoveInto), icon: 'icon-arrow-right', disabled: !this.canMoveInto() },
    {
      id: 'move-to-parent', label: this.t(AppStrings.Widgets.Folder.MoveOutToParent), icon: 'icon-arrow-left',
      disabled: !this.canMoveToParent(), dividerAfter: true
    },
    ...(this.canSetStart()
      ? [{ id: 'set-start', label: this.t(AppStrings.Widgets.Folder.SetAsStartFolder), icon: 'icon-star', dividerAfter: true } as ContextMenuItem]
      : []),
    { id: 'rename', label: this.t(Strings.Common.Rename), icon: 'icon-pencil' },
    ...(this.canChangeView()
      ? [{ id: 'change-view', label: this.t(AppStrings.Widgets.Folder.ChangeView), icon: 'icon-grid' } as ContextMenuItem]
      : []),
    { id: 'duplicate', label: this.t(AppStrings.Widgets.Folder.Duplicate), icon: 'icon-copy' },
    { id: 'export', label: this.t(Strings.Common.Export), icon: 'icon-upload' },
    { id: 'focus-rule', label: this.focusRuleLabel(), icon: 'icon-zap', dividerAfter: true },
    { id: 'delete', label: this.t(Strings.Common.Delete), icon: 'icon-trash', danger: true, disabled: !this.canDelete() }
  ]);

  private t(key: string): string {
    return this.localization.translateKey(key);
  }

  private focusRuleLabel(): string {
    const count = this.focusRuleCount();
    return count > 0
      ? this.localization.translateKey(AppStrings.Widgets.Folder.AutomaticActivationCount, { count })
      : this.t(AppStrings.Widgets.Folder.AutomaticActivation);
  }

  onItemClick(itemId: string): void {
    this.action.emit(itemId as FolderContextMenuAction);
  }
}
