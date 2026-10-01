import { LocalizedText } from '../../localization/localized-text';

export type VideoStreamState = 'unavailable' | 'disconnected' | 'connecting' | 'connected';

export type VideoStreamSessionState = 'opening' | 'active' | 'suspended' | 'reconnecting';

export type VideoStreamSessionReason =
  | 'none'
  | 'provider_reconnecting'
  | 'source_lost'
  | 'source_recovered'
  | 'provider_closed'
  | 'provider_removed'
  | 'consumer_closed'
  | 'lease_expired'
  | 'consumer_disconnected'
  | 'host_disconnected'
  | 'host_shutdown'
  | 'failed';

export type VideoStreamErrorCode =
  | 'busy'
  | 'unknown_provider'
  | 'unknown_stream'
  | 'unknown_session'
  | 'session_limit_reached'
  | 'stream_unavailable'
  | 'transport_not_accepted'
  | 'provider_unavailable'
  | 'failed';

// Session pushes can arrive out of order: keep the one with the highest revision per session.
// The open response is revision 0.
export type VideoStreamSessionRevision = number;

export interface VideoStreamItem {
  id: string;
  name: LocalizedText;
  description?: LocalizedText;
  width?: number;
  height?: number;
  hasAudio: boolean;
  state: VideoStreamState;
}

export interface VideoStreamProviderItem {
  id: string;
  name: LocalizedText;
  description?: LocalizedText;
  streams: VideoStreamItem[];
}

export interface VideoStreamDescriptionMessage {
  transport: string;
  url?: string;
}

export interface GetVideoStreamsRequest {}

export interface GetVideoStreamsResponse {
  providers: VideoStreamProviderItem[];
}

export interface OpenVideoStreamRequest {
  providerId: string;
  streamId: string;
  acceptedTransports: string[];
}

export interface OpenVideoStreamResponse {
  sessionId: string;
  revision: VideoStreamSessionRevision;
  state: VideoStreamSessionState;
}

export interface KeepAliveVideoStreamRequest {
  sessionId: string;
}

export interface SuspendVideoStreamRequest {
  sessionId: string;
}

export interface ResumeVideoStreamRequest {
  sessionId: string;
}

export interface CloseVideoStreamRequest {
  sessionId: string;
}

export interface VideoStreamCatalogChangedEvent {}

export interface VideoStreamSessionChangedEvent {
  sessionId: string;
  revision: VideoStreamSessionRevision;
  state: VideoStreamSessionState;
  description?: VideoStreamDescriptionMessage;
  reason: VideoStreamSessionReason;
  message?: LocalizedText;
}

export interface VideoStreamSessionClosedEvent {
  sessionId: string;
  reason: VideoStreamSessionReason;
  error?: VideoStreamErrorCode;
  message?: LocalizedText;
}
