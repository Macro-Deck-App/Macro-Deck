import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { AppStrings, IntegrationBatteryChargeState, IntegrationBatteryHealth, IpcIntegrationBattery } from '@macro-deck/runtime';
import { ApiService, ErrorBannerComponent, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { VariableRowComponent } from '../../../variables/variable-row.component';
import { CapabilityDataLoader } from './capability-data-loader';

const KIND_LABELS: Record<string, string> = {
  phone: AppStrings.Integrations.Detail.Batteries.Kind.Phone,
  tablet: AppStrings.Integrations.Detail.Batteries.Kind.Tablet,
  laptop: AppStrings.Integrations.Detail.Batteries.Kind.Laptop,
  headphones: AppStrings.Integrations.Detail.Batteries.Kind.Headphones,
  watch: AppStrings.Integrations.Detail.Batteries.Kind.Watch,
  controller: AppStrings.Integrations.Detail.Batteries.Kind.Controller,
  mouse: AppStrings.Integrations.Detail.Batteries.Kind.Mouse,
  keyboard: AppStrings.Integrations.Detail.Batteries.Kind.Keyboard,
  speaker: AppStrings.Integrations.Detail.Batteries.Kind.Speaker,
};

const CHARGE_STATE_LABELS: Record<IntegrationBatteryChargeState, string> = {
  Unknown: AppStrings.Integrations.Detail.ValueUnknown,
  Discharging: AppStrings.Integrations.Detail.Batteries.State.Discharging,
  Charging: AppStrings.Widgets.Battery.State.Charging,
  Full: AppStrings.Widgets.Battery.State.Full,
  NotCharging: AppStrings.Widgets.Battery.State.NotCharging,
};

const HEALTH_LABELS: Record<IntegrationBatteryHealth, string> = {
  Unknown: AppStrings.Integrations.Detail.ValueUnknown,
  Good: AppStrings.Integrations.Detail.Batteries.Health.Good,
  Overheat: AppStrings.Integrations.Detail.Batteries.Health.Overheat,
  Dead: AppStrings.Integrations.Detail.Batteries.Health.Dead,
  OverVoltage: AppStrings.Integrations.Detail.Batteries.Health.OverVoltage,
  Cold: AppStrings.Integrations.Detail.Batteries.Health.Cold,
  Failure: AppStrings.Integrations.Detail.Batteries.Health.Failure,
};

interface BatteryRow {
  battery: IpcIntegrationBattery;
  details: string;
  state: string;
  stateTone: 'Ready' | 'Muted';
  health: string | null;
  level: string;
}

@Component({
  selector: 'app-integration-batteries-panel',
  standalone: true,
  imports: [EmptyStateComponent, ErrorBannerComponent, LoadingStateComponent, TranslatePipe, VariableRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-batteries-panel.component.html',
  styleUrls: ['./capability-panel.scss'],
})
export class IntegrationBatteriesPanelComponent {
  readonly integrationId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  protected readonly loader = new CapabilityDataLoader('battery-provider',
    id => this.api.getIntegrationBatteries(id));

  protected readonly rows = computed<BatteryRow[]>(() =>
    (this.loader.data()?.batteries ?? []).map(battery => this.toRow(battery)));

  constructor() {
    effect(() => {
      const id = this.integrationId();
      untracked(() => void this.loader.load(id));
    });
  }

  private toRow(battery: IpcIntegrationBattery): BatteryRow {
    const t = (key: string, args?: Record<string, unknown>) => this.localization.translateKey(key, args);
    const level = battery.levelPercent;
    const kind = t(KIND_LABELS[battery.kind] ?? AppStrings.Integrations.Detail.Batteries.Kind.Other);
    const estimate = this.estimate(battery);
    const updated = t(AppStrings.Integrations.Detail.Batteries.UpdatedAt, { time: this.formatTime(battery.updatedAt) });
    const charging = battery.chargeState === 'Charging' || battery.chargeState === 'Full';
    return {
      battery,
      details: [kind, estimate, updated].filter(part => !!part).join(' · '),
      state: battery.isConnected
        ? t(CHARGE_STATE_LABELS[battery.chargeState] ?? AppStrings.Integrations.Detail.ValueUnknown)
        : t(AppStrings.Widgets.Battery.State.NotConnected),
      stateTone: charging ? 'Ready' : 'Muted',
      health: battery.health === 'Unknown' || battery.health === 'Good'
        ? null
        : t(HEALTH_LABELS[battery.health] ?? AppStrings.Integrations.Detail.ValueUnknown),
      level: level === null || level === undefined
        ? t(AppStrings.Integrations.Detail.ValueUnknown)
        : t(AppStrings.Integrations.Detail.Batteries.LevelValue, { level }),
    };
  }

  private estimate(battery: IpcIntegrationBattery): string | null {
    const charging = battery.chargeState === 'Charging';
    const seconds = charging ? battery.timeToFullSeconds : battery.timeRemainingSeconds;
    if (!battery.isConnected || seconds === null || seconds === undefined || seconds <= 0) {
      return null;
    }

    const totalMinutes = Math.round(seconds / 60);
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    const key = charging
      ? hours > 0 ? AppStrings.Widgets.Battery.State.FullInHours : AppStrings.Widgets.Battery.State.FullInMinutes
      : hours > 0 ? AppStrings.Widgets.Battery.State.HoursLeft : AppStrings.Widgets.Battery.State.MinutesLeft;
    return this.localization.translateKey(key, { hours, minutes });
  }

  private formatTime(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return date.toLocaleTimeString(locale, { hourCycle, hour: 'numeric', minute: '2-digit', second: '2-digit' });
  }
}
