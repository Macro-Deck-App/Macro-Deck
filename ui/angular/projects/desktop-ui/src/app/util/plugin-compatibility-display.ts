import { CompatibilityFindingSource, PluginCompatibilityReport, PluginCompatibilityState } from '@macro-deck/runtime';

const EVIDENCE_TEXT: Record<CompatibilityFindingSource, string> = {
  confirmed: "Confirmed from the plugin's build-time usage manifest.",
  negotiated: 'From protocol negotiation.',
  inferred: 'Inferred from the SDK version - this plugin may be affected.',
  unknown: 'Not reported by this plugin.',
};

const STATE_LABELS: Record<PluginCompatibilityState, string> = {
  compatible: 'Compatible',
  deprecated_apis: 'Deprecated APIs',
  update_recommended: 'Update recommended',
  update_required: 'Update required',
  partially_incompatible: 'Partially incompatible',
  incompatible: 'Incompatible',
};

const STATE_TIERS: Record<PluginCompatibilityState, 'ok' | 'warning' | 'danger'> = {
  compatible: 'ok',
  deprecated_apis: 'warning',
  update_recommended: 'warning',
  update_required: 'danger',
  partially_incompatible: 'danger',
  incompatible: 'danger',
};

const SOURCE_LABELS: Record<CompatibilityFindingSource, string> = {
  confirmed: 'Confirmed',
  negotiated: 'Negotiated',
  inferred: 'Inferred',
  unknown: 'Unknown',
};

export function compatibilityStateLabel(state: PluginCompatibilityState): string {
  return STATE_LABELS[state] ?? state;
}

export function compatibilityStateTier(state: PluginCompatibilityState): 'ok' | 'warning' | 'danger' {
  return STATE_TIERS[state] ?? 'warning';
}

export function compatibilityEvidenceText(report: PluginCompatibilityReport): string {
  return EVIDENCE_TEXT[report.usageSource] ?? EVIDENCE_TEXT.unknown;
}

export function compatibilityFindingSourceLabel(source: CompatibilityFindingSource): string {
  return SOURCE_LABELS[source] ?? source;
}
