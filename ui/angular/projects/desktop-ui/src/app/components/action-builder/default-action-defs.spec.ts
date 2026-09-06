import { AppStrings } from '@macro-deck/runtime';
import { comparisonOperatorOptions } from './default-action-defs';

describe('comparisonOperatorOptions', () => {
  it('D1: returns the full operator list, in order, an added operator cannot silently displace an existing one', () => {
    const options = comparisonOperatorOptions(key => key);

    expect(options.map(o => o.value)).toEqual([
      '==', '!=', '>', '<', '>=', '<=', 'contains', 'startsWith',
      'isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable',
    ]);
  });

  it('D1: labels the four state operators with their AppStrings.ActionBuilder.Comparison keys', () => {
    const options = comparisonOperatorOptions(key => key);
    const byValue = new Map(options.map(o => [o.value, o.label]));

    expect(byValue.get('isEmpty')).toBe(AppStrings.ActionBuilder.Comparison.IsEmpty);
    expect(byValue.get('isNotEmpty')).toBe(AppStrings.ActionBuilder.Comparison.IsNotEmpty);
    expect(byValue.get('isAvailable')).toBe(AppStrings.ActionBuilder.Comparison.IsAvailable);
    expect(byValue.get('isNotAvailable')).toBe(AppStrings.ActionBuilder.Comparison.IsNotAvailable);
  });
});
