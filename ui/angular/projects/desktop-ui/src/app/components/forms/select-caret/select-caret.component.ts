import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'shared-select-caret',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
      stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="m6 9 6 6 6-6"/>
    </svg>
  `,
  styles: [`
    :host {
      display: inline-flex;
      width: var(--caret-size, 0.8125rem);
      height: var(--caret-size, 0.8125rem);
      flex-shrink: 0;
      color: var(--color-text-muted);
    }

    svg {
      width: 100%;
      height: 100%;
    }
  `],
})
export class SelectCaretComponent {}
