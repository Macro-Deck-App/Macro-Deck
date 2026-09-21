import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { AppStrings, IntegrationsChangedEvent, UiPreviewDiagnostic, UiPreviewEntry } from '@macro-deck/runtime';
import { ApiService, ConnectionState, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { RailPageComponent } from '../../../rail-page/rail-page.component';
import { IntegrationService } from '../../../../services/integration.service';
import { CanvasSize, PreviewCanvasComponent } from './preview-canvas.component';

interface PreviewGroup {
  key: string;
  ownerId: string;
  ownerLabel: string;
  view: string;
  previews: UiPreviewEntry[];
}

export const PREVIEW_QUERY_PARAMS = ['preview', 'owner', 'w', 'h'] as const;

const RELOAD_DELAY_MS = 250;
const MIN_SIZE = 40;
const MAX_SIZE = 4000;

function parseDimension(value: string | null): number | null {
  if (value === null || !/^\d+$/.test(value)) return null;
  return Math.min(MAX_SIZE, Math.max(MIN_SIZE, Number(value)));
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
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal(false);
  protected readonly previews = signal<UiPreviewEntry[]>([]);
  protected readonly diagnostics = signal<UiPreviewDiagnostic[]>([]);
  protected readonly selectedPreviewId = signal<string | null>(null);
  protected readonly catalogRevision = signal(0);
  protected readonly initialSize: CanvasSize | null;

  private readonly lastKnownPreview = signal<UiPreviewEntry | null>(null);
  private readonly restoredOwnerId: string;

  protected readonly selectedPreview = computed<UiPreviewEntry | null>(() => {
    const id = this.selectedPreviewId();
    if (id === null) return null;

    const listed = this.previews().find(p => p.id === id);
    if (listed) return listed;

    const lastKnown = this.lastKnownPreview();
    if (lastKnown?.id === id) return lastKnown;

    return { id, ownerId: this.restoredOwnerId, view: '', scenario: '', profile: '' };
  });

  protected readonly selectedAvailable = computed(() => this.previews().some(p => p.id === this.selectedPreviewId()));

  protected readonly selectedOwnerConnected = computed(() => {
    const ownerId = this.selectedPreview()?.ownerId ?? '';
    return ownerId === '' || this.previews().some(p => p.ownerId === ownerId);
  });

  protected readonly groups = computed<PreviewGroup[]>(() => this.buildGroups(this.previews()));

  private reloadRunning = false;
  private reloadQueued = false;
  private reloadTimer: ReturnType<typeof setTimeout> | null = null;
  private connectionBaseline: ConnectionState | null = null;

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.selectedPreviewId.set(params.get('preview'));
    this.restoredOwnerId = params.get('owner') ?? '';
    const width = parseDimension(params.get('w'));
    const height = parseDimension(params.get('h'));
    this.initialSize = width !== null && height !== null ? { width, height } : null;

    this.api.onNotification<IntegrationsChangedEvent>('IntegrationsChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.scheduleReload());

    effect(() => {
      const state = this.api.connectionStateSignal();
      untracked(() => {
        if (state === 'connected' && this.connectionBaseline !== null && this.connectionBaseline !== 'connected') {
          this.scheduleReload();
        }
        this.connectionBaseline = state;
      });
    });

    effect(() => {
      const listed = this.previews().find(p => p.id === this.selectedPreviewId());
      if (listed) untracked(() => this.lastKnownPreview.set(listed));
    });

    inject(DestroyRef).onDestroy(() => {
      if (this.reloadTimer !== null) clearTimeout(this.reloadTimer);
    });

    void this.reload();
  }

  protected select(previewId: string): void {
    this.selectedPreviewId.set(previewId);
    const ownerId = this.previews().find(p => p.id === previewId)?.ownerId ?? '';
    this.writeQueryParams({ preview: previewId, owner: ownerId === '' ? null : ownerId });
  }

  protected isSelected(previewId: string): boolean {
    return this.selectedPreviewId() === previewId;
  }

  protected onCanvasSizeChange(size: CanvasSize): void {
    this.writeQueryParams({ w: size.width, h: size.height });
  }

  private scheduleReload(): void {
    if (this.reloadTimer !== null) clearTimeout(this.reloadTimer);
    this.reloadTimer = setTimeout(() => {
      this.reloadTimer = null;
      void this.reload();
    }, RELOAD_DELAY_MS);
  }

  private async reload(): Promise<void> {
    if (this.reloadRunning) {
      this.reloadQueued = true;
      return;
    }

    this.reloadRunning = true;
    try {
      do {
        this.reloadQueued = false;
        await this.fetch();
      } while (this.reloadQueued);
    } finally {
      this.reloadRunning = false;
    }
  }

  private async fetch(): Promise<void> {
    this.loading.set(true);
    try {
      const response = await this.api.listUiPreviews();
      if (!response) {
        this.loadError.set(!this.loaded());
        return;
      }
      this.previews.set(response.previews);
      this.diagnostics.set(response.diagnostics);
      this.loadError.set(false);
      this.loaded.set(true);
      this.catalogRevision.update(revision => revision + 1);
    } catch {
      this.loadError.set(!this.loaded());
    } finally {
      this.loading.set(false);
    }
  }

  private writeQueryParams(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
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
