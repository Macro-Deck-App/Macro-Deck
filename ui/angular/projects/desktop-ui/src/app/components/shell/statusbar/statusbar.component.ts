import { Component, EventEmitter, inject, Input, Output } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import {ProfileSelectorComponent} from '../../widgets';
import { NavigationService } from '../../../services';

@Component({
  selector: 'app-statusbar',
  standalone: true,
  imports: [
    ProfileSelectorComponent,
    TranslatePipe,
  ],
  templateUrl: './statusbar.component.html',
  styleUrls: ['./statusbar.component.scss']
})
export class StatusbarComponent {
  @Input() isSidebarCollapsed = false;
  @Input() isConnectionPanelOpen = false;
  @Output() toggleSidebar = new EventEmitter<void>();
  @Output() toggleConnectionPanel = new EventEmitter<void>();

  readonly isShell = !!window.macroDeckShell;
  readonly isMacOS = navigator.platform.toLowerCase().includes('mac');
  readonly isShellMacOS = this.isShell && this.isMacOS;
  protected readonly navigationService = inject(NavigationService);
  protected readonly appStrings = AppStrings;
}
