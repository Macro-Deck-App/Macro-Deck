import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'shared-loading-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ls">
      <span class="ls-spinner" aria-hidden="true"></span>
      @if (message) {
        <span class="ls-message">{{ message }}</span>
      }
    </div>
  `,
  styleUrls: ['./loading-state.component.scss'],
})
export class LoadingStateComponent {
  @Input() message = '';
}
