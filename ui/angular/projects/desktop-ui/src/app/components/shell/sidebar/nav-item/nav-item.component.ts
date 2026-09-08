import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { RouterLink, RouterLinkActive } from '@angular/router';
import { TooltipDirective } from '../../../overlay/tooltip/tooltip.directive';
import { NavItem } from '../../../../domain/navigation.interface';

@Component({
  selector: 'app-nav-item',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, NgTemplateOutlet, TooltipDirective],
  templateUrl: './nav-item.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./nav-item.component.scss']
})
export class NavItemComponent {
  @Input({ required: true }) item!: NavItem;
  @Input() isCollapsed = false;
  @Input() active = false;

  @Output() action = new EventEmitter<string>();
}
