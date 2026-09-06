import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface IntegrationStatusBadge {
  id: string;
  tier: 'ok' | 'neutral';
  icon: 'check' | 'info';
  label: string;
}

@Component({
  selector: 'app-integration-status-strip',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents;' },
  templateUrl: './integration-status-strip.component.html',
  styleUrls: ['./integration-status-strip.component.scss'],
})
export class IntegrationStatusStripComponent {
  readonly badges = input.required<IntegrationStatusBadge[]>();
}
