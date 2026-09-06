import { Component, Input, Output, EventEmitter } from '@angular/core';

import { TranslatePipe } from '@shared';
import { GridSettingsComponent } from '../../grid-settings/grid-settings.component';
import { DropdownMenuComponent } from '../../overlay/dropdown-menu/dropdown-menu.component';

@Component({
  selector: 'app-grid-settings-menu',
  standalone: true,
  imports: [DropdownMenuComponent, GridSettingsComponent, TranslatePipe],
  templateUrl: './grid-settings-menu.component.html',
  styleUrls: ['./grid-settings-menu.component.scss']
})
export class GridSettingsMenuComponent {
  @Input() cols: number | null = 5;
  @Input() rows: number | null = 3;
  @Input() background = '';
  @Input() minCols = 1;
  @Input() maxCols = 12;
  @Input() minRows = 1;
  @Input() maxRows = 8;
  @Input() effectiveCols = 5;
  @Input() effectiveRows = 3;
  @Input() colsLocked = false;
  @Input() rowsLocked = false;
  @Input() lockNote = '';
  @Input() spacingHonoured = true;
  @Input() cornerRadiusHonoured = true;
  @Input() noEffectNote = '';
  @Input() showWidgetAppearance = false;
  @Input() spacing: number | null = null;
  @Input() borderRadius: number | null = null;
  @Input() effectiveSpacing = 12;
  @Input() effectiveBorderRadius: number | null = null;

  @Output() colsChange = new EventEmitter<number | null>();
  @Output() rowsChange = new EventEmitter<number | null>();
  @Output() backgroundChange = new EventEmitter<string>();
  @Output() spacingChange = new EventEmitter<number | null>();
  @Output() borderRadiusChange = new EventEmitter<number | null>();
}
