import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Input,
  ViewChild,
  ViewEncapsulation,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { ContextMenuComponent, ContextMenuItem, InputComponent, LocalizationService, OverlayPanelComponent, ToggleSwitchComponent } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { TooltipDirective } from '../../../overlay/tooltip/tooltip.directive';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ActionDragService } from '../../services/action-drag.service';

@Component({
  selector: 'shared-action-card-frame',
  standalone: true,
  imports: [ToggleSwitchComponent, ContextMenuComponent, OverlayPanelComponent, TooltipDirective, InputComponent, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './action-card-frame.component.html',
  styleUrls: ['./action-card-frame.component.scss', '../action-colors.scss'],
})
export class ActionCardFrameComponent {
  @Input({ required: true }) block!: ActionBlock;
  @Input() summary = '';
  @Input() collapsible = true;

  protected readonly store = inject(ActionFlowStore);
  private readonly drag = inject(ActionDragService, { optional: true });
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly localization = inject(LocalizationService);

  protected readonly enableToggleLabel = computed(() => this.localization.translateKey(
    this.block.disabled ? AppStrings.ActionBuilder.Frame.EnableAction : AppStrings.ActionBuilder.Frame.DisableAction,
  ));
  protected readonly enableToggleTitle = computed(() => this.localization.translateKey(
    this.block.disabled ? AppStrings.ActionBuilder.Frame.SkippedTooltip : AppStrings.ActionBuilder.Frame.SkipTooltip,
  ));
  protected readonly offTagLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Frame.Off));
  protected readonly incompleteConfigLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Frame.IncompleteConfig));
  protected readonly deleteActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Frame.DeleteAction));
  protected readonly commentPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Frame.CommentPlaceholder));
  protected readonly commentFieldAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Frame.CommentFieldAriaLabel));

  protected readonly menu = signal<{ isOpen: boolean; x: number; y: number }>({
    isOpen: false,
    x: 0,
    y: 0,
  });

  @ViewChild('commentButton') private commentButtonRef?: ElementRef<HTMLButtonElement>;

  protected readonly commentOpen = signal(false);
  protected readonly commentDraft = signal('');

  protected readonly menuItems = computed<ContextMenuItem[]>(() => {
    const t = (key: string) => this.localization.translateKey(key);
    const F = AppStrings.ActionBuilder.Frame;
    return [
      { id: 'copy', label: t('macrodeck:Common.Copy'), icon: 'icon-copy' },
      this.store.isCutSource(this.block.id)
        ? { id: 'cancel-cut', label: t(F.MenuCancelMove), icon: 'icon-x' }
        : { id: 'cut', label: t(F.MenuCut), icon: 'icon-scissors' },
      { id: 'duplicate', label: t(F.MenuDuplicate), icon: 'icon-copy' },
      {
        id: 'paste',
        label: t(F.MenuPasteAfter),
        icon: 'icon-clipboard',
        disabled: !this.store.canPaste(),
        dividerAfter: true,
      },
      { id: 'delete', label: t('macrodeck:Common.Delete'), icon: 'icon-trash', danger: true },
    ];
  });

  get colorClass(): string {
    return `action-color-${this.block.type}`;
  }

  get isCutSource(): boolean {
    return this.store.isCutSource(this.block.id);
  }

  get expanded(): boolean {
    return !this.collapsible || this.store.isExpanded(this.block.id);
  }

  get hasErrors(): boolean {
    return this.store.errorsFor(this.block.id).length > 0;
  }

  get comment(): string {
    return this.block.comment ?? '';
  }

  get hasComment(): boolean {
    return this.comment.trim().length > 0;
  }

  get commentTooltip(): string {
    return this.hasComment ? this.comment : this.localization.translateKey(AppStrings.ActionBuilder.Frame.AddComment);
  }

  get commentAriaLabel(): string {
    return this.hasComment
      ? this.localization.translateKey(AppStrings.ActionBuilder.Frame.EditCommentAriaLabel, { comment: this.comment })
      : this.localization.translateKey(AppStrings.ActionBuilder.Frame.AddComment);
  }

  onHeaderPress(event: PointerEvent): void {
    this.drag?.press(event, this.block, this.host.nativeElement);
  }

  toggleComment(event: Event): void {
    event.stopPropagation();
    if (this.commentOpen()) {
      this.closeComment();
      return;
    }
    this.commentDraft.set(this.comment);
    this.commentOpen.set(true);
  }

  closeComment(): void {
    if (!this.commentOpen()) return;
    this.commentOpen.set(false);
    this.commentButtonRef?.nativeElement.focus();
  }

  onCommentInput(value: string | number): void {
    this.store.updateBlockComment(this.block.id, String(value));
  }

  toggle(): void {
    // A drag that started on this header must not also toggle the card.
    if (this.drag?.consumeClickSuppression()) return;
    if (!this.collapsible) return;
    this.store.toggleExpanded(this.block.id);
  }

  onHeaderContextMenu(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.menu.set({ isOpen: true, x: event.clientX, y: event.clientY });
  }

  closeMenu(): void {
    this.menu.update(menu => ({ ...menu, isOpen: false }));
  }

  onMenuAction(action: string): void {
    switch (action) {
      case 'copy':
        this.store.copyBlock(this.block.id);
        break;
      case 'cut':
        this.store.cutBlock(this.block.id);
        break;
      case 'cancel-cut':
        this.store.cancelCut();
        break;
      case 'duplicate':
        void this.store.duplicateBlock(this.block.id);
        break;
      case 'paste':
        void this.store.pasteAfter(this.block.id);
        break;
      case 'delete':
        this.store.removeBlock(this.block.id);
        break;
    }
  }

  onHeaderKeydown(event: KeyboardEvent): void {
    if (event.target !== event.currentTarget) return;

    if (event.ctrlKey || event.metaKey) {
      const key = event.key.toLowerCase();
      if (key === 'c') {
        event.preventDefault();
        this.store.copyBlock(this.block.id);
      } else if (key === 'x') {
        event.preventDefault();
        if (this.isCutSource) this.store.cancelCut();
        else this.store.cutBlock(this.block.id);
      } else if (key === 'v') {
        event.preventDefault();
        void this.store.pasteAfter(this.block.id);
      }
      return;
    }

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (this.collapsible) this.store.toggleExpanded(this.block.id);
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      this.store.removeBlock(this.block.id);
    }
  }
}
