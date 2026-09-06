import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, UiPreviewDiagnostic, UiPreviewEntry } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { RailPageComponent } from '../../../rail-page/rail-page.component';
import { IntegrationService } from '../../../../services/integration.service';
import { PreviewCanvasComponent } from './preview-canvas.component';

interface PreviewGroup {
  key: string;
  ownerId: string;
  ownerLabel: string;
  view: string;
  previews: UiPreviewEntry[];
}

@Component({
  selector: 'app-ui-previews-tab',
  standalone: true,
  imports: [
    EmptyStateComponent,
    LoadingStateComponent,
    PreviewCanvasComponent,
    RailItemComponent,
    RailPageComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-previews-tab.component.html',
  styleUrls: ['./ui-previews-tab.component.scss'],
})
export class UiPreviewsTabComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly integrationService = inject(IntegrationService);

  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);
  protected readonly previews = signal<UiPreviewEntry[]>([]);
  protected readonly diagnostics = signal<UiPreviewDiagnostic[]>([]);
  protected readonly selectedPreviewId = signal<string | null>(null);

  protected readonly selectedPreview = computed<UiPreviewEntry | null>(() =>
    this.previews().find(p => p.id === this.selectedPreviewId()) ?? null,
  );

  protected readonly groups = computed<PreviewGroup[]>(() => this.buildGroups(this.previews()));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(false);
    try {
      const response = await this.api.listUiPreviews();
      if (!response) {
        this.loadError.set(true);
        return;
      }
      this.previews.set(response.previews);
      this.diagnostics.set(response.diagnostics);
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected select(previewId: string): void {
    this.selectedPreviewId.set(previewId);
  }

  protected isSelected(previewId: string): boolean {
    return this.selectedPreviewId() === previewId;
  }

  private buildGroups(previews: UiPreviewEntry[]): PreviewGroup[] {
    const byKey = new Map<string, PreviewGroup>();
    for (const preview of previews) {
      const key = `${preview.ownerId}::${preview.view}`;
      let group = byKey.get(key);
      if (!group) {
        group = { key, ownerId: preview.ownerId, ownerLabel: this.ownerLabel(preview.ownerId), view: preview.view, previews: [] };
        byKey.set(key, group);
      }
      group.previews.push(preview);
    }
    return [...byKey.values()].sort((a, b) => a.ownerLabel.localeCompare(b.ownerLabel) || a.view.localeCompare(b.view));
  }

  private ownerLabel(ownerId: string): string {
    if (ownerId === '') return this.localization.translateKey(AppStrings.Developer.Previews.BuiltInSource);
    return this.integrationService.integrations().find(i => i.id === ownerId)?.name ?? ownerId;
  }
}
