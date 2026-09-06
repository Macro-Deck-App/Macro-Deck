import { bundledTranslator as t } from '../localization/bundled-translator';
import { isSelectionModifierEvent, keyFromEvent, supportedKeyGroups } from './keyboard.interface';

describe('keyFromEvent', () => {
  it('maps the spacebar to the canonical "Space" key name', () => {
    const event = new KeyboardEvent('keydown', { key: ' ', code: 'Space' });
    expect(keyFromEvent(event)).toBe('Space');
  });

  it('uppercases single-character keys', () => {
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: 'a', code: 'KeyA' }))).toBe('A');
    expect(keyFromEvent(new KeyboardEvent('keydown', { key: '5', code: 'Digit5' }))).toBe('5');
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
