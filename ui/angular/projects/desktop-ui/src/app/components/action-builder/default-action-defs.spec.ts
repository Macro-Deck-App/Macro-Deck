import { AppStrings } from '@macro-deck/runtime';
import { comparisonOperatorOptions, fixedTriggerTabsFor } from './default-action-defs';

describe('fixedTriggerTabsFor', () => {
  const t = (key: string): string => key;

  it('gives a widget naming only the double tap exactly that tab, labelled from its localization key', () => {
    expect(fixedTriggerTabsFor(['onDoublePress'], t)).toEqual([
      { triggerType: 'onDoublePress', label: AppStrings.ActionBuilder.Trigger.DoublePress },
    ]);
  });

  it('keeps the default tabs for a widget naming press triggers, nothing, or only unknown ids', () => {
    expect(fixedTriggerTabsFor(['onShortPress', 'onLongPress', 'onTouchStart', 'onTouchEnd'], t)).toBeNull();
    expect(fixedTriggerTabsFor(undefined, t)).toBeNull();
    expect(fixedTriggerTabsFor([], t)).toBeNull();
    expect(fixedTriggerTabsFor(['onPluginGesture'], t)).toBeNull();
  });
});

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
