import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { IconImageService, LocalizationService } from '@shared';
import { IconModel } from '../../services/icon-pack.service';

export interface IconTileClick {
  icon: IconModel;
  toggle: boolean;
  range: boolean;
}

@Component({
  selector: 'shared-icon-tile',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon-tile.component.html',
  styleUrls: ['./icon-tile.component.scss']
})
export class IconTileComponent {
  private readonly iconImage = inject(IconImageService);
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) icon!: IconModel;
  @Input() size = 96;
  @Input() selected = false;
  @Input() multiSelect = false;
  @Input() selectionActive = false;

  @Output() tileClick = new EventEmitter<IconTileClick>();
  @Output() tileContextMenu = new EventEmitter<{ icon: IconModel; x: number; y: number }>();

  readonly animatedIconLabel = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Tile.AnimatedIcon));
  readonly processingLabel = computed(() =>
    this.localization.translateKey(AppStrings.IconPacks.Tile.Processing));

  get imageUrl(): string | null {
    return this.iconImage.getIconUrl(this.icon.id, 128);
  }

  get failedTooltip(): string {
    return this.icon.processingError
      ? this.localization.translateKey(AppStrings.IconPacks.Tile.ImportFailedWithReason, { reason: this.icon.processingError })
      : this.localization.translateKey(AppStrings.IconPacks.Tile.ImportFailed);
  }

  get selectAriaLabel(): string {
    return this.localization.translateKey(AppStrings.IconPacks.Tile.SelectIcon, { name: this.icon.name });
  }

  onClick(event: MouseEvent): void {
    this.tileClick.emit({
      icon: this.icon,
      toggle: event.metaKey || event.ctrlKey,
      range: event.shiftKey,
    });
  }

  onCheckboxClick(event: MouseEvent): void {
    event.stopPropagation();
    this.tileClick.emit({ icon: this.icon, toggle: true, range: false });
  }

  onContextMenu(event: MouseEvent): void {
    event.preventDefault();
    this.tileContextMenu.emit({ icon: this.icon, x: event.clientX, y: event.clientY });
  }
}
