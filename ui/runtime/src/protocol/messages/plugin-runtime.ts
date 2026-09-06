import { ApiError, ResultResponse } from './common';

export type PluginRuntimeState = 'stopped' | 'starting' | 'running' | 'stopping' | 'backoff' | 'failed';

export type PluginRuntimeHealth = 'unknown' | 'healthy' | 'degraded' | 'unhealthy' | 'crashed' | 'failed';

export type PluginRuntimeStopReason =
  | 'none'
  | 'user_requested'
  | 'host_shutdown'
  | 'update'
  | 'manual_restart'
  | 'crash'
  | 'health_failure'
  | 'launch_failure';

export interface PluginRuntimeInfo {
  pluginId: string;
  displayName: string;
  version: string;
  state: PluginRuntimeState;
  health: PluginRuntimeHealth;
  managed: boolean;
  processId?: number | null;
  launchId?: string | null;
  startedAt?: string | null;
  lastExitCode?: number | null;
  lastStopReason: PluginRuntimeStopReason;
  lastExitAt?: string | null;
  lastHeartbeatAt?: string | null;
  lastHealthCheckAt?: string | null;
  consecutiveHealthFailures: number;
  restartCount: number;
  nextRestartAt?: string | null;
  lastError?: string | null;
  bootstrapOutput: string[];
}

export interface GetPluginRuntimeResponse {
  plugins: PluginRuntimeInfo[];
}

export interface PluginRuntimeOperationResponse extends ResultResponse {
  error?: ApiError;
}
