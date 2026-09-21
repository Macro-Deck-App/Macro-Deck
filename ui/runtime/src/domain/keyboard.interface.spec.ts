import { AppStrings } from '../localization/generated/app-strings';
import { bundledTranslator as t } from '../localization/bundled-translator';
import { formatCombo, isSelectionModifierEvent, keyFromEvent, sidedModifier, supportedKeyGroups } from './keyboard.interface';

describe('formatCombo', () => {
  const translate = (key: string, args?: Record<string, unknown>): string =>
    key === AppStrings.Keyboard.Modifier.Right ? `Right ${args?.['modifier']}` : key;

  it('labels right-hand modifiers through the translator and generic ones as before', () => {
    expect(formatCombo(['Ctrl', 'RightAlt'], 'F1', translate)).toBe('Ctrl + Right Alt + F1');
  });
});

describe('sidedModifier', () => {
  it('names the right-hand modifier only when the right key alone is held', () => {
    expect(sidedModifier('Alt', new Set(['AltRight']))).toBe('RightAlt');
    expect(sidedModifier('Ctrl', new Set(['ControlRight']))).toBe('RightCtrl');
    expect(sidedModifier('Alt', new Set(['AltLeft']))).toBe('Alt');
    expect(sidedModifier('Alt', new Set(['AltLeft', 'AltRight']))).toBe('Alt');
    expect(sidedModifier('Alt', new Set())).toBe('Alt');
  });
});

describe('keyFromEvent', () => {
  it('maps the spacebar to the canonical "Space" key name', () => {
    const event = new KeyboardEvent('keydown', { key: ' ', code: 'Space' });
    expect(keyFromEvent(event)).toBe('Space');
  });

  it('uppercases single-character keys', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'a', code: 'KeyA' }))).toBe('A');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '5', code: 'Digit5' }))).toBe('5');
  });

  it('records the physical letter or digit when AltGr or Shift makes the key produce another character', () => {
    const altGr = { ctrlKey: true, altKey: true };
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '³', code: 'Digit3', ...altGr }))).toBe('3');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '@', code: 'KeyQ', ...altGr }))).toBe('Q');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '€', code: 'KeyE', ...altGr }))).toBe('E');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '!', code: 'Digit1', shiftKey: true }))).toBe('1');
  });

  it('records the Latin letter of the physical key on non-Latin layouts', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'й', code: 'KeyQ' }))).toBe('Q');
  });

  it('keeps the layout letter when the key already produces a letter', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'y', code: 'KeyZ' }))).toBe('Y');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'a', code: 'KeyQ' }))).toBe('A');
  });

  it('passes named keys through unchanged', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter' }))).toBe('Enter');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', code: 'ArrowUp' }))).toBe('ArrowUp');
  });

  it('maps media keys to the canonical host key name', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'MediaPlayPause', code: 'MediaPlayPause' })))
      .toBe('MediaPlayPause');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'AudioVolumeUp', code: 'AudioVolumeUp' })))
      .toBe('AudioVolumeUp');
  });

  it('prefers event.code for media keys when event.key is unreliable', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'Unidentified', code: 'MediaTrackNext' })))
      .toBe('MediaTrackNext');
  });

  it('records numpad keys as the numpad key, whatever character NumLock makes them produce', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '1', code: 'Numpad1' }))).toBe('Numpad1');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'End', code: 'Numpad1' }))).toBe('Numpad1');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '+', code: 'NumpadAdd' }))).toBe('NumpadAdd');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'NumpadEnter' }))).toBe('NumpadEnter');
  });

  it('falls back to the produced character for numpad keys the host has no name for', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '=', code: 'NumpadEqual' }))).toBe('=');
  });
});

describe('supportedKeyGroups', () => {
  it('exposes the media keys in the selector list', () => {
    const media = supportedKeyGroups(t).find(group => group.label === 'Media');
    expect(media).toBeTruthy();
    expect(media!.keys.map(k => k.value)).toEqual([
      'MediaPlayPause',
      'MediaStop',
      'MediaTrackNext',
      'MediaTrackPrevious',
      'AudioVolumeUp',
      'AudioVolumeDown',
      'AudioVolumeMute',
    ]);
  });
});

describe('isSelectionModifierEvent', () => {
  // On macOS, Ctrl+click IS the secondary click. Honouring it as "add to selection" would toggle
  // the selection and open the context menu from a single gesture (issue #213).
  it('does not treat Ctrl as a selection modifier on Apple platforms', () => {
    expect(isSelectionModifierEvent({ ctrlKey: true, metaKey: false }, true)).toBe(false);
  });

  it('treats Ctrl as a selection modifier off Apple platforms', () => {
    expect(isSelectionModifierEvent({ ctrlKey: true, metaKey: false }, false)).toBe(true);
  });

  it('treats Meta as a selection modifier on either platform', () => {
    expect(isSelectionModifierEvent({ ctrlKey: false, metaKey: true }, true)).toBe(true);
    expect(isSelectionModifierEvent({ ctrlKey: false, metaKey: true }, false)).toBe(true);
  });

  it('is not triggered by an unmodified event on either platform', () => {
    expect(isSelectionModifierEvent({ ctrlKey: false, metaKey: false }, true)).toBe(false);
    expect(isSelectionModifierEvent({ ctrlKey: false, metaKey: false }, false)).toBe(false);
  });
});
