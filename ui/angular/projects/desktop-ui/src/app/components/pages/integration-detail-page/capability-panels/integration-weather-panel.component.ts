import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { AppStrings, WeatherInstanceDto } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { VariableGroupHeaderComponent } from '../../../variables/variable-group-header.component';
import { VariableRowComponent } from '../../../variables/variable-row.component';
import { WeatherService } from '../../../../services/weather.service';

const CONDITION_LABELS: Record<string, string> = {
  clear: AppStrings.Widgets.Weather.Condition.Clear,
  'mainly-clear': AppStrings.Widgets.Weather.Condition.MainlyClear,
  'partly-cloudy': AppStrings.Widgets.Weather.Condition.PartlyCloudy,
  overcast: AppStrings.Widgets.Weather.Condition.Overcast,
  fog: AppStrings.Widgets.Weather.Condition.Fog,
  drizzle: AppStrings.Widgets.Weather.Condition.Drizzle,
  rain: AppStrings.Widgets.Weather.Condition.Rain,
  'freezing-rain': AppStrings.Widgets.Weather.Condition.FreezingRain,
  snow: AppStrings.Widgets.Weather.Condition.Snow,
  'snow-grains': AppStrings.Widgets.Weather.Condition.SnowGrains,
  'rain-showers': AppStrings.Widgets.Weather.Condition.RainShowers,
  'snow-showers': AppStrings.Widgets.Weather.Condition.SnowShowers,
  thunderstorm: AppStrings.Widgets.Weather.Condition.Thunderstorm,
};

@Component({
  selector: 'app-integration-weather-panel',
  standalone: true,
  imports: [EmptyStateComponent, LoadingStateComponent, RailItemComponent, TranslatePipe, VariableGroupHeaderComponent, VariableRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-weather-panel.component.html',
  styleUrls: ['./capability-panel.scss'],
})
export class IntegrationWeatherPanelComponent implements OnInit {
  readonly integrationId = input.required<string>();
  readonly available = input(true);

  private readonly weather = inject(WeatherService);
  private readonly localization = inject(LocalizationService);

  protected readonly loading = signal(true);

  protected readonly stations = computed<WeatherInstanceDto[]>(() =>
    this.weather.instances().filter(instance => instance.integrationId === this.integrationId()));

  private readonly selectedId = signal<string | null>(null);

  protected readonly station = computed<WeatherInstanceDto | null>(() => {
    const stations = this.stations();
    return stations.find(station => station.instanceId === this.selectedId()) ?? stations[0] ?? null;
  });

  protected readonly state = computed(() => {
    const station = this.station();
    return station ? this.weather.stateFor(station.instanceId) : null;
  });

  constructor() {
    effect(() => {
      const station = this.station();
      if (station) {
        void this.weather.ensureState(station.instanceId);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.weather.start();
    await this.weather.loadInstances();
    this.loading.set(false);
  }

  protected select(station: WeatherInstanceDto): void {
    this.selectedId.set(station.instanceId);
  }

  protected condition(slug: string): string {
    return this.localization.translateKey(CONDITION_LABELS[slug] ?? AppStrings.Widgets.Weather.Condition.Unknown);
  }

  protected temperature(value: number | undefined | null): string {
    return value === undefined || value === null
      ? this.localization.translateKey(AppStrings.Integrations.Detail.ValueUnknown)
      : this.localization.translateKey(AppStrings.Widgets.Weather.TemperatureValue, { value: Math.round(value) });
  }

  protected weekday(date: string): string {
    const parsed = new Date(`${date}T12:00:00`);
    return Number.isNaN(parsed.getTime())
      ? date
      : parsed.toLocaleDateString(this.localization.timeLocale().locale, { weekday: 'long', day: 'numeric', month: 'short' });
  }
}
