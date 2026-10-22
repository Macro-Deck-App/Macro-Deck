import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { EventDefinition, IntegrationsChangedEvent } from '@macro-deck/runtime';
import { ApiService, ErrorBannerComponent, InputComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { EventCatalogService } from '../../../../services/event-catalog.service';

@Component({
  selector: 'app-integration-events-panel',
  standalone: true,
  imports: [EmptyStateComponent, ErrorBannerComponent, FormsModule, InputComponent, LoadingStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-events-panel.component.html',
  styleUrls: ['./capability-panel.scss', '../action-capability-row.component.scss', './integration-events-panel.component.scss'],
})
export class IntegrationEventsPanelComponent implements OnInit {
  readonly integrationId = input.required<string>();

  protected readonly catalog = inject(EventCatalogService);
  private readonly api = inject(ApiService);

  protected readonly search = signal('');

  protected readonly events = computed<EventDefinition[]>(() =>
    this.catalog.events().filter(event => event.isIntegration && event.providerId === this.integrationId()));

  protected readonly filtered = computed<EventDefinition[]>(() => {
    const query = this.search().trim().toLowerCase();
    const events = this.events();
    if (!query) return events;
    return events.filter(event =>
      event.name.toLowerCase().includes(query)
      || (event.description ?? '').toLowerCase().includes(query)
      || (event.category ?? '').toLowerCase().includes(query));
  });

  constructor() {
    this.api.onNotification<IntegrationsChangedEvent>('IntegrationsChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(event => {
        if (event.integrationId === this.integrationId()) {
          void this.catalog.reload();
        }
      });
  }

  ngOnInit(): void {
    void this.catalog.reload();
  }

  protected names(parameters: EventDefinition['payloadParameters']): string {
    return parameters.map(parameter => parameter.label || parameter.name).join(', ');
  }
}
