import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@shared';
import { CapabilityPage } from '../../../../domain/integration-capability-pages';
import { RailItemComponent } from '../../../rail-page/rail-item.component';

@Component({
  selector: 'app-integration-data-nav',
  standalone: true,
  imports: [RailItemComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-data-nav.component.html',
  styleUrls: ['./integration-data-nav.component.scss'],
})
export class IntegrationDataNavComponent {
  readonly pages = input.required<readonly CapabilityPage[]>();
  readonly activeId = input.required<string>();
  readonly counts = input<ReadonlyMap<string, number>>(new Map());

  readonly selected = output<string>();
}
