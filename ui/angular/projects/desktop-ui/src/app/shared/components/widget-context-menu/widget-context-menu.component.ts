import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { ContextMenuComponent, ContextMenuItem } from '../overlay/context-menu/context-menu.component';
import { AppStrings, PinScope } from '@macro-deck/runtime';
import { LocalizationService } from '../../localization';

export type WidgetContextMenuMode = 'widget' | 'empty';

export type WidgetContextMenuAction =
  'edit' | 'copy' | 'cut' | 'delete' | 'paste' | 'export' | 'import' | 'pin-profile' | 'pin-subtree' | 'unpin';

@Component({
  selector: 'shared-widget-context-menu',
  standalone: true,
  imports: [ContextMenuComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-context-menu
      [isOpen]="isOpen()"
      [x]="x()"
      [y]="y()"
      [items]="menuItems()"
      (itemClick)="onItemClick($event)"
      (closed)="closed.emit()">
    </shared-context-menu>
  `,
})
export class WidgetContextMenuComponent {
  readonly isOpen = input(false);
  readonly x = input(0);
  readonly y = input(0);
  readonly mode = input<WidgetContextMenuMode>('widget');
  readonly canPaste = input(false);
  readonly isPinned = input(false);
  readonly pinScope = input<PinScope | undefined>(undefined);
  readonly selectionCount = input(1);

  readonly action = output<WidgetContextMenuAction>();
  readonly closed = output<void>();

  private readonly localization = inject(LocalizationService);

  readonly menuItems = computed<ContextMenuItem[]>(() => {
    const t = (key: string, args?: Record<string, unknown>) => this.localization.translateKey(key, args);

    if (this.mode() === 'empty') {
      return [
        { id: 'paste', label: t(AppStrings.Widgets.ContextMenu.Paste), icon: 'icon-clipboard', disabled: !this.canPaste(), dividerAfter: true },
        { id: 'import', label: t(AppStrings.Widgets.ContextMenu.ImportWidgets), icon: 'icon-download' },
      ];
    }

    const pinned = this.isPinned();
    const scope = this.pinScope() ?? 'Profile';
    const count = this.selectionCount();

    if (count > 1) {
      const batchPinItem: ContextMenuItem = {
        id: 'pin',
        label: t(AppStrings.Widgets.ContextMenu.Pin),
        icon: 'icon-pin',
        dividerAfter: !pinned,
        children: [
          { id: 'pin-profile', label: t(AppStrings.Widgets.ContextMenu.EveryFolderInProfile) },
          { id: 'pin-subtree', label: t(AppStrings.Widgets.ContextMenu.FolderAndSubfolders) },
        ],
      };

      return [
        { id: 'copy', label: t(AppStrings.Widgets.ContextMenu.CopyCount, { count }), icon: 'icon-copy' },
        { id: 'cut', label: t(AppStrings.Widgets.ContextMenu.Cut), icon: 'icon-scissors' },
        batchPinItem,
        ...(pinned ? [{ id: 'unpin', label: t(AppStrings.Widgets.ContextMenu.UnpinWidgets), icon: 'icon-pin-off', dividerAfter: true } as ContextMenuItem] : []),
        { id: 'delete', label: t(AppStrings.Widgets.ContextMenu.DeleteCount, { count }), icon: 'icon-trash', danger: true },
      ];
    }

    const pinItem: ContextMenuItem = {
      id: 'pin',
      label: pinned ? t(AppStrings.Widgets.ContextMenu.Pinned) : t(AppStrings.Widgets.ContextMenu.PinWidget),
      icon: 'icon-pin',
      dividerAfter: !pinned,
      children: [
        { id: 'pin-profile', label: t(AppStrings.Widgets.ContextMenu.EveryFolderInProfile), active: pinned && scope === 'Profile' },
        { id: 'pin-subtree', label: t(AppStrings.Widgets.ContextMenu.FolderAndSubfolders), active: pinned && scope === 'Subtree' },
      ],
    };

    return [
      { id: 'edit', label: t(AppStrings.Widgets.ContextMenu.Edit), icon: 'icon-pencil' },
      { id: 'copy', label: t(AppStrings.Widgets.ContextMenu.Copy), icon: 'icon-copy' },
      ...(pinned ? [] : [{ id: 'cut', label: t(AppStrings.Widgets.ContextMenu.Cut), icon: 'icon-scissors' } as ContextMenuItem]),
      pinItem,
      ...(pinned ? [{ id: 'unpin', label: t(AppStrings.Widgets.ContextMenu.UnpinWidget), icon: 'icon-pin-off', dividerAfter: true } as ContextMenuItem] : []),
      { id: 'export', label: t(AppStrings.Widgets.ContextMenu.Export), icon: 'icon-upload', dividerAfter: true },
      { id: 'delete', label: t(AppStrings.Widgets.ContextMenu.Delete), icon: 'icon-trash', danger: true },
    ];
  });

  onItemClick(itemId: string): void {
    this.action.emit(itemId as WidgetContextMenuAction);
  }
}
