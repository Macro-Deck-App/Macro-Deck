import { AppStrings } from '../localization/generated/app-strings';

export interface KeyboardComboValue {
  modifiers: string[];
  key: string;
}

export type KeyboardStepType = 'keyCombo' | 'text' | 'delay' | 'keyDown' | 'keyUp';

export interface KeyComboStepValue {
  type: 'keyCombo';
  modifiers: string[];
  key: string;
  repeat?: number;
  repeatDelayMs?: number;
}

export interface TextStepValue {
  type: 'text';
  text: string;
}

export interface DelayStepValue {
  type: 'delay';
  milliseconds: number;
}

export interface KeyDownStepValue {
  type: 'keyDown';
  modifiers: string[];
  key: string;
}

export interface KeyUpStepValue {
  type: 'keyUp';
  modifiers: string[];
  key: string;
}

export type KeyboardStepValue =
  | KeyComboStepValue
  | TextStepValue
  | DelayStepValue
  | KeyDownStepValue
  | KeyUpStepValue;

export interface KeyboardSequenceValue {
  steps: KeyboardStepValue[];
  repeat: number;
  repeatDelayMs: number;
}

export const KEYBOARD_MODIFIERS = ['Ctrl', 'Shift', 'Alt', 'Meta'] as const;

export const RIGHT_KEYBOARD_MODIFIERS = ['RightCtrl', 'RightShift', 'RightAlt', 'RightMeta'] as const;

const MODIFIER_CODES = new Map<string, { left: string; right: string }>([
  ['Ctrl', { left: 'ControlLeft', right: 'ControlRight' }],
  ['Shift', { left: 'ShiftLeft', right: 'ShiftRight' }],
  ['Alt', { left: 'AltLeft', right: 'AltRight' }],
  ['Meta', { left: 'MetaLeft', right: 'MetaRight' }],
]);

export function sidedModifier(modifier: string, heldCodes: ReadonlySet<string>): string {
  const codes = MODIFIER_CODES.get(modifier);
  return codes && heldCodes.has(codes.right) && !heldCodes.has(codes.left) ? `Right${modifier}` : modifier;
}

export function isApplePlatform(): boolean {
  const platform = (typeof navigator !== 'undefined' ? navigator.platform : '').toLowerCase();
  return platform.includes('mac');
}

export function isSelectionModifierEvent(
  event: { ctrlKey: boolean; metaKey: boolean },
  applePlatform: boolean
): boolean {
  return applePlatform ? event.metaKey : event.ctrlKey || event.metaKey;
}

export function metaKeyLabel(): string {
  const platform = (typeof navigator !== 'undefined' ? navigator.platform : '').toLowerCase();
  if (platform.includes('mac')) return 'Command';
  if (platform.includes('win')) return 'Win';
  return 'Meta';
}

export function modifierLabel(modifier: string, t: KeyboardTranslator): string {
  const base = modifier.startsWith('Right') ? modifier.slice('Right'.length) : '';
  if (MODIFIER_CODES.has(base)) {
    return t(AppStrings.Keyboard.Modifier.Right, { modifier: modifierLabel(base, t) });
  }
  return modifier === 'Meta' ? metaKeyLabel() : modifier;
}

export function formatCombo(modifiers: string[], key: string, t: KeyboardTranslator): string {
  return [...modifiers.map(modifier => modifierLabel(modifier, t)), key].filter(Boolean).join(' + ');
}

export interface KeyOption {
  value: string;
  label: string;
}

export interface KeyGroup {
  label: string;
  keys: KeyOption[];
}

function range(values: string[]): KeyOption[] {
  return values.map(v => ({ value: v, label: v }));
}

export type KeyboardTranslator = (key: string, args?: Record<string, unknown>) => string;

export function supportedKeyGroups(t: KeyboardTranslator): KeyGroup[] {
  const G = AppStrings.Keyboard.Group;
  const M = AppStrings.Keyboard.Media;
  return [
  { label: t(G.Letters), keys: range('ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('')) },
  { label: t(G.Digits), keys: range(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']) },
  {
    label: t(G.Function),
    keys: range(Array.from({ length: 24 }, (_, i) => `F${i + 1}`)),
  },
  {
    label: t(G.Navigation),
    keys: [
      { value: 'ArrowUp', label: '↑ Up' },
      { value: 'ArrowDown', label: '↓ Down' },
      { value: 'ArrowLeft', label: '← Left' },
      { value: 'ArrowRight', label: '→ Right' },
      ...range(['Home', 'End', 'PageUp', 'PageDown']),
    ],
  },
  {
    label: t(G.Editing),
    keys: range(['Enter', 'Escape', 'Tab', 'Space', 'Backspace', 'Delete', 'Insert', 'CapsLock']),
  },
  {
    label: t(G.System),
    keys: range(['PrintScreen', 'ScrollLock', 'Pause', 'NumLock']),
  },
  {
    label: t(G.Symbols),
    keys: [
      { value: 'Minus', label: '- Minus' },
      { value: 'Equal', label: '= Equal' },
      { value: 'BracketLeft', label: '[ Bracket Left' },
      { value: 'BracketRight', label: '] Bracket Right' },
      { value: 'Backslash', label: '\\ Backslash' },
      { value: 'Semicolon', label: '; Semicolon' },
      { value: 'Quote', label: "' Quote" },
      { value: 'Comma', label: ', Comma' },
      { value: 'Period', label: '. Period' },
      { value: 'Slash', label: '/ Slash' },
      { value: 'Backquote', label: '` Backquote' },
    ],
  },
  { label: t(G.Numpad), keys: range([...NUMPAD_KEY_NAMES]) },
  {
    label: t(G.Media),
    keys: [
      { value: 'MediaPlayPause', label: t(M.PlayPause) },
      { value: 'MediaStop', label: t(M.Stop) },
      { value: 'MediaTrackNext', label: t(M.NextTrack) },
      { value: 'MediaTrackPrevious', label: t(M.PreviousTrack) },
      { value: 'AudioVolumeUp', label: t(M.VolumeUp) },
      { value: 'AudioVolumeDown', label: t(M.VolumeDown) },
      { value: 'AudioVolumeMute', label: t(M.Mute) },
    ],
  },
  ];
}

const NUMPAD_KEY_NAMES = new Set([
  'Numpad0', 'Numpad1', 'Numpad2', 'Numpad3', 'Numpad4',
  'Numpad5', 'Numpad6', 'Numpad7', 'Numpad8', 'Numpad9',
  'NumpadAdd', 'NumpadSubtract', 'NumpadMultiply', 'NumpadDivide', 'NumpadDecimal', 'NumpadEnter',
]);

const MEDIA_KEY_NAMES = new Set([
  'MediaPlayPause',
  'MediaStop',
  'MediaTrackNext',
  'MediaTrackPrevious',
  'AudioVolumeUp',
  'AudioVolumeDown',
  'AudioVolumeMute',
]);

const LETTER_OR_DIGIT = /^[A-Z0-9]$/;
const LETTER_OR_DIGIT_CODE = /^(?:Key([A-Z])|Digit([0-9]))$/;

export function keyFromEvent(event: KeyboardEvent): string {
  if (event.code === 'Space' || event.key === ' ') return 'Space';
  if (MEDIA_KEY_NAMES.has(event.code)) return event.code;
  if (MEDIA_KEY_NAMES.has(event.key)) return event.key;
  if (NUMPAD_KEY_NAMES.has(event.code)) return event.code;
  if (event.key.length === 1) {
    const key = event.key.toUpperCase();
    if (LETTER_OR_DIGIT.test(key)) return key;
    const physical = LETTER_OR_DIGIT_CODE.exec(event.code);
    if (physical) return physical[1] ?? physical[2];
    return key;
  }
  return event.key;
}
