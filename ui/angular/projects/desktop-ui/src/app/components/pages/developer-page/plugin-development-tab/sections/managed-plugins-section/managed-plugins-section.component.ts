import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, PluginRuntimeInfo } from '@macro-deck/runtime';
import { ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../../../feedback/empty-state/empty-state.component';
import { DropdownMenuComponent } from '../../../../../overlay/dropdown-menu/dropdown-menu.component';
import { PluginRuntimeService } from '../../../../../../services/plugin-runtime.service';

const STARTABLE_STATES = new Set(['stopped', 'failed']);
const STOPPABLE_STATES = new Set(['starting', 'running', 'backoff']);
const RESTARTABLE_STATES = new Set(['starting', 'running', 'backoff', 'failed']);

@Component({
  selector: 'app-managed-plugins-section',
  standalone: true,
  imports: [
    DropdownMenuComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './managed-plugins-section.component.html',
  styleUrls: ['./managed-plugins-section.component.scss'],
})
export class ManagedPluginsSectionComponent {
  protected readonly runtimeService = inject(PluginRuntimeService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly plugins = computed(() =>
    [...this.runtimeService.plugins()].sort((a, b) => a.displayName.localeCompare(b.displayName)));

  readonly expandedOutputIds = signal<ReadonlySet<string>>(new Set());

  readonly openMenuPluginId = signal<string | null>(null);

  isBusy(pluginId: string): boolean {
    return this.runtimeService.busy().has(pluginId);
  }

  setMenuOpen(pluginId: string, open: boolean): void {
    this.openMenuPluginId.set(open ? pluginId : null);
  }

  canStart(plugin: PluginRuntimeInfo): boolean {
    return plugin.managed && STARTABLE_STATES.has(plugin.state) && !this.isBusy(plugin.pluginId);
  }

  canStop(plugin: PluginRuntimeInfo): boolean {
    return plugin.managed && STOPPABLE_STATES.has(plugin.state) && !this.isBusy(plugin.pluginId);
  }

  canRestart(plugin: PluginRuntimeInfo): boolean {
    return plugin.managed && RESTARTABLE_STATES.has(plugin.state) && !this.isBusy(plugin.pluginId);
  }

  unmanagedReason(plugin: PluginRuntimeInfo): string | null {
    return plugin.managed
      ? null
      : this.localization.translateKey(AppStrings.Developer.ManagedPlugins.UnmanagedReason);
  }

  hasBootstrapOutput(plugin: PluginRuntimeInfo): boolean {
    return plugin.bootstrapOutput.length > 0;
  }

  isOutputExpanded(pluginId: string): boolean {
    return this.expandedOutputIds().has(pluginId);
  }

  toggleOutput(pluginId: string): void {
    const next = new Set(this.expandedOutputIds());
    if (next.has(pluginId)) {
      next.delete(pluginId);
    } else {
      next.add(pluginId);
    }
    this.expandedOutputIds.set(next);
  }

  async start(plugin: PluginRuntimeInfo): Promise<void> {
    this.openMenuPluginId.set(null);
    const response = await this.runtimeService.start(plugin.pluginId);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Developer.ManagedPlugins.StartFailed),
        { variant: 'error' });
    }
  }

  async stop(plugin: PluginRuntimeInfo): Promise<void> {
    this.openMenuPluginId.set(null);
    const response = await this.runtimeService.stop(plugin.pluginId);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Developer.ManagedPlugins.StopFailed),
        { variant: 'error' });
    }
  }

  async restart(plugin: PluginRuntimeInfo): Promise<void> {
    this.openMenuPluginId.set(null);
    const response = await this.runtimeService.restart(plugin.pluginId);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Developer.ManagedPlugins.RestartFailed),
        { variant: 'error' });
    }
  }

  absoluteTime(iso: string | null | undefined): string {
    if (!iso) {
      return '-';
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '-';
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', hourCycle });
  }
}
