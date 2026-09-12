import { AppStrings } from '@macro-deck/runtime';
import type { ActionBlockDefinition, ComparisonOperator } from '@macro-deck/runtime';

export interface TriggerTab {
  triggerType: string;
  label: string;
}

export const DEFAULT_TRIGGER_TYPE = 'onShortPress';

type Translator = (key: string) => string;

export function defaultTriggerTabs(t: Translator): readonly TriggerTab[] {
  const T = AppStrings.ActionBuilder.Trigger;
  return [
    { triggerType: DEFAULT_TRIGGER_TYPE, label: t(T.ShortPress) },
    { triggerType: 'onLongPress', label: t(T.LongPress) },
    { triggerType: 'onTouchStart', label: t(T.TouchStart) },
    { triggerType: 'onTouchEnd', label: t(T.TouchEnd) },
  ];
}

export function toggleTriggerTab(t: Translator): TriggerTab {
  return {
    triggerType: 'onStateChange',
    label: t(AppStrings.ActionBuilder.Trigger.OnStateChange),
  };
}

export const TOGGLE_TRIGGER_TYPE = 'onStateChange';

export function widgetTriggerCatalog(t: Translator): readonly TriggerTab[] {
  return [
    ...defaultTriggerTabs(t),
    toggleTriggerTab(t),
    { triggerType: 'onDoublePress', label: t(AppStrings.ActionBuilder.Trigger.DoublePress) },
  ];
}

export function fixedTriggerTabsFor(triggers: readonly string[] | undefined, t: Translator): TriggerTab[] | null {
  const named = triggers ?? [];
  const presses = new Set(defaultTriggerTabs(t).map(tab => tab.triggerType));
  if (named.length === 0 || named.some(trigger => presses.has(trigger))) return null;

  const tabs = widgetTriggerCatalog(t).filter(tab => named.includes(tab.triggerType));
  return tabs.length > 0 ? tabs : null;
}

export function comparisonOperatorOptions(t: Translator): ReadonlyArray<{ label: string; value: ComparisonOperator }> {
  const C = AppStrings.ActionBuilder.Comparison;
  return [
    { label: t(C.Is),                  value: '==' },
    { label: t(C.IsNot),                value: '!=' },
    { label: t(C.GreaterThan),          value: '>'  },
    { label: t(C.LessThan),             value: '<'  },
    { label: t(C.GreaterThanOrEqual),   value: '>=' },
    { label: t(C.LessThanOrEqual),      value: '<=' },
    { label: t(C.Contains),             value: 'contains'   },
    { label: t(C.StartsWith),           value: 'startsWith' },
    { label: t(C.IsEmpty),              value: 'isEmpty'         },
    { label: t(C.IsNotEmpty),           value: 'isNotEmpty'      },
    { label: t(C.IsAvailable),          value: 'isAvailable'     },
    { label: t(C.IsNotAvailable),       value: 'isNotAvailable'  },
  ];
}

export function eventFilterOperatorOptions(t: Translator): ReadonlyArray<{ label: string; value: ComparisonOperator }> {
  return comparisonOperatorOptions(t).filter(o => o.value !== 'contains' && o.value !== 'startsWith');
}

export function defaultActionDefs(t: Translator): ActionBlockDefinition[] {
  const A = AppStrings.ActionBuilder.Defaults;
  return [
    {
      blockType: 'wait',
      type: 'delay',
      label: t(A.Wait),
      color: '#8b5cf6',
      category: t(A.CategoryLogic),
      categoryId: 'Logic',
      parameters: [
        { name: 'duration', type: 'number', label: t(A.DurationMs), min: 0, max: 60000, step: 100 },
      ],
    },
    {
      blockType: 'ifElse',
      type: 'condition',
      label: t(A.IfElse),
      color: '#f59e0b',
      category: t(A.CategoryLogic),
      categoryId: 'Logic',
      hasBranches: true,
    },
    {
      blockType: 'repeatLoop',
      type: 'loop',
      label: t(A.Repeat),
      color: '#22c55e',
      category: t(A.CategoryLogic),
      categoryId: 'Logic',
      hasChildren: true,
      parameters: [
        { name: 'count', type: 'number', label: t(A.Times), min: 1, max: 100 },
      ],
    },
    {
      blockType: 'whileLoop',
      type: 'loop',
      label: t(A.While),
      color: '#22c55e',
      category: t(A.CategoryLogic),
      categoryId: 'Logic',
      hasChildren: true,
      hasCondition: true,
    },
    {
      blockType: 'break',
      type: 'flow-control',
      label: t(A.Break),
      color: '#ef4444',
      category: t(A.CategoryLogic),
      categoryId: 'Logic',
      blockShape: 'cap',
      loopOnly: true,
    },
  ];
}
