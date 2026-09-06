import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  inject,
  Input,
  isDevMode,
} from '@angular/core';

import { ButtonGroupComponent, ButtonGroupSize } from './button-group.component';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'danger-ghost' | 'ghost' | 'add';
export type ButtonSize    = 'compact' | 'md' | 'lg' | 'icon' | 'icon-compact';

function resolveButtonSize(
  declared: ButtonSize | undefined,
  iconOnly: boolean,
  group: ButtonGroupSize | null,
): ButtonSize {
  const isIcon = iconOnly || declared === 'icon' || declared === 'icon-compact';
  if (group === null) {
    return declared ?? (isIcon ? 'icon' : 'md');
  }
  if (group === 'compact') {
    return isIcon ? 'icon-compact' : 'compact';
  }
  return isIcon ? 'icon' : 'md';
}

function isTextSize(size: ButtonSize | undefined): size is 'compact' | 'md' | 'lg' {
  return size !== undefined && size !== 'icon' && size !== 'icon-compact';
}

@Component({
  selector: 'shared-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [class]="hostClass"
      [disabled]="disabled || loading"
      [attr.aria-label]="ariaLabel || null"
      [attr.aria-pressed]="pressed === undefined ? null : pressed"
      [attr.type]="type">
      @if (loading) {
        <span class="sb-spinner" aria-hidden="true"></span>
      }
      <ng-content></ng-content>
    </button>
  `,
  styleUrls: ['./button.component.scss'],
  host: {
    '[class.sb-fill]': 'fill',
    '[class.sb-disabled]': 'disabled || loading',
  },
})
export class ButtonComponent {
  @Input() variant: ButtonVariant = 'secondary';
  @Input() size?: ButtonSize;
  @Input({ transform: booleanAttribute }) iconOnly = false;
  @Input({ transform: booleanAttribute }) fill = false;
  @Input() loading = false;
  @Input() disabled = false;
  @Input() type: 'button' | 'submit' | 'reset' = 'button';
  @Input() ariaLabel = '';
  @Input() pressed?: boolean;

  private readonly group = inject(ButtonGroupComponent, { optional: true, host: true });
  private warned = false;

  get hostClass(): string {
    const group = this.group?.size() ?? null;
    const size = resolveButtonSize(this.size, this.iconOnly, group);
    if (isDevMode() && group !== null && isTextSize(this.size) && this.size !== size) {
      this.warnMismatch(size);
    }
    return `sb sb-${this.variant} sb-${size}${this.loading ? ' sb-loading' : ''}${this.pressed ? ' sb-pressed' : ''}`;
  }

  private warnMismatch(resolved: ButtonSize): void {
    if (this.warned) {
      return;
    }
    this.warned = true;
    console.error(
      `shared-button: size="${this.size}" contradicts the enclosing shared-button-group; ` +
      `rendering as "${resolved}". Drop the size, or take the button out of the group.`,
    );
  }
}
