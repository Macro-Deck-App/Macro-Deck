import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'shared-rail-item',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="rail-item"
      role="option"
      [attr.aria-selected]="active"
      [class.active]="active">
      <span class="rail-item-name" [title]="label">{{ label }}</span>
      <span class="rail-item-meta">
        <ng-content></ng-content>
        @if (count !== null) {
          <span class="rail-item-count">{{ unboundCount === null ? count : count + ' (' + unboundCount + ')' }}</span>
        }
      </span>
    </button>
  `,
  styleUrls: ['./rail-item.component.scss'],
})
export class RailItemComponent {
  @Input({ required: true }) label = '';
  @Input() count: number | null = null;

  @Input() unboundCount: number | null = null;
  @Input() active = false;
}
