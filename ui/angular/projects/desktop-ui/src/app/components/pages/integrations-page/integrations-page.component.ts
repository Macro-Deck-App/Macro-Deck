import { Component, ElementRef, OnInit, ViewChild, computed, effect, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { FileOpenService } from '../../../services/file-open.service';
import {
  IntegrationFilterService,
  IntegrationIssuesFilter,
  IntegrationStatusFilter,
  IntegrationTypeFilter,
} from '../../../services/integration-filter.service';
import { AppStrings, IpcProvidedCapability, PLUGIN_INSTALL_ERROR_ALREADY_INSTALLED, PluginCompatibilityReport, PluginCompatibilityState, PluginInstallActionResponse, LocalizedText, resolveLocalizedText, Strings } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, CheckboxComponent, ErrorBannerComponent, InputComponent, LocalizationService, LocalizedTextPipe, ModalComponent, ToastService, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { ConfigFlowDialogComponent } from '../../config-flow/config-flow-dialog.component';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../../overlay/dropdown-menu/dropdown-menu.component';
import { PluginInstallConfirmModalComponent } from '../../plugins/plugin-install-confirm-modal/plugin-install-confirm-modal.component';
import { ShellDrop, ShellDropTargetDirective } from '../../shell-drop/shell-drop-target.directive';
import { COUNTED_CAPABILITY_KINDS, integrationCapabilityIcon } from '../../../domain/integration-capability.util';
import { ConfigFlowService } from '../../../services/config-flow.service';
import { IntegrationService, Integration } from '../../../services/integration.service';
import { PluginCompatibilityService } from '../../../services/plugin-compatibility.service';
import { PluginInstallationService } from '../../../services/plugin-installation.service';
import { ArchiveSource, fileSource, pathSource } from '../../../services/portability.service';
import { compatibilityStateLabel, compatibilityStateTier } from '../../../util/plugin-compatibility-display';

interface CapabilityFilterOption {
  kind: string;
  name: LocalizedText;
}

type FilterFacet = 'status' | 'type' | 'capabilities' | 'issues';

@Component({
  selector: 'app-integrations-page',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    CheckboxComponent,
    ConfigFlowDialogComponent,
    ConfirmationModalComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    InputComponent,
    LoadingStateComponent,
    ModalComponent,
    PluginInstallConfirmModalComponent,
    ShellDropTargetDirective,
    ToggleSwitchComponent,
    LocalizedTextPipe,
    TranslatePipe,
  ],
  templateUrl: './integrations-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./integrations-page.component.scss']
})
export class IntegrationsPageComponent implements OnInit {
  private readonly localization = inject(LocalizationService);
  protected readonly integrationService = inject(IntegrationService);
  protected readonly configFlow = inject(ConfigFlowService);
  protected readonly compatibilityService = inject(PluginCompatibilityService);
  protected readonly installation = inject(PluginInstallationService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly fileOpen = inject(FileOpenService);
  protected readonly filters = inject(IntegrationFilterService);

  @ViewChild('artifactInput') private artifactInput?: ElementRef<HTMLInputElement>;

  protected readonly search = signal('');

  protected readonly openFilterMenu = signal<FilterFacet | null>(null);

  protected readonly configuringId = signal<string | null>(null);
  protected readonly disableTarget = signal<Integration | null>(null);

  protected readonly addMenuOpen = signal(false);
  protected readonly openMenuId = signal<string | null>(null);

  protected readonly pendingInstall =
    signal<{ source: ArchiveSource; inspection: PluginInstallActionResponse } | null>(null);
  protected readonly installBusy = signal(false);

  protected readonly inspecting = signal(false);

  private inspectToken = 0;

  protected readonly replaceTarget = signal<{ source: ArchiveSource; version: string | null } | null>(null);

  protected readonly uninstallTarget = signal<Integration | null>(null);
  protected readonly uninstallDeleteData = signal(false);
  protected readonly uninstallBusy = signal(false);

  protected readonly disableMessage = computed(() => {
    const name = this.disableTarget()?.name ?? this.localization.translateKey(AppStrings.Integrations.Page.FallbackIntegrationName);
    return this.localization.translateKey(AppStrings.Integrations.Page.DisableMessage, { name });
  });

  protected readonly replaceMessage = computed(() => {
    const version = this.replaceTarget()?.version ?? this.localization.translateKey(AppStrings.Integrations.Page.FallbackVersion);
    return this.localization.translateKey(AppStrings.Integrations.Page.ReplaceMessage, { version });
  });

  protected readonly uninstallMessage = computed(() => {
    const target = this.uninstallTarget();
    if (!target) {
      return '';
    }
    const version = this.installation.find(target.id)?.activeVersion;
    const named = version ? `${target.name} ${version}` : target.name;
    return this.localization.translateKey(AppStrings.Integrations.Page.UninstallMessage, { name: named });
  });

  protected readonly displayedIntegrations = computed(() => {
    const query = this.search().trim().toLowerCase();
    const status = this.filters.status();
    const type = this.filters.type();
    const issues = this.filters.issues();
    const capabilities = this.filters.capabilities();

    return this.integrationService.integrations()
      .filter(i => query === '' || i.name.toLowerCase().includes(query))
      .filter(i => status === 'all' || i.enabled === (status === 'enabled'))
      .filter(i => type === 'all' || i.isInternal === (type === 'internal'))
      // Several capabilities narrow rather than widen: an integration must provide every one of them.
      .filter(i => capabilities.every(kind => i.providedCapabilities.some(c => c.kind === kind)))
      .filter(i => issues === 'all' || this.hasIssues(i) === (issues === 'has'))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  protected hasIssues(integration: Integration): boolean {
    if (integration.issueCount > 0) {
      return true;
    }

    const compatibility = this.compatibilityFor(integration);
    return compatibility !== null && compatibility.state !== 'compatible';
  }

  protected readonly capabilityOptions = computed<CapabilityFilterOption[]>(() => {
    const byKind = new Map<string, LocalizedText>();
    for (const integration of this.integrationService.integrations()) {
      for (const capability of integration.providedCapabilities) {
        byKind.set(capability.kind, capability.name);
      }
    }

    return [...byKind.entries()]
      .map(([kind, name]) => ({ kind, name }))
      .sort((a, b) => this.capabilityLabel(a).localeCompare(this.capabilityLabel(b)));
  });

  private capabilityLabel(option: CapabilityFilterOption): string {
    return resolveLocalizedText(option.name, this.localization);
  }

  protected readonly statusFilterLabel = computed(() => this.optionLabel(this.filters.status(), {
    enabled: this.localization.translateKey(Strings.Common.Enabled),
    disabled: this.localization.translateKey(Strings.Common.Disabled),
  }));

  protected readonly typeFilterLabel = computed(() => this.optionLabel(this.filters.type(), {
    internal: this.localization.translateKey(AppStrings.Integrations.Page.InternalTag),
    external: this.localization.translateKey(AppStrings.Integrations.Page.ExternalTag),
  }));

  protected readonly issuesFilterLabel = computed(() => this.optionLabel(this.filters.issues(), {
    has: this.localization.translateKey(AppStrings.Integrations.Page.FilterHasIssues),
    none: this.localization.translateKey(AppStrings.Integrations.Page.FilterNoIssues),
  }));

  protected readonly capabilitiesFilterLabel = computed(() => {
    const count = this.filters.capabilities().length;
    return count === 0
      ? null
      : this.localization.translateKey(AppStrings.Integrations.Page.FilterCapabilitiesSelectedCount, { count });
  });

  private optionLabel<T extends string>(value: T, labels: Partial<Record<T, string>>): string | null {
    return labels[value] ?? null;
  }

  protected setStatusFilter(value: IntegrationStatusFilter): void {
    this.filters.status.set(value);
    this.openFilterMenu.set(null);
  }

  protected setTypeFilter(value: IntegrationTypeFilter): void {
    this.filters.type.set(value);
    this.openFilterMenu.set(null);
  }

  protected setIssuesFilter(value: IntegrationIssuesFilter): void {
    this.filters.issues.set(value);
    this.openFilterMenu.set(null);
  }

  protected setFilterMenuOpen(facet: FilterFacet, open: boolean): void {
    this.openFilterMenu.set(open ? facet : null);
  }

  async ngOnInit(): Promise<void> {
    await this.integrationService.loadIntegrations();
    // Fire-and-forget: the badge only renders once a report is in, and this cache is shared with the
    // Developer tab's Compatibility tab and the detail page, so it never blocks the list rendering.
    void this.compatibilityService.load();
    // Decides which cards may offer an uninstall, so it must not block the list either.
    void this.installation.load();
  }

  private readonly claimOpenedPlugin = effect(() => {
    if (!this.fileOpen.pending().some(entry => entry.kind === 'plugin')) {
      return;
    }

    const opened = this.fileOpen.claim('plugin');
    if (opened) {
      void this.inspectArtifact(pathSource(opened.path));
    }
  });

  protected pickArtifact(): void {
    this.addMenuOpen.set(false);
    this.artifactInput?.nativeElement.click();
  }

  protected openStore(): void {
    this.addMenuOpen.set(false);
    void this.router.navigate(['/store']);
  }

  protected async onArtifactPicked(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    await this.inspectArtifact(fileSource(file));
  }

  private async inspectArtifact(source: ArchiveSource): Promise<void> {
    const token = ++this.inspectToken;
    this.inspecting.set(true);

    // The host decides what a .macroDeckPlugin is - `accept` is a hint the user can bypass, and there
    // is no client-side validation worth trusting.
    const inspection = await this.installation.inspect(source).catch(() => null);

    if (token !== this.inspectToken) {
      return;
    }

    this.inspecting.set(false);

    if (!inspection?.success) {
      this.toast.show(this.localization.translateKey(AppStrings.Integrations.Page.InspectFailed), {
        detail: inspection?.error?.message,
        variant: 'error'
      });
      return;
    }

    this.pendingInstall.set({ source, inspection });
  }

  protected onArtifactDropped(drop: ShellDrop): void {
    void this.inspectArtifact(pathSource(drop.path));
  }

  protected cancelInstall(): void {
    this.inspectToken++;
    this.inspecting.set(false);
    this.pendingInstall.set(null);
  }

  protected cancelReplace(): void {
    this.replaceTarget.set(null);
  }

  protected async confirmInstall(force = false, allowUnsigned = false): Promise<void> {
    const source = force ? this.replaceTarget()?.source : this.pendingInstall()?.source;
    if (!source) {
      return;
    }

    this.installBusy.set(true);
    const result = await this.installation.install(source, force, allowUnsigned).catch(() => null);
    this.installBusy.set(false);

    if (!force && result?.error?.code === PLUGIN_INSTALL_ERROR_ALREADY_INSTALLED) {
      this.pendingInstall.set(null);
      this.replaceTarget.set({ source, version: result.version ?? null });
      return;
    }

    this.pendingInstall.set(null);
    this.replaceTarget.set(null);

    if (!result?.success) {
      this.toast.show(this.localization.translateKey(AppStrings.Integrations.Page.InstallFailed), {
        detail: result?.error?.message,
        variant: 'error'
      });
      return;
    }

    this.toast.show(this.localization.translateKey(AppStrings.Integrations.Page.Installed, { pluginId: result.pluginId, version: result.version }), { variant: 'success' });
    await this.integrationService.loadIntegrations();
  }

  protected canUninstall(integration: Integration): boolean {
    return !integration.isInternal && this.installation.installedIds().has(integration.id);
  }

  protected setMenuOpen(integrationId: string, open: boolean): void {
    this.openMenuId.set(open ? integrationId : null);
  }

  protected requestUninstall(integration: Integration): void {
    this.openMenuId.set(null);
    this.uninstallDeleteData.set(false);
    this.uninstallTarget.set(integration);
  }

  protected cancelUninstall(): void {
    this.uninstallTarget.set(null);
  }

  protected async confirmUninstall(): Promise<void> {
    const target = this.uninstallTarget();
    if (!target) {
      return;
    }

    this.uninstallBusy.set(true);
    const keepData = !this.uninstallDeleteData();
    const result = await this.installation.uninstall(target.id, keepData).catch(() => null);
    this.uninstallBusy.set(false);

    if (!result?.success) {
      this.toast.show(this.localization.translateKey(AppStrings.Integrations.Page.UninstallFailed), {
        detail: result?.error?.message,
        variant: 'error'
      });
      return;
    }

    this.uninstallTarget.set(null);
    this.toast.show(this.localization.translateKey(AppStrings.Integrations.Page.Uninstalled, { name: target.name }), { variant: 'success' });
    await this.integrationService.loadIntegrations();
  }

  protected iconUrl(integration: Integration): string | null {
    return integration.hasIcon ? this.api.getIntegrationIconUrl(integration.id, integration.iconVersion) : null;
  }

  protected extraCapabilities(integration: Integration): IpcProvidedCapability[] {
    return integration.providedCapabilities.filter(c => !COUNTED_CAPABILITY_KINDS.includes(c.kind));
  }

  protected readonly capabilityIcon = integrationCapabilityIcon;

  protected compatibilityFor(integration: Integration): PluginCompatibilityReport | null {
    if (integration.isInternal) {
      return null;
    }
    return this.compatibilityService.reports().find(report => report.pluginId === integration.id) ?? null;
  }

  protected compatibilityStateLabel(state: PluginCompatibilityState): string {
    return compatibilityStateLabel(state);
  }

  protected compatibilityStateTier(state: PluginCompatibilityState): string {
    return compatibilityStateTier(state);
  }

  onToggle(integration: Integration, toggle: ToggleSwitchComponent): void {
    // The switch flips itself on click; revert it for the branches that don't change the
    // enabled state directly, so the toggle never gets stuck out of sync with reality.
    if (integration.enabled) {
      toggle.checked = true;
      this.disableTarget.set(integration);
      return;
    }

    if (integration.supportsConfigFlow && integration.configuredEntryCount === 0) {
      toggle.checked = false;
      this.configuringId.set(integration.id);
      return;
    }

    void this.integrationService.toggleIntegration(integration.id);
  }

  async confirmDisable(): Promise<void> {
    const target = this.disableTarget();
    this.disableTarget.set(null);
    if (target) {
      await this.integrationService.toggleIntegration(target.id);
    }
  }

  cancelDisable(): void {
    this.disableTarget.set(null);
  }

  async onConfigFlowClosed(completed: boolean): Promise<void> {
    this.configuringId.set(null);
    if (completed) {
      await this.integrationService.loadIntegrations();
    }
  }

  openDetail(integration: Integration): void {
    void this.router.navigate(['/integrations', integration.id]);
  }

  async onShowCapabilityTab(integration: Integration, tab: 'actions' | 'variables'): Promise<void> {
    await this.router.navigate(['/integrations', integration.id], { queryParams: { tab } });
  }

  variableCountHint(integration: Integration): string {
    return integration.variablesDependOnConfiguration
      ? this.localization.translateKey(AppStrings.Integrations.Page.VariableCountHintPerConfig)
      : this.localization.translateKey(AppStrings.Integrations.Page.VariableCountHintDefault);
  }
}
