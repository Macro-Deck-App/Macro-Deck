import { Component, EventEmitter, Input, Output } from '@angular/core';

import { NavItem } from '../../../domain/navigation.interface';
import { SIDEBAR_COLLAPSED_WIDTH, SIDEBAR_EXPANDED_WIDTH } from '../../../services';
import { NavItemComponent } from './nav-item/nav-item.component';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [NavItemComponent],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.scss']
})
export class SidebarComponent {
  protected readonly expandedWidth = SIDEBAR_EXPANDED_WIDTH;
  protected readonly collapsedWidth = SIDEBAR_COLLAPSED_WIDTH;
  protected readonly collapsedWidthCss = `${SIDEBAR_COLLAPSED_WIDTH}px`;

  @Input() isCollapsed = false;
  @Input() mainItemGroups: NavItem[][] = [];
  @Input() bottomItems: NavItem[] = [];
  @Input() activeAction: string | null = null;

  @Output() itemAction = new EventEmitter<string>();
}
