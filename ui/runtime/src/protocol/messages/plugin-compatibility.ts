export type PluginCompatibilityState =
  | 'compatible'
  | 'deprecated_apis'
  | 'update_recommended'
  | 'update_required'
  | 'partially_incompatible'
  | 'incompatible';

export type CompatibilityFindingSource = 'confirmed' | 'negotiated' | 'inferred' | 'unknown';

export type CompatibilitySeverity = 'info' | 'warning' | 'error';

export interface CompatibilityFinding {
  diagnosticId: string;
  source: CompatibilityFindingSource;
  severity: CompatibilitySeverity;
  subject: string;
  guidance: string;
  deprecatedIn?: string | null;
  removedIn?: string | null;
  replacement?: string | null;
  migrationUrl?: string | null;
}

export interface PluginCompatibilityReport {
  pluginId: string;
  displayName: string;
  state: PluginCompatibilityState;
  usageSource: CompatibilityFindingSource;
  sdkVersion?: string | null;
  negotiatedProtocolVersion?: number | null;
  usageTruncated: boolean;
  findings: CompatibilityFinding[];
}

export interface GetPluginCompatibilityResponse {
  plugins: PluginCompatibilityReport[];
}
