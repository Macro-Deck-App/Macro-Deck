import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import type { ConfigEntryDto } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { IntegrationService } from '../../../services/integration.service';
import { SelectComponent, SelectOption } from '../select/select.component';

@Component({
  selector: 'shared-integration-picker',
  standalone: true,
  imports: [FormsModule, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-select
      [options]="options()"
      [placeholder]="placeholder()"
      [disabled]="disabled()"
      [ngModel]="value()"
      (ngModelChange)="valueChange.emit($event)" />
  `,
})
export class IntegrationPickerComponent {
  private readonly integrationService = inject(IntegrationService);
  private readonly api = inject(ApiService);

  readonly value = input('');
  readonly capability = input<string | undefined>(undefined);
  readonly configurationEntries = input(false);
  readonly placeholder = input('');
  readonly disabled = input(false);

  readonly valueChange = output<string>();

  private readonly entriesByIntegration = signal<Readonly<Record<string, readonly ConfigEntryDto[]>>>({});
  private readonly entriesRequested = new Set<string>();

  protected readonly filteredIntegrations = computed(() => {
    const capability = this.capability();
    return this.integrationService.integrations().filter(integration =>
      !capability || integration.providedCapabilities.some(provided => provided.kind === capability));
  });

  protected readonly options = computed<SelectOption[]>(() => {
    const integrations = this.filteredIntegrations();
    if (!this.configurationEntries()) {
      return integrations.map(integration => ({ value: integration.id, label: integration.name }));
    }

    const entries = this.entriesByIntegration();
    const multipleIntegrations = integrations.length > 1;
    const options: SelectOption[] = [];
    for (const integration of integrations) {
      for (const entry of entries[integration.id] ?? []) {
        options.push({
          value: entry.id,
          label: multipleIntegrations ? `${integration.name} – ${entry.title}` : entry.title,
        });
      }
    }
    return options;
  });

  constructor() {
    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }

    effect(() => {
      if (!this.configurationEntries()) return;
      for (const integration of this.filteredIntegrations()) {
        this.loadEntries(integration.id);
      }
    });
  }

  private loadEntries(integrationId: string): void {
    if (this.entriesRequested.has(integrationId)) return;
    this.entriesRequested.add(integrationId);
    this.api.getConfigEntries(integrationId)
      .then(response => {
        this.entriesByIntegration.update(current => ({ ...current, [integrationId]: response.entries }));
      })
      .catch(() => this.entriesRequested.delete(integrationId));
  }
}
