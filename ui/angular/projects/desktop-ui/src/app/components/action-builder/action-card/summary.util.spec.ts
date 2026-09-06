import { ActionBlockParameter } from '@macro-deck/runtime';
import { summarizeParameters } from './summary.util';

function param(partial: Partial<ActionBlockParameter>): ActionBlockParameter {
  return { name: 'p', type: 'dynamic-choice', label: 'Folder', value: '', ...partial };
}

const t = (key: string): string => key;

describe('summarizeParameters', () => {
  it('shows the cached label instead of the raw id for a host-backed option', () => {
    const p = param({ value: 'b3f1c2a4-0000-0000-0000-000000000000', valueLabel: 'Living Room' });
    expect(summarizeParameters([p], t)).toBe('Folder: Living Room');
  });

  it('falls back to the raw value when no label was cached', () => {
    const p = param({ value: 'raw-id', valueLabel: undefined });
    expect(summarizeParameters([p], t)).toBe('Folder: raw-id');
  });

  it('ignores a stale label when the value is a variable reference', () => {
    const p = param({ value: { $var: 'target' }, valueLabel: 'Living Room' });
    expect(summarizeParameters([p], t)).toBe('Folder: {{ vars.target }}');
  });

  it('joins multiple parameters, resolving labels per parameter', () => {
    const summary = summarizeParameters([
      param({ label: 'Track', value: 'spotify:track:xyz', valueLabel: 'Bohemian Rhapsody' }),
      param({ name: 'v', type: 'number', label: 'Volume', value: 50 }),
    ], t);
    expect(summary).toBe('Track: Bohemian Rhapsody · Volume: 50');
  });
});
