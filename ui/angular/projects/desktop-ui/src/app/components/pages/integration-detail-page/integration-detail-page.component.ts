import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, exhaustMap, from, timer } from 'rxjs';
import { AppStrings, ConfigEntryDto, GetIntegrationCapabilitiesResponse, IpcProvidedCapability, IntegrationIssuesChangedEvent, IpcIntegrationActionCapability, IpcIntegrationVariableCapability, VariableCatalogNode, IpcIntegrationIssue, PluginCompatibilityReport, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, ErrorBannerComponent, InputComponent, LocalizationService, LocalizedTextPipe, ModalComponent, ToastService, ToggleSwitchComponent, ButtonComponent, TranslatePipe, VariableService } from '@shared';
import { ConfigFlowDialogComponent } from '../../config-flow/config-flow-dialog.component';
import { DetailPageComponent } from '../../detail-page/detail-page.component';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { TabBarComponent } from '../../tab-bar/tab-bar.component';
import { TabItem } from '../../tab-bar/tab-bar.model';
import { COUNTED_CAPABILITY_KINDS } from '../../../domain/integration-capability.util';
import { ConfigFlowService } from '../../../services/config-flow.service';
import { IntegrationService, Integration } from '../../../services/integration.service';
import { PluginCompatibilityService } from '../../../services/plugin-compatibility.service';
import { VariableCatalogService } from '../../../services/variable-catalog.service';
import { VariableBindDialogComponent } from '../../variables/variable-bind-dialog.component';
import { ActionCapabilityRowComponent } from './action-capability-row.component';
import { VariableCapabilityRowComponent } from './variable-capability-row.component';
import { IntegrationCompatibilityCardComponent } from './integration-compatibility-card.component';
import { IntegrationIssuesCardComponent } from './integration-issues-card.component';
import { IntegrationStatusBadge, IntegrationStatusStripComponent } from './integration-status-strip.component';

type DetailTab = 'overview' | 'actions' | 'variables';

type CatalogRow =
  | { kind: 'leaf'; key: string; node: VariableCatalogNode }
  | { kind: 'more'; key: string; parentId: string | undefined };

const MAX_CATALOG_LEAVES = 200;

const TAB_ID_PREFIX = 'integration-detail';

@Component({
  selector: 'app-integration-detail-page',
  standalone: true,
  imports: [
    FormsModule,
    ConfigFlowDialogComponent,
    ConfirmationModalComponent,
    DetailPageComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    ModalComponent,
    ToggleSwitchComponent,
    ButtonComponent,
    InputComponent,
    TabBarComponent,
    ActionCapabilityRowComponent,
    VariableCapabilityRowComponent,
    VariableBindDialogComponent,
    IntegrationIssuesCardComponent,
    IntegrationCompatibilityCardComponent,
    IntegrationStatusStripComponent,
    LocalizedTextPipe,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-detail-page.component.html',
  styleUrls: ['./integration-detail-page.component.scss'],
})
export class IntegrationDetailPageComponent implements OnInit {
  private readonly localization = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly integrationService = inject(IntegrationService);
  protected readonly configFlow = inject(ConfigFlowService);
  private readonly api = inject(ApiService);
  private readonly variableService = inject(VariableService);
  private readonly variableCatalog = inject(VariableCatalogService);
  private readonly toasts = inject(ToastService);
  protected readonly compatibilityService = inject(PluginCompatibilityService);

  protected readonly integrationId = signal<string>('');
  protected readonly configEntries = signal<ConfigEntryDto[]>([]);
  protected readonly configuring = signal(false);
  protected readonly configuringEntryId = signal<string | null>(null);
  protected readonly configurationName = signal('');
  protected readonly configurationNameTarget = signal<ConfigEntryDto | null>(null);
  protected readonly pendingRemove = signal<ConfigEntryDto | null>(null);
  protected readonly configurationMutationPending = signal(false);
  protected readonly showDisableConfirm = signal(false);
  protected readonly issues = signal<IpcIntegrationIssue[]>([]);
  protected readonly issuesLoading = signal(false);
  protected readonly issuesError = signal<string | null>(null);
  protected readonly resolvingIssueId = signal<string | null>(null);

  protected readonly capabilities = signal<GetIntegrationCapabilitiesResponse | null>(null);
  protected readonly capabilitiesLoading = signal(false);
  protected readonly capabilitiesError = signal<string | null>(null);

  protected readonly activeTab = signal<DetailTab>('overview');
  protected readonly actionSearch = signal('');
  protected readonly variableSearch = signal('');

  protected readonly tabIdPrefix = TAB_ID_PREFIX;

  protected readonly disableMessage = computed(() => {
    const name = this.integration()?.name ?? this.localization.translateKey(AppStrings.Integrations.Page.FallbackIntegrationName);
    return this.localization.translateKey(AppStrings.Integrations.Page.DisableMessage, { name });
  });

  protected readonly removeConfigurationMessage = computed(() =>
    this.localization.translateKey(AppStrings.Integrations.Detail.RemoveConfigEntryMessage, {
      name: this.pendingRemove()?.title ?? '',
    }));

  protected readonly integration = computed<Integration | null>(() =>
    this.integrationService.integrations().find(i => i.id === this.integrationId()) ?? null
  );

  protected readonly canSetUp = computed(() => this.integration()?.supportsConfigFlow === true);

  protected readonly compatibilityReport = computed<PluginCompatibilityReport | null>(() => {
    const integration = this.integration();
    if (!integration || integration.isInternal) {
      return null;
    }
    return this.compatibilityService.reports().find(report => report.pluginId === integration.id) ?? null;
  });

  private readonly issuesHealthy = computed(() =>
    this.integration()?.enabled === true
    && !this.issuesLoading()
    && this.issuesError() === null
    && this.issues().length === 0
  );

  private readonly compatibilityHealthy = computed(() => {
    const report = this.compatibilityReport();
    return !this.compatibilityService.isLoading()
      && this.compatibilityService.loadError() === null
      && report !== null
      && report.state === 'compatible'
      && !report.usageTruncated
      && report.findings.length === 0;
  });

  private readonly compatibilityUnknown = computed(() =>
    this.integration()?.isInternal === false
    && !this.compatibilityService.isLoading()
    && this.compatibilityService.loadError() === null
    && this.compatibilityReport() === null
  );

  protected readonly showIssuesCard = computed(() =>
    this.integration()?.enabled === true && !this.issuesHealthy()
  );

  protected readonly showCompatibilityCard = computed(() =>
    this.integration()?.isInternal === false && !this.compatibilityHealthy() && !this.compatibilityUnknown()
  );

  protected readonly statusBadges = computed<IntegrationStatusBadge[]>(() => {
    const badges: IntegrationStatusBadge[] = [];
    if (this.issuesHealthy()) {
      badges.push({ id: 'issues', tier: 'ok', icon: 'check', label: this.localization.translateKey(AppStrings.Integrations.Detail.RunningCleanly) });
    }
    if (this.integration()?.isInternal === false) {
      if (this.compatibilityHealthy()) {
        badges.push({ id: 'compatibility', tier: 'ok', icon: 'check', label: this.localization.translateKey(AppStrings.Integrations.Detail.FullyCompatible) });
      } else if (this.compatibilityUnknown()) {
        badges.push({ id: 'compatibility', tier: 'neutral', icon: 'info', label: this.localization.translateKey(AppStrings.Integrations.Detail.NoCompatibilityData) });
      }
    }
    return badges;
  });

  protected readonly offersCatalog = computed(() => {
    const id = this.integration()?.id;
    return !!id && this.variableCatalog.providersFor()().some(p => p.integrationId === id);
  });

  protected readonly catalogBindNode = signal<VariableCatalogNode | null>(null);

  protected readonly catalogRows = computed<CatalogRow[]>(() => {
    const id = this.integration()?.id;
    if (!id || !this.offersCatalog()) {
      return [];
    }
    const out: CatalogRow[] = [];
    this.appendCatalogRows(id, undefined, out);
    return out;
  });

  private appendCatalogRows(integrationId: string, parentId: string | undefined, out: CatalogRow[]): void {
    if (out.filter(r => r.kind === 'leaf').length >= MAX_CATALOG_LEAVES) {
      return;
    }

    const page = this.variableCatalog.pageFor(integrationId, parentId, undefined)();
    if (!page.available) {
      return;
    }

    for (const node of page.nodes) {
      if (node.hasChildren) {
        this.appendCatalogRows(integrationId, node.id, out);
      }
      if (!node.boundVariableId && node.type) {
        out.push({ kind: 'leaf', key: node.id, node });
      }
      if (out.filter(r => r.kind === 'leaf').length >= MAX_CATALOG_LEAVES) {
        return;
      }
    }

    if (page.hasMore) {
      out.push({ kind: 'more', key: `more:${parentId ?? 'root'}`, parentId });
    }
  }

  protected typeLabel(type: 'text' | 'numeric' | 'boolean'): string {
    const S = AppStrings.Scripts;
    const key = type === 'text' ? S.InputTypeText : type === 'numeric' ? S.InputTypeNumeric : S.InputTypeBoolean;
    return this.localization.translateKey(key);
  }

  protected catalogLabel(node: VariableCatalogNode): string {
    return node.suggestedName ?? resolveLocalizedText(node.displayName, this.localization) ?? node.name;
  }

  protected loadMoreCatalog(parentId: string | undefined): void {
    const id = this.integration()?.id;
    if (id) {
      void this.variableCatalog.loadMore(id, parentId, undefined);
    }
  }

  protected readonly dynamicBadgeLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.Badge));

  protected readonly loadMoreLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.LoadMore));

  protected onCatalogBindRequested(node: VariableCatalogNode): void {
    this.catalogBindNode.set(node);
  }

  protected closeCatalogBindDialog(): void {
    this.catalogBindNode.set(null);
  }

  protected readonly additionalCapabilities = computed<IpcProvidedCapability[]>(() =>
    (this.integration()?.providedCapabilities ?? []).filter(c => !COUNTED_CAPABILITY_KINDS.includes(c.kind)));

  protected readonly requiresSetup = computed(() =>
    this.canSetUp() && !this.configEntries().some(entry => entry.usable !== false)
  );

  private readonly statusKind = computed<'setup' | 'disabled' | 'starting' | 'ready' | ''>(() => {
    const integration = this.integration();
    if (!integration) return '';
    if (this.requiresSetup()) return 'setup';
    if (!integration.enabled) return 'disabled';
    if (!integration.isInitialized) return 'starting';
    return 'ready';
  });

  protected readonly statusLabel = computed(() => {
    switch (this.statusKind()) {
      case 'setup': return this.localization.translateKey(AppStrings.Integrations.Detail.StatusSetupRequired);
      case 'disabled': return this.localization.translateKey(AppStrings.Integrations.Detail.StatusDisabled);
      case 'starting': return this.localization.translateKey(AppStrings.Integrations.Detail.StatusStarting);
      case 'ready': return this.localization.translateKey(AppStrings.Integrations.Detail.StatusReady);
      default: return '';
    }
  });

  protected readonly statusClass = computed(() => {
    switch (this.statusKind()) {
      case 'setup': return 'status-chip-setup';
      case 'disabled': return 'status-chip-disabled';
      case 'starting': return 'status-chip-starting';
      default: return 'status-chip-ready';
    }
  });

  protected readonly declaredActions = computed<IpcIntegrationActionCapability[]>(() =>
    this.capabilities()?.actions ?? []);

  protected readonly declaredVariables = computed<IpcIntegrationVariableCapability[]>(() => {
    const declared = this.capabilities()?.variables ?? [];
    const id = this.integrationId();
    const live = new Map(
      this.variableService.variables()
        .filter(v => v.ownerIntegrationId === id)
        .map(v => [v.name, v] as const));

    if (live.size === 0) return declared;

    return declared.map(capability => {
      // Only rows the host resolved against the registry may be refined here. The earlier branches
      // (setup required, integration disabled, template) are lifecycle states that a value cannot
      // contradict, and the paths that change them refetch the catalog anyway.
      if (capability.availability !== 'Ready' && capability.availability !== 'Unavailable') {
        return capability;
      }

      const match = live.get(capability.name);
      if (!match) return capability;

      const available = match.available ?? true;
      return {
        ...capability,
        value: available ? match.value : capability.value,
        valueAvailable: available,
        availability: available ? 'Ready' : 'Unavailable',
        availabilityReason: available ? 'Ready' : 'Unavailable',
      };
    });
  });

  protected readonly actionsCount = computed(() =>
    this.capabilities()?.actions.length ?? this.integration()?.actionCount ?? 0);

  protected readonly variablesCount = computed(() =>
    this.capabilities()?.variables.length ?? this.integration()?.variableCount ?? 0);

  protected readonly tabs = computed<TabItem[]>(() => [
    { id: 'overview', label: this.localization.translateKey(AppStrings.Integrations.Detail.OverviewTab) },
    { id: 'actions', label: this.localization.translateKey(AppStrings.Integrations.Detail.ActionsTab), badge: this.actionsCount() },
    { id: 'variables', label: this.localization.translateKey(AppStrings.Integrations.Detail.VariablesTab), badge: this.variablesCount() },
  ]);

  protected readonly filteredActions = computed<IpcIntegrationActionCapability[]>(() => {
    const query = this.actionSearch().trim().toLowerCase();
    const actions = this.declaredActions();
    if (!query) return actions;
    return actions.filter(a =>
      resolveLocalizedText(a.name, this.localization).toLowerCase().includes(query)
      || resolveLocalizedText(a.description, this.localization).toLowerCase().includes(query));
  });

  protected readonly filteredVariables = computed<IpcIntegrationVariableCapability[]>(() => {
    const query = this.variableSearch().trim().toLowerCase();
    const variables = this.declaredVariables();
    if (!query) return variables;
    return variables.filter(v => v.name.toLowerCase().includes(query));
  });

  constructor() {
    // The event carries the full issue list, so applying it directly here means the page never has
    // to refetch to stay current with a provider's own unprompted state change (issue #229).
    this.api.onNotification<IntegrationIssuesChangedEvent>('IntegrationIssuesChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(event => {
        if (event.integrationId !== this.integrationId()) {
          return;
        }

        this.issues.set(event.issues);
        this.issuesError.set(null);
      });

    // Connection states may change without a configuration mutation. Exhausting rather than
    // overlapping requests keeps a slow/offline OBS endpoint from building up refresh work.
    timer(2000, 2000)
      .pipe(
        exhaustMap(() => from(this.loadConfigEntries()).pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  protected readonly setUpLabel = computed(() => {
    const integration = this.integration();
    if (integration?.allowsMultipleConfigurations) {
      return this.localization.translateKey(AppStrings.Integrations.Detail.AddConfigurationAction);
    }
    const reconfiguring = !integration?.allowsMultipleConfigurations && this.configEntries().length > 0;
    return this.localization.translateKey(
      reconfiguring ? AppStrings.Integrations.Detail.ReconfigureAction : AppStrings.Integrations.Detail.SetUpAction
    );
  });

  async ngOnInit(): Promise<void> {
    this.integrationId.set(this.route.snapshot.paramMap.get('integrationId') ?? '');

    const initialTab = this.route.snapshot.queryParamMap.get('tab');
    if (initialTab === 'actions' || initialTab === 'variables') {
      this.activeTab.set(initialTab);
    }

    if (this.integrationService.integrations().length === 0) {
      await this.integrationService.loadIntegrations();
    }

    await this.loadConfigEntries();
    await this.loadIssues();
    await this.loadCapabilities();
    // Fire-and-forget: the card renders its own loading/empty states, and this cache is shared with
    // the Developer tab's Compatibility tab, so a visit there already primes it.
    void this.compatibilityService.load();
  }

  goBack(): void {
    void this.router.navigate(['/integrations']);
  }

  iconUrl(): string | null {
    const integration = this.integration();
    return integration?.hasIcon ? this.api.getIntegrationIconUrl(integration.id, integration.iconVersion) : null;
  }

  onTabChange(id: string): void {
    const tab = id as DetailTab;
    this.activeTab.set(tab);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tab === 'overview' ? null : tab },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onToggle(toggle: ToggleSwitchComponent): void {
    const integration = this.integration();
    // The switch flips itself on click; revert it for the branches that don't change the
    // enabled state directly, so the toggle never gets stuck out of sync with reality.
    if (!integration) {
      return;
    }

    if (integration.enabled) {
      toggle.checked = true;
      this.showDisableConfirm.set(true);
      return;
    }

    if (integration.supportsConfigFlow && !this.configEntries().some(entry => entry.usable !== false)) {
      toggle.checked = false;
      const legacy = this.configEntries().find(entry => entry.usable === false);
      if (legacy) {
        this.openConfigurationFlow(legacy.id);
      } else {
        this.onConfigure();
      }
      return;
    }

    void this.toggleAndRefresh(integration.id);
  }

  async confirmDisable(): Promise<void> {
    this.showDisableConfirm.set(false);
    const integration = this.integration();
    if (integration) {
      await this.toggleAndRefresh(integration.id);
    }
  }

  private async toggleAndRefresh(integrationId: string): Promise<void> {
    await this.integrationService.toggleIntegration(integrationId);
    await this.loadIssues();
    await this.loadCapabilities();
  }

  cancelDisable(): void {
    this.showDisableConfirm.set(false);
  }

  onConfigure(): void {
    const integration = this.integration();
    if (!integration) return;

    this.openConfigurationFlow(
      integration.allowsMultipleConfigurations ? null : this.configEntries()[0]?.id ?? null,
    );
  }

  onEditEntry(entry: ConfigEntryDto): void {
    this.openConfigurationFlow(entry.id);
  }

  onRenameEntry(entry: ConfigEntryDto): void {
    this.configurationName.set(entry.title);
    this.configurationNameTarget.set(entry);
  }

  cancelConfigurationName(): void {
    this.configurationNameTarget.set(null);
  }

  async submitConfigurationName(): Promise<void> {
    const title = this.configurationName().trim();
    if (!title || this.configurationMutationPending()) return;

    const integration = this.integration();
    const entry = this.configurationNameTarget();
    if (!integration || !entry) return;

    this.configurationMutationPending.set(true);
    try {
      const response = await this.api.renameConfigEntry(integration.id, entry.id, title);
      if (!response.success) {
        this.toasts.show(response.error?.message ?? this.localization.translateKey(AppStrings.Integrations.Detail.ConfigurationMutationFailed), { variant: 'error' });
        return;
      }
      this.cancelConfigurationName();
      await this.refreshAfterConfigurationMutation();
    } finally {
      this.configurationMutationPending.set(false);
    }
  }

  private openConfigurationFlow(entryId: string | null): void {
    this.configuringEntryId.set(entryId);
    this.configuring.set(true);
  }

  async onConfigFlowClosed(completed: boolean): Promise<void> {
    this.configuring.set(false);
    this.configuringEntryId.set(null);
    if (completed) {
      await this.integrationService.loadIntegrations();
      await this.loadConfigEntries();
      await this.loadIssues();
      await this.loadCapabilities();
    }
  }

  async onResolveIssue(issue: IpcIntegrationIssue): Promise<void> {
    const integration = this.integration();
    if (!integration || this.resolvingIssueId()) {
      return;
    }

    this.resolvingIssueId.set(issue.id);
    try {
      const response = await this.api.resolveIntegrationIssue(integration.id, issue.id);
      if (response.followUp === 'StartConfigFlow') {
        this.onConfigure();
        return;
      }
      // What the action actually achieved is only in the response - a permission may have been
      // granted, a prompt raised, or System Settings opened - and dropping it left the button
      // looking like it did nothing at all (issue #706).
      const message = resolveLocalizedText(response.message, this.localization);
      if (message) {
        this.toasts.show(message, { variant: response.success ? 'success' : 'error' });
      }
      await this.integrationService.loadIntegrations();
      await this.loadIssues();
      await this.loadCapabilities();
    } finally {
      this.resolvingIssueId.set(null);
    }
  }

  private async loadIssues(): Promise<void> {
    const integration = this.integration();
    if (!integration?.enabled) {
      this.issues.set([]);
      this.issuesLoading.set(false);
      this.issuesError.set(null);
      return;
    }

    this.issuesLoading.set(true);
    this.issuesError.set(null);
    try {
      const response = await this.api.getIntegrationIssues(integration.id);
      this.issues.set(response.issues);
    } catch (error) {
      // A failed load must not silently blank the list - that would read as "no issues" when the
      // truth is "we don't know".
      console.error('Failed to load integration issues:', error);
      this.issuesError.set(this.localization.translateKey(AppStrings.Integrations.Detail.LoadIssuesFailed));
    } finally {
      this.issuesLoading.set(false);
    }
  }

  private async loadCapabilities(): Promise<void> {
    const id = this.integrationId();
    if (!id) {
      return;
    }

    this.capabilitiesLoading.set(true);
    this.capabilitiesError.set(null);
    try {
      const response = await this.api.getIntegrationCapabilities(id);
      this.capabilities.set(response);
    } catch (error) {
      // Same rule as the issue list: a failed load must not render as an empty catalog, which would
      // read as "this integration declares nothing" when the truth is "we don't know".
      console.error('Failed to load integration capabilities:', error);
      this.capabilitiesError.set(this.localization.translateKey(AppStrings.Integrations.Detail.LoadCapabilitiesFailed));
    } finally {
      this.capabilitiesLoading.set(false);
    }
  }

  onRemoveEntry(entry: ConfigEntryDto): void {
    this.pendingRemove.set(entry);
  }

  cancelRemoveEntry(): void {
    this.pendingRemove.set(null);
  }

  async confirmRemoveEntry(): Promise<void> {
    const integration = this.integration();
    const entry = this.pendingRemove();
    if (!integration || !entry || this.configurationMutationPending()) {
      return;
    }

    this.pendingRemove.set(null);
    this.configurationMutationPending.set(true);
    try {
      const response = await this.api.deleteConfigEntry(integration.id, entry.id, true);
      if (!response.success) {
        this.toasts.show(response.error?.message ?? this.localization.translateKey(AppStrings.Integrations.Detail.ConfigurationMutationFailed), { variant: 'error' });
        return;
      }
      await this.refreshAfterConfigurationMutation();
    } finally {
      this.configurationMutationPending.set(false);
    }
  }

  protected configurationStatusLabel(entry: ConfigEntryDto): string {
    const key = {
      Connected: AppStrings.Integrations.Detail.ConfigStatusConnected,
      Connecting: AppStrings.Integrations.Detail.ConfigStatusConnecting,
      Reconnecting: AppStrings.Integrations.Detail.ConfigStatusReconnecting,
      Disconnected: AppStrings.Integrations.Detail.ConfigStatusDisconnected,
      NeedsReconfiguration: AppStrings.Integrations.Detail.ConfigStatusNeedsReconfiguration,
      Ready: AppStrings.Integrations.Detail.ConfigStatusReady,
    }[entry.status ?? 'Ready'];
    return this.localization.translateKey(key);
  }

  protected configurationStatusClass(entry: ConfigEntryDto): string {
    if (!entry.status || entry.status === 'Connected' || entry.status === 'Ready') return 'config-status-ok';
    if (entry.status === 'NeedsReconfiguration' || entry.status === 'Disconnected') return 'config-status-warning';
    return 'config-status-pending';
  }

  private async refreshAfterConfigurationMutation(): Promise<void> {
    await this.integrationService.loadIntegrations();
    await this.loadConfigEntries();
    await this.loadIssues();
    await this.loadCapabilities();
  }

  tryInDeveloper(action: IpcIntegrationActionCapability): void {
    if (action.availability !== 'Ready') {
      return;
    }
    const integration = this.integration();
    if (!integration) {
      return;
    }
    void this.router.navigate(['/developer'], {
      queryParams: { integrationId: integration.id, actionId: action.id },
    });
  }

  private async loadConfigEntries(): Promise<void> {
    const integration = this.integration();
    if (!integration?.supportsConfigFlow) {
      this.configEntries.set([]);
      return;
    }

    const response = await this.api.getConfigEntries(integration.id);
    this.configEntries.set(response.entries);
  }
}
