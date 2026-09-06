import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { NavItem, NavItemGroup } from '../../../domain/navigation.interface';
import { TooltipDirective } from '../../overlay/tooltip/tooltip.directive';

@Component({
  selector: 'shared-settings-nav',
  standalone: true,
  imports: [TooltipDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="settings-nav" [class.settings-nav--collapsed]="collapsed">
      @for (group of renderedGroups(); track $index; let first = $first) {
        <div
          class="settings-nav__group"
          [class.settings-nav__group--divided]="!group.label && !first"
          role="group"
          [attr.aria-label]="group.label ?? null">
          @if (group.label) {
            <h3 class="settings-nav__group-label">{{ group.label }}</h3>
          }
          @for (item of group.items; track item.id) {
            <button
              type="button"
              class="settings-nav__item"
              [class.active]="item.id === activeId"
              [sharedTooltip]="item.label"
              [tooltipDisabled]="!collapsed"
              [attr.aria-label]="collapsed ? item.label : null"
              [attr.aria-current]="item.id === activeId ? 'true' : null"
              (click)="select.emit(item.id)">
              <span [class]="iconClass(item)"></span>
              <span class="settings-nav__label">{{ item.label }}</span>
            </button>
          }
        </div>
      }
    </nav>
  `,
  styleUrls: ['./settings-nav.component.scss'],
})
export class SettingsNavComponent {
  private readonly flatItems = signal<NavItem[]>([]);
  private readonly itemGroups = signal<NavItemGroup[] | undefined>(undefined);

  @Input() set items(value: NavItem[]) {
    this.flatItems.set(value);
  }

  @Input() set groups(value: NavItemGroup[] | undefined) {
    this.itemGroups.set(value);
  }

  @Input() activeId?: string;
  @Input() collapsed = false;

  @Output() select = new EventEmitter<string>();

  protected readonly renderedGroups = computed<NavItemGroup[]>(
    () => this.itemGroups() ?? [{ items: this.flatItems() }],
  );

  iconClass(item: NavItem): string {
    return `icon icon-${item.icon} icon-md settings-nav__icon`;
  }
}
