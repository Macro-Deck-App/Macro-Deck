import { AppStrings, AppStringsKey, IpcProvidedCapability } from '@macro-deck/runtime';
import type { SettingsCategory } from '../services/settings-modal.service';

export type CapabilityPageId = 'events' | 'widgets' | 'batteries' | 'calendars' | 'email' | 'weather' | 'music';

export type CapabilityPageGroup = 'primary' | 'data';

export interface CapabilityPage {
  id: CapabilityPageId;
  kind: string;
  group: CapabilityPageGroup;
  labelKey: AppStringsKey;
}

export const CAPABILITY_PAGES: readonly CapabilityPage[] = [
  { id: 'events', kind: 'events', group: 'primary', labelKey: AppStrings.Integrations.Capability.Events },
  { id: 'widgets', kind: 'widget-type-provider', group: 'primary', labelKey: AppStrings.Integrations.Capability.WidgetTypeProvider },
  { id: 'batteries', kind: 'battery-provider', group: 'data', labelKey: AppStrings.Integrations.Capability.BatteryProvider },
  { id: 'calendars', kind: 'calendar', group: 'data', labelKey: AppStrings.Integrations.Capability.Calendar },
  { id: 'email', kind: 'email-inbox', group: 'data', labelKey: AppStrings.Integrations.Capability.EmailInbox },
  { id: 'weather', kind: 'weather', group: 'data', labelKey: AppStrings.Integrations.Capability.Weather },
  { id: 'music', kind: 'music-player', group: 'data', labelKey: AppStrings.Integrations.Capability.MusicPlayer },
];

export const DATA_TAB_ID = 'data';

export const SETTINGS_LINKED_CAPABILITIES: Readonly<Record<string, SettingsCategory>> = {
  'device-provider': 'devices',
};

export function capabilityPagesFor(capabilities: readonly IpcProvidedCapability[] | undefined): CapabilityPage[] {
  const kinds = new Set((capabilities ?? []).map(capability => capability.kind));
  return CAPABILITY_PAGES.filter(page => kinds.has(page.kind));
}

export function capabilityPageForKind(kind: string): CapabilityPage | undefined {
  return CAPABILITY_PAGES.find(page => page.kind === kind);
}
