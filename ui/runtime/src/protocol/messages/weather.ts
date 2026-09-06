import { LocalizedText } from '../../localization/localized-text';
export interface WeatherForecastDayPayload {
  date: string;
  condition: string;
  min: number;
  max: number;
}

export interface WeatherStatePayload {
  instanceId?: string;
  isAvailable: boolean;
  stationExists?: boolean;
  locationName: string;
  temperature?: number;
  apparentTemperature?: number;
  condition: string;
  isDay: boolean;
  unit: string;
  days: WeatherForecastDayPayload[];
}

export interface WeatherInstanceDto {
  instanceId: string;
  integrationId: string;
  providerName: LocalizedText;
  displayName: string;
  hasIcon: boolean;
}

export interface GetWeatherInstancesResponse {
  instances: WeatherInstanceDto[];
}

export interface GetWeatherStateResponse {
  state: WeatherStatePayload;
}

export interface WeatherStateChangedNotification {
  state: WeatherStatePayload;
}

export interface WeatherInstancesChangedNotification {
  instances: WeatherInstanceDto[];
}
