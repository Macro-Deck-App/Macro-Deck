import { ChangeDetectionStrategy, Component, Input, booleanAttribute } from '@angular/core';

@Component({
  selector: 'shared-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="es" [class.es-compact]="compact">
      @if (icon) {
        <span class="es-icon icon" [class]="'icon-' + icon + (compact ? ' icon-lg' : ' icon-2xl')"></span>
      }
      @if (heading) {
        <h3 class="es-title">{{ heading }}</h3>
      }
      @if (message) {
        <p class="es-message">{{ message }}</p>
      }
      <ng-content></ng-content>
    </div>
  `,
  styleUrls: ['./empty-state.component.scss'],
})
export class EmptyStateComponent {
  @Input() icon = '';
  @Input() heading = '';
  @Input() message = '';

  @Input({ transform: booleanAttribute }) compact = false;
}
