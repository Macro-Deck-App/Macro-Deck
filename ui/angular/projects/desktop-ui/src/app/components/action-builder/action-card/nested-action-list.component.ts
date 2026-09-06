import {
  ChangeDetectionStrategy,
  Component,
  Input,
  ViewEncapsulation,
  computed,
  forwardRef,
  inject,
  signal,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { ContextMenuComponent, ContextMenuItem, LocalizationService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { offersPasteMenu } from '../paste-target.util';
import { ActionFlowStore } from '../services/action-flow.store';
import { ActionDragService } from '../services/action-drag.service';
import { ActionCardComponent } from './action-card.component';

@Component({
  selector: 'shared-action-list',
  standalone: true,
  imports: [forwardRef(() => ActionCardComponent), ContextMenuComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './nested-action-list.component.html',
  styleUrls: ['./nested-action-list.component.scss'],
})
export class NestedActionListComponent {
  @Input({ required: true }) listId!: string;
  @Input({ required: true }) items: ActionBlock[] = [];
  @Input() variant: 'root' | 'nested' = 'nested';

  protected readonly store = inject(ActionFlowStore);
  protected readonly drag = inject(ActionDragService);
  private readonly localization = inject(LocalizationService);

  protected readonly dragHandleAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.DragToReorder));
  protected readonly addActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.AddAction));

  protected readonly menu = signal<{ isOpen: boolean; x: number; y: number }>({
    isOpen: false,
    x: 0,
    y: 0,
  });

  protected readonly menuItems = computed<ContextMenuItem[]>(() => [
    { id: 'paste', label: this.localization.translateKey(AppStrings.ActionBuilder.Paste), icon: 'icon-clipboard', disabled: !this.store.canPaste() },
  ]);

  onHandlePress(event: PointerEvent, block: ActionBlock, handle: HTMLElement): void {
    this.drag.press(event, block, handle);
  }

  onContextMenu(event: MouseEvent): void {
    if (!offersPasteMenu(event)) return;
    event.preventDefault();
    event.stopPropagation();
    this.menu.set({ isOpen: true, x: event.clientX, y: event.clientY });
  }

  closeMenu(): void {
    this.menu.update(menu => ({ ...menu, isOpen: false }));
  }

  onMenuAction(action: string): void {
    if (action === 'paste') void this.store.pasteIntoList(this.listId);
  }

  slotAt(i: number): boolean {
    const slot = this.drag.activeSlot();
    return slot !== null && slot.listId === this.listId && slot.index === i;
  }

  isDropTarget(): boolean {
    return this.drag.activeSlot()?.listId === this.listId;
  }
}
