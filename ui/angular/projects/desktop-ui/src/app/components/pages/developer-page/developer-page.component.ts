import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { RailItemComponent } from '../../rail-page/rail-item.component';
import { RailPageComponent } from '../../rail-page/rail-page.component';
import { TabBarComponent } from '../../tab-bar/tab-bar.component';
import { TabItem } from '../../tab-bar/tab-bar.model';
import { ActionService } from '../../../services/action.service';
import { IntegrationService } from '../../../services/integration.service';
import { ActionRunnerComponent } from './action-runner.component';
import { EventsTabComponent } from './events-tab/events-tab.component';
import { LogsTabComponent } from './logs-tab/logs-tab.component';
import { MaintenanceTabComponent } from './maintenance-tab.component';
import {
  PLUGIN_DEVELOPMENT_TAB,
  PluginDevelopmentTabComponent,
} from './plugin-development-tab/plugin-development-tab.component';
import { TemplateTabComponent } from './template-tab/template-tab.component';
import { UiPreviewsTabComponent } from './ui-previews-tab/ui-previews-tab.component';

const DEFAULT_TAB = 'run-action';

const LEGACY_PLUGIN_TABS: Readonly<Record<string, string>> = {
  'plugin-tokens': PLUGIN_DEVELOPMENT_TAB,
  'managed-plugins': PLUGIN_DEVELOPMENT_TAB,
  compatibility: PLUGIN_DEVELOPMENT_TAB,
};

interface IntegrationSourceEntry {
  integrationId: string;
  name: string;
  count: number;
}

@Component({
  selector: 'app-developer-page',
  standalone: true,
  imports: [
    RailItemComponent,
    RailPageComponent,
    TabBarComponent,
    ActionRunnerComponent,
    TemplateTabComponent,
    EventsTabComponent,
    LogsTabComponent,
    MaintenanceTabComponent,
    PluginDevelopmentTabComponent,
    UiPreviewsTabComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './developer-page.component.html',
  styleUrls: ['./developer-page.component.scss'],
})
export class DeveloperPageComponent {
  private readonly localization = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly integrationService = inject(IntegrationService);
  protected readonly actionService = inject(ActionService);

  readonly tabs = computed<TabItem[]>(() => {
    const t = this.localization.translateKey.bind(this.localization);
    const S = AppStrings.Developer;
    return [
      { id: DEFAULT_TAB, label: t(S.RunActionTab) },
      { id: 'template', label: t(S.TemplateTab) },
      { id: 'events', label: t(S.EventsTab) },
      { id: 'logs', label: t(S.LogsTab) },
      { id: 'previews', label: t(S.Previews.Tab) },
      { id: PLUGIN_DEVELOPMENT_TAB, label: t(S.PluginDevelopmentTab) },
      { id: 'maintenance', label: t(S.MaintenanceTab) },
    ];
  });

  readonly activeTab = signal(DEFAULT_TAB);

  readonly selectedIntegrationId = signal<string | null>(null);
  readonly selectedActionId = signal<string | null>(null);

  readonly totalCount = computed(() => this.actionService.actions().length);

  readonly integrationSources = computed<IntegrationSourceEntry[]>(() => {
    const counts = new Map<string, number>();
    for (const action of this.actionService.actions()) {
      counts.set(action.integrationId, (counts.get(action.integrationId) ?? 0) + 1);
    }

    const integrations = this.integrationService.integrations();
    return [...counts.entries()]
      .map(([integrationId, count]) => ({
        integrationId,
        name: integrations.find(i => i.id === integrationId)?.name
          ?? this.actionService.actions().find(a => a.integrationId === integrationId)?.integrationName
          ?? integrationId,
        count,
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly runnerTitle = computed(() => {
    const id = this.selectedIntegrationId();
    if (!id) {
      return this.localization.translateKey(AppStrings.Developer.AllActionsHeading);
    }
    return this.integrationSources().find(s => s.integrationId === id)?.name ?? id;
  });

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      this.selectedIntegrationId.set(params.get('integrationId'));
      this.selectedActionId.set(params.get('actionId'));

      const tab = params.get('tab');
      const resolved = tab === null ? null : LEGACY_PLUGIN_TABS[tab] ?? tab;
      this.activeTab.set(this.tabs().some(t => t.id === resolved) ? resolved! : DEFAULT_TAB);
    });

    if (this.integrationService.integrations().length === 0) {
      void this.integrationService.loadIntegrations();
    }
    void this.actionService.loadActions();
  }

  isAllSelected(): boolean {
    return this.selectedIntegrationId() === null;
  }

  isIntegrationSelected(integrationId: string): boolean {
    return this.selectedIntegrationId() === integrationId;
  }

  selectTab(tabId: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tabId === DEFAULT_TAB ? null : tabId },
      queryParamsHandling: 'merge',
    });
  }

  selectAll(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { integrationId: null, actionId: null },
      queryParamsHandling: 'merge',
    });
  }

  selectIntegration(integrationId: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { integrationId },
      queryParamsHandling: 'merge',
    });
  }
}
