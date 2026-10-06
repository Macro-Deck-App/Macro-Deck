import { AppStrings } from '@macro-deck/runtime';
import { comparisonOperatorOptions, fixedTriggerTabsFor, interactionTriggerTabsFor } from './default-action-defs';

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
    expect(fixedTriggerTabsFor(['onShortPress', 'onLongPress', 'onTouchStart', 'onTouchEnd', 'onDoublePress'], t)).toBeNull();
  });
});

describe('interactionTriggerTabsFor', () => {
  const t = (key: string): string => key;
  const T = AppStrings.ActionBuilder.Trigger;

  it('gives a countdown its own labelled triggers with the one the widget names first as the default', () => {
    expect(interactionTriggerTabsFor([
      'onCountdownFinished', 'onCountdownStarted', 'onCountdownPaused', 'onCountdownReset', 'onCountdownDismissed',
    ], t)).toEqual([
      { triggerType: 'onCountdownFinished', label: T.CountdownFinished },
      { triggerType: 'onCountdownStarted', label: T.TimerStarted },
      { triggerType: 'onCountdownPaused', label: T.TimerPaused },
      { triggerType: 'onCountdownReset', label: T.TimerReset },
      { triggerType: 'onCountdownDismissed', label: T.CountdownDismissed },
    ]);
  });

  it('gives a stopwatch its started, paused and reset triggers', () => {
    expect(interactionTriggerTabsFor(['onStopwatchStarted', 'onStopwatchPaused', 'onStopwatchReset'], t)!
      .map(tab => tab.triggerType)).toEqual(['onStopwatchStarted', 'onStopwatchPaused', 'onStopwatchReset']);
  });

  it('leaves every other widget to the press triggers', () => {
    expect(interactionTriggerTabsFor(undefined, t)).toBeNull();
    expect(interactionTriggerTabsFor(['onDoublePress'], t)).toBeNull();
    expect(interactionTriggerTabsFor(['onShortPress', 'onLongPress'], t)).toBeNull();
  });

  it('keeps the timer triggers out of the fixed tabs', () => {
    expect(fixedTriggerTabsFor(['onStopwatchStarted', 'onStopwatchPaused', 'onStopwatchReset'], t)).toBeNull();
  });
});

describe('calendar widget trigger tabs', () => {
  const t = (key: string): string => key;
  const T = AppStrings.ActionBuilder.Trigger;
  const calendarWidgetTriggers = [
    'onShortPress', 'onLongPress', 'onDoublePress', 'onTouchStart', 'onTouchEnd',
    'onCalendarEventStartsSoon', 'onCalendarEventStarted', 'onCalendarEventEnded',
  ];

  it('gives a calendar widget the five press tabs followed by its three calendar triggers, Short Press first', () => {
    expect(interactionTriggerTabsFor(calendarWidgetTriggers, t)).toEqual([
      { triggerType: 'onShortPress', label: T.ShortPress },
      { triggerType: 'onLongPress', label: T.LongPress },
      { triggerType: 'onTouchStart', label: T.TouchStart },
      { triggerType: 'onTouchEnd', label: T.TouchEnd },
      { triggerType: 'onDoublePress', label: T.DoublePress },
      { triggerType: 'onCalendarEventStartsSoon', label: T.CalendarEventStartsSoon },
      { triggerType: 'onCalendarEventStarted', label: T.CalendarEventStarted },
      { triggerType: 'onCalendarEventEnded', label: T.CalendarEventEnded },
    ]);
    expect(fixedTriggerTabsFor(calendarWidgetTriggers, t)).toBeNull();
  });

  it('leaves the tab sets of buttons, weather, timers and sliders as they were', () => {
    const actionButton = ['onShortPress', 'onLongPress', 'onTouchStart', 'onTouchEnd', 'onDoublePress'];
    expect(interactionTriggerTabsFor(actionButton, t)).toBeNull();
    expect(fixedTriggerTabsFor(actionButton, t)).toBeNull();
    expect(interactionTriggerTabsFor([...actionButton, 'onStateChange'], t)).toBeNull();
    expect(fixedTriggerTabsFor([...actionButton, 'onStateChange'], t)).toBeNull();

    expect(interactionTriggerTabsFor(undefined, t)).toBeNull();
    expect(fixedTriggerTabsFor(undefined, t)).toBeNull();

    expect(interactionTriggerTabsFor(['onStopwatchStarted', 'onStopwatchPaused', 'onStopwatchReset'], t)!
      .map(tab => tab.triggerType)).toEqual(['onStopwatchStarted', 'onStopwatchPaused', 'onStopwatchReset']);

    expect(interactionTriggerTabsFor(['onDoublePress'], t)).toBeNull();
    expect(fixedTriggerTabsFor(['onDoublePress'], t)!.map(tab => tab.triggerType)).toEqual(['onDoublePress']);
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
