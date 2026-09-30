import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { VARIABLE_ROW_INDENT } from './variable-row.component';

@Component({
  selector: 'shared-variable-group-header',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (collapsible()) {
      <button
        type="button"
        class="vgh vgh-toggle"
        [attr.aria-expanded]="expanded()"
        [attr.aria-label]="ariaLabel()"
        (click)="toggle.emit()">
        <span class="icon icon-xs icon-chevron-right vgh-chevron" [class.vgh-chevron-open]="expanded()" aria-hidden="true"></span>
        <span class="vgh-label">{{ label() }}</span>
        @if (count() !== null) {
          <span class="vgh-count">{{ count() }}</span>
        }
      </button>
    } @else {
      <div class="vgh" aria-hidden="true">
        <span class="vgh-label">{{ label() }}</span>
      </div>
    }
  `,
  styleUrls: ['./variable-group-header.component.scss'],
  host: {
    '[class.vgh-section]': 'section()',
    '[class.vgh-card]': 'card()',
    '[style.margin-inline-start.px]': 'depth() * indent',
  },
})
export class VariableGroupHeaderComponent {
  readonly label = input.required<string>();
  readonly count = input<number | null>(null);
  readonly collapsible = input(false);
  readonly section = input(false);
  readonly card = input(false);
  readonly expanded = input(false);
  readonly ariaLabel = input<string | null>(null);
  readonly depth = input(0);

  readonly toggle = output<void>();

  protected readonly indent = VARIABLE_ROW_INDENT;
}
