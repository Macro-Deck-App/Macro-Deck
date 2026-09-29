export interface GetDeviceClientSettingsResponse {
  settingsButtonHidden: boolean;
}

export interface DeviceClientSettingsChangedEvent {
  settingsButtonHidden: boolean;
}

export interface SetDeviceClientSettingsRequest {
  settingsButtonHidden: boolean;
}

export interface SetDeviceClientSettingsResponse {
  success: boolean;
}
