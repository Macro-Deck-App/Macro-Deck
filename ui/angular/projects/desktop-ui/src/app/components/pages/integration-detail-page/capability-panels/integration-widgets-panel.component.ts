import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { resolveLocalizedText } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe, WidgetTypeCatalogService, WidgetTypeInfo } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { WidgetTypePreviewComponent } from '../../../widgets/widget-type-preview/widget-type-preview.component';

@Component({
  selector: 'app-integration-widgets-panel',
  standalone: true,
  imports: [EmptyStateComponent, LoadingStateComponent, TranslatePipe, WidgetTypePreviewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-widgets-panel.component.html',
  styleUrls: ['./capability-panel.scss', './integration-widgets-panel.component.scss'],
})
export class IntegrationWidgetsPanelComponent implements OnInit {
  readonly integrationId = input.required<string>();
  readonly available = input(true);

  private readonly catalog = inject(WidgetTypeCatalogService);
  private readonly localization = inject(LocalizationService);

  protected readonly previewSize = 112;

  protected readonly loading = signal(true);

  protected readonly types = computed<WidgetTypeInfo[]>(() =>
    this.catalog.types().filter(type => type.providerId === this.integrationId()));

  async ngOnInit(): Promise<void> {
    await this.catalog.load();
    this.loading.set(false);
  }

  protected name(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.name, this.localization) || type.id;
  }

  protected description(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.description, this.localization);
  }
}
