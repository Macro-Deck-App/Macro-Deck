import { LocalizedText } from '../../localization/localized-text';
export interface MusicPlayerStatePayload {
  instanceId?: string;
  isConnected: boolean;
  isUnavailable?: boolean;
  statusMessage?: string;
  playbackState: string;
  isPlaying: boolean;
  trackName?: string;
  artistName?: string;
  albumName?: string;
  artworkId?: string;
  positionMs?: number;
  durationMs?: number;
  volume?: number;
  shuffleEnabled: boolean;
  repeatMode: string;
  deviceName?: string;
  deviceType?: string;
}

export interface MusicPlayerInstanceDto {
  instanceId: string;
  integrationId: string;
  providerName: LocalizedText;
  displayName: string;
  hasIcon: boolean;
}

export interface GetMusicPlayerInstancesResponse {
  instances: MusicPlayerInstanceDto[];
}

export interface GetMusicPlayerStateResponse {
  state: MusicPlayerStatePayload;
}

export interface MusicPlayerStateChangedNotification {
  state: MusicPlayerStatePayload;
}

export interface MusicPlayerInstancesChangedNotification {
  instances: MusicPlayerInstanceDto[];
}
