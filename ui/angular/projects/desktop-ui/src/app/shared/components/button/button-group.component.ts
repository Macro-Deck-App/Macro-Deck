import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  input,
} from '@angular/core';

export type ButtonGroupSize = 'compact' | 'md';

@Component({
  selector: 'shared-button-group',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content></ng-content>',
  styleUrls: ['./button-group.component.scss'],
  host: {
    '[class]': 'hostClass()',
    '[attr.role]': 'ariaLabel() ? "group" : null',
    '[attr.aria-label]': 'ariaLabel() || null',
  },
})
export class ButtonGroupComponent {
  readonly size = input<ButtonGroupSize>('md');
  readonly align = input<'start' | 'center' | 'end' | 'between'>('start');
  readonly gap = input<'xs' | 'sm' | 'md'>('sm');
  readonly wrap = input(false, { transform: booleanAttribute });
  readonly ariaLabel = input('');

  hostClass(): string {
    return `sbg sbg-align-${this.align()} sbg-gap-${this.gap()}${this.wrap() ? ' sbg-wrap' : ''}`;
  }
}
