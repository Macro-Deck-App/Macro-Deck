export const COUNTED_CAPABILITY_KINDS: readonly string[] = ['actions', 'variables'];

const CAPABILITY_ICONS: Record<string, string> = {
  actions: 'zap',
  // The same glyph the action card's provider switch uses, so the capability and the control that
  // turns it on read as the same thing.
  'action-states': 'zap',
  'action-icons': 'image',
  events: 'bell',
  variables: 'sliders',
  'music-player': 'music-player-type',
  weather: 'weather-type',
  calendar: 'clock-type',
  'email-inbox': 'mail',
  'virtual-profiles': 'layers',
  'battery-provider': 'battery',
};

export function integrationCapabilityIcon(kind: string): string {
  return CAPABILITY_ICONS[kind] ?? 'puzzle';
}
