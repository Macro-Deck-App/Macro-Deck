import { applyConfigDraftEvent, composeConfigDraft } from './config-draft.util';
import { UiConfigEvents } from './config-events';
import { UiConfigPrimitives } from './config-primitives';
import { UiNode, UiNodeEvent } from '../ui-framework/ui-node.interface';

function allKeys(value: unknown): string[] {
  if (Array.isArray(value)) return value.flatMap(allKeys);
  if (value === null || typeof value !== 'object') return [];

  const record = value as Record<string, unknown>;
  return Object.keys(record).flatMap(key => [key, ...allKeys(record[key])]);
}

describe('applyConfigDraftEvent', () => {
  // The shape production actually serves: a widget-configuration root whose two regions share one
  // input-id namespace, because neither region opens an input-id scope. An implementation that only
  // descended into the first child, or that scoped ids by region and wrote `editor.flows`, passes
  // every single-region test above and then writes keys no widget schema has.
  it('reaches an input in either region of a widget configuration root', () => {
    const root: UiNode = {
      id: 'root',
      type: UiConfigPrimitives.WidgetConfiguration,
      children: [
        {
          id: 'properties',
          type: UiConfigPrimitives.WidgetProperties,
          children: [{ id: 'label', type: UiConfigPrimitives.String }],
        },
        {
          id: 'editor',
          type: UiConfigPrimitives.WidgetEditor,
          children: [{ id: 'flows', type: UiConfigPrimitives.ActionsListEditor }],
        },
      ],
    };
    const data = { label: 'Before', flows: [], historyLength: 120 };

    const fromProperties = applyConfigDraftEvent(root, data,
      { nodeId: 'label', name: UiConfigEvents.Change, data: 'After' });
    const fromEditor = applyConfigDraftEvent(root, fromProperties,
      { nodeId: 'flows', name: UiConfigEvents.Change, data: [{ trigger: 'onShortPress' }] });

    expect(fromEditor).toEqual({
      label: 'After',
      flows: [{ trigger: 'onShortPress' }],
      historyLength: 120,
    });
    expect(allKeys(fromEditor).some(key => key.includes('.'))).toBeFalse();
  });

  // The whole reason this module exists: a flat implementation keyed by `event.nodeId` would write
  // `{"border.style": "comet"}` here, which every widget schema's unset `additionalProperties`
  // happily accepts and silently persists (no validation error, no save failure - just a widget
  // that renders nothing next time it loads). Composing by walking the tree's `object` scope
  // instead must land the value at `data.border.style`.
  it('composes a nested object edit into nested JSON, not a flat dotted key', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'label', type: UiConfigPrimitives.String },
        {
          id: 'border',
          type: UiConfigPrimitives.Object,
          children: [
            { id: 'border.style', type: UiConfigPrimitives.Choice },
            { id: 'border.color', type: UiConfigPrimitives.Color },
          ],
        },
      ],
    };
    const data = { label: 'Foo', border: { style: 'solid', color: '#ffffff', width: 2 } };
    const event: UiNodeEvent = { nodeId: 'border.style', name: UiConfigEvents.Change, data: 'comet' };

    const result = applyConfigDraftEvent(root, data, event);

    expect(result).toEqual({ label: 'Foo', border: { style: 'comet', color: '#ffffff', width: 2 } });
    expect('border.style' in result).toBeFalse();
    expect(allKeys(result).some(key => key.includes('.'))).toBeFalse();
  });

  // `a.b` is a single DSL key, valid because `UiIdentifier` allows a literal `.` inside a key - not
  // two keys `a` and `b`. Composing by splitting the id string could never tell the two apart;
  // walking the tree from the root (where this input has no enclosing `object`/`array` scope) can.
  it('does not split a top-level id that itself contains a literal dot', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'a.b', type: UiConfigPrimitives.String }],
    };
    const event: UiNodeEvent = { nodeId: 'a.b', name: UiConfigEvents.Change, data: 'value' };

    const result = applyConfigDraftEvent(root, {}, event);

    expect(result).toEqual({ 'a.b': 'value' });
    expect(Object.keys(result)).toEqual(['a.b']);
  });

  describe('an array scope', () => {
    const statesTree = (order: readonly ['off', 'on'] | readonly ['on', 'off']): UiNode => {
      const item = (key: 'off' | 'on'): UiNode => ({
        id: `states.${key}`,
        type: UiConfigPrimitives.Object,
        children: [
          {
            id: `states.${key}.appearance`,
            type: UiConfigPrimitives.Object,
            children: [{ id: `states.${key}.appearance.labelColor`, type: UiConfigPrimitives.Color }],
          },
        ],
      });

      return {
        id: 'root',
        type: 'stack',
        children: [{ id: 'states', type: UiConfigPrimitives.Array, children: order.map(item) }],
      };
    };

    const initialData = {
      states: [
        { id: 'off', appearance: { labelColor: '#000000' } },
        { id: 'on', appearance: { labelColor: '#ffffff' } },
      ],
    };

    // The counterexample that makes item-key matching mandatory: an index-keyed composer would
    // still pass the edit above, then silently swap "off" and "on"'s values the moment the tree's
    // child order changes underneath it - which a provider reordering the list does routinely and
    // without any event of its own to hook.
    it('round-trips items by their own key - editing one, reordering, then removing it', () => {
      const editEvent: UiNodeEvent = {
        nodeId: 'states.on.appearance.labelColor',
        name: UiConfigEvents.Change,
        data: '#00ff00',
      };

      const afterEdit = applyConfigDraftEvent(statesTree(['off', 'on']), initialData, editEvent);

      expect(afterEdit['states']).toEqual([
        { id: 'off', appearance: { labelColor: '#000000' } },
        { id: 'on', appearance: { labelColor: '#00ff00' } },
      ]);

      const afterReorder = applyConfigDraftEvent(statesTree(['on', 'off']), afterEdit, editEvent);

      expect(afterReorder['states']).toEqual([
        { id: 'on', appearance: { labelColor: '#00ff00' } },
        { id: 'off', appearance: { labelColor: '#000000' } },
      ]);
      // Exactly the pre-edit value, with nothing added to it.
      expect((afterReorder['states'] as unknown[])[1]).toEqual(initialData.states[0]);

      const removeEvent: UiNodeEvent = { nodeId: 'states', name: UiConfigEvents.Remove, data: 'states.on' };
      const afterRemove = applyConfigDraftEvent(statesTree(['on', 'off']), afterReorder, removeEvent);

      expect(afterRemove['states']).toEqual([{ id: 'off', appearance: { labelColor: '#000000' } }]);
    });
  });

  // A widget's data carries whatever it (or an older version of it) chose to store; nothing about
  // rendering one field's control may forget the fields that field's tree says nothing about.
  it('leaves keys the tree does not mention untouched', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'volume', type: UiConfigPrimitives.Number }],
    };
    const data = { historyLength: 120, 'x-experimental': { k: 1 }, volume: 5 };
    const event: UiNodeEvent = { nodeId: 'volume', name: UiConfigEvents.Change, data: 10 };

    const result = applyConfigDraftEvent(root, data, event);

    expect(result).toEqual({ historyLength: 120, 'x-experimental': { k: 1 }, volume: 10 });
  });
});

// A provider may write several keys at once in response to an event that is not itself a value edit -
// applying a preset, adopting a state provider. Those arrive as patches to the tree's own input values
// and never as `change` events, so a draft folded only from events silently keeps the old values: the
// form visibly updates, the user saves, and the write is lost with nothing red anywhere.
describe('composeConfigDraft', () => {
  it('folds values the provider wrote itself into the draft', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        // What the tree looks like after a preset button applied four keys at once.
        { id: 'valueVariable', type: UiConfigPrimitives.VariablePicker, properties: { value: 'cpu.load', events: ['change'] } },
        { id: 'title', type: UiConfigPrimitives.String, properties: { value: 'CPU', events: ['change'] } },
        { id: 'maxValue', type: UiConfigPrimitives.Number, properties: { value: 100, events: ['change'] } },
      ],
    };
    const data = { valueVariable: '', title: '', maxValue: 0, historyLength: 120 };

    const result = composeConfigDraft(root, data);

    expect(result).toEqual({
      valueVariable: 'cpu.load',
      title: 'CPU',
      maxValue: 100,
      historyLength: 120,
    });
  });

  it('leaves a key alone when its node cannot be edited', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        // Chrome carrying a `value` property that is not the widget's data - a copy-value block.
        { id: 'redirect', type: UiConfigPrimitives.CopyValue, properties: { value: 'https://localhost' } },
      ],
    };

    expect(composeConfigDraft(root, { title: 'Kept' })).toEqual({ title: 'Kept' });
  });

  it('composes nested and repeated structure the same way an edit does', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'border',
          type: UiConfigPrimitives.Object,
          children: [{ id: 'border.style', type: UiConfigPrimitives.Choice, properties: { value: 'comet', events: ['change'] } }],
        },
        {
          id: 'states',
          type: UiConfigPrimitives.Array,
          children: [
            {
              id: 'states.on',
              type: UiConfigPrimitives.Object,
              children: [{ id: 'states.on.label', type: UiConfigPrimitives.String, properties: { value: 'On', events: ['change'] } }],
            },
          ],
        },
      ],
    };
    const data = { border: { style: 'solid', color: '#fff' }, states: [{ id: 'on', label: 'Old' }] };

    const result = composeConfigDraft(root, data);

    expect(result).toEqual({
      border: { style: 'comet', color: '#fff' },
      states: [{ id: 'on', label: 'On' }],
    });
    expect(allKeys(result).some(key => key.includes('.'))).toBeFalse();
  });

  // The trap ActionButtonWidgetConfigView's state-section rebuild has to avoid (issue #837): every
  // state contributes an item node so this array reconciliation is never asked to rebuild the array
  // from fewer items than it actually has, but only the *selected* state's item additionally carries
  // a rename field and an appearance subtree - the rest are bare id-only items. Adding a second state
  // and then editing the newly-selected one must leave the first exactly as it was, not drop it.
  it('preserves an unselected state in full when a newly-selected sibling gains a rename and an appearance edit', () => {
    const selectedItem = (key: 'off' | 'on'): UiNode => ({
      id: `states.${key}`,
      type: UiConfigPrimitives.Object,
      children: [
        { id: `states.${key}.label`, type: UiConfigPrimitives.String, properties: { value: 'On', events: ['change'] } },
        {
          id: `states.${key}.appearance`,
          type: UiConfigPrimitives.Object,
          children: [
            {
              id: `states.${key}.appearance.backgroundColor`,
              type: UiConfigPrimitives.Color,
              properties: { value: '#ff0000', events: ['change'] },
            },
          ],
        },
      ],
    });
    const unselectedItem = (key: 'off' | 'on'): UiNode => ({ id: `states.${key}`, type: UiConfigPrimitives.Object, children: [] });

    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'states', type: UiConfigPrimitives.Array, children: [unselectedItem('off'), selectedItem('on')] }],
    };
    const data = {
      states: [
        { id: 'off', label: 'Off', appearance: { backgroundColor: '#000000' } },
        { id: 'on', label: 'On' },
      ],
    };

    const result = composeConfigDraft(root, data);

    expect(result['states']).toEqual([
      { id: 'off', label: 'Off', appearance: { backgroundColor: '#000000' } },
      { id: 'on', label: 'On', appearance: { backgroundColor: '#ff0000' } },
    ]);
  });
});

// An Action Button with no icon is what forced this: `icon` is a typed {type, reference} reference with
// no null form, so a null there fails the widget schema - which rejected the editor's live preview and
// would have rejected the save too. No widget schema requires any key, so leaving one out is always the
// safe reading of "nothing is chosen here".
describe('an unset value', () => {
  const iconNode: UiNode = {
    id: 'icon',
    type: UiConfigPrimitives.Icon,
    properties: { events: ['change'] },
  };

  it('is left out of the draft rather than written as null', () => {
    const root: UiNode = { id: 'root', type: 'stack', children: [{ ...iconNode, properties: { events: ['change'], value: null } }] };

    const result = composeConfigDraft(root, { icon: { type: 'icon-pack', reference: 'old' }, label: 'Keep' });

    expect('icon' in result).toBeFalse();
    expect(result).toEqual({ label: 'Keep' });
  });

  it('removes the key when a control is cleared', () => {
    const root: UiNode = { id: 'root', type: 'stack', children: [iconNode] };
    const data = { icon: { type: 'icon-pack', reference: 'bolt' }, label: 'Keep' };

    const result = applyConfigDraftEvent(root, data, { nodeId: 'icon', name: UiConfigEvents.Change, data: null });

    expect('icon' in result).toBeFalse();
    expect(result).toEqual({ label: 'Keep' });
  });

  it('removes a nested key without disturbing its siblings', () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{
        id: 'border',
        type: UiConfigPrimitives.Object,
        children: [{ id: 'border.color', type: UiConfigPrimitives.Color, properties: { events: ['change'] } }],
      }],
    };
    const data = { border: { style: 'comet', color: '#ff0000' } };

    const result = applyConfigDraftEvent(root, data,
      { nodeId: 'border.color', name: UiConfigEvents.Change, data: null });

    expect(result).toEqual({ border: { style: 'comet' } });
  });
});

// A colour has no empty form: a widget with backgroundColor "" paints nothing, where one with no
// backgroundColor at all paints its default. A label is different - an empty label is a real answer.
describe('an empty colour', () => {
  const tree = (type: string, id: string): UiNode => ({
    id: 'root', type: 'stack',
    children: [{ id, type, properties: { events: ['change'], value: '' } }],
  });

  it('is left out of the draft, so the widget falls back to its default', () => {
    expect(composeConfigDraft(tree(UiConfigPrimitives.Color, 'backgroundColor'), { backgroundColor: '#123456' }))
      .toEqual({});
  });

  it('does not apply to text, where empty is a real answer', () => {
    expect(composeConfigDraft(tree(UiConfigPrimitives.String, 'label'), { label: 'Old' }))
      .toEqual({ label: '' });
  });
});

// An enumerated field's schema lists its values and no empty one, so an empty choice is not merely odd -
// it fails validation and takes the whole save with it. That is what made a multi-state Action Button
// unsavable the moment a state was added.
describe('an empty choice', () => {
  const node = (type: string, id: string): UiNode =>
    ({ id: 'root', type: 'stack', children: [{ id, type, properties: { events: ['change'], value: '' } }] });

  it('is left out rather than written as an empty enum value', () => {
    expect(composeConfigDraft(node(UiConfigPrimitives.Choice, 'textAlign'), { textAlign: 'left' })).toEqual({});
  });

  it('applies to the pickers too', () => {
    expect(composeConfigDraft(node(UiConfigPrimitives.VariablePicker, 'valueVariable'), { valueVariable: 'v' })).toEqual({});
  });

  it('still leaves free text alone, so a label can be cleared', () => {
    expect(composeConfigDraft(node(UiConfigPrimitives.String, 'label'), { label: 'Old' })).toEqual({ label: '' });
  });
});

// A state list binds its whole value at once: the provider owns the ids and every state, while the tree
// only renders an item node for the state being edited. Rebuilding the array from those nodes kept one
// state, dropped the rest and lost their ids - which then failed the widget schema outright.
describe('a composite that binds its whole value', () => {
  const statesTree = (rendered: string): UiNode => ({
    id: 'root', type: 'stack',
    children: [{
      id: 'states',
      type: UiConfigPrimitives.Array,
      properties: {
        events: ['change'],
        value: [{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }],
      },
      children: [{
        id: `states.${rendered}`,
        type: UiConfigPrimitives.Object,
        children: [{ id: `states.${rendered}.label`, type: UiConfigPrimitives.String, properties: { value: 'On', events: ['change'] } }],
      }],
    }],
  });

  it('keeps every item, and their ids, when only one is rendered', () => {
    const result = composeConfigDraft(statesTree('on'), { states: [{ id: 'off', label: 'Off' }] });

    expect(result['states']).toEqual([{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }]);
  });
});

// Editing inside a composite that binds its whole value must not rebuild it from the rendered children:
// the provider owns the ids and the items the form is not showing, and its next patch carries the result.
it('leaves a whole-bound composite to its provider when an edit lands inside it', () => {
  const root: UiNode = {
    id: 'root', type: 'stack',
    children: [{
      id: 'states', type: UiConfigPrimitives.Array,
      properties: { events: ['change'], value: [{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }] },
      children: [{
        id: 'states.on', type: UiConfigPrimitives.Object,
        children: [{ id: 'states.on.label', type: UiConfigPrimitives.String, properties: { events: ['change'] } }],
      }],
    }],
  };
  const data = { states: [{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }] };

  const result = applyConfigDraftEvent(root, data,
    { nodeId: 'states.on.label', name: UiConfigEvents.Change, data: 'Renamed' });

  expect(result['states']).toEqual([{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }]);
});

// A transient input (UiInput.Transient, issue #837) is operated and raises change like any other input,
// but stands for no stored key of its own - the Action Button's font family selector, which exists only
// to offer a control for a value derived from `fontFaceId` rather than stored itself. Its value must never
// reach the draft, from either the compose path (opening the editor, saving unedited) or the event path
// (the family picker itself firing a change).
describe('a transient input', () => {
  const root: UiNode = {
    id: 'root', type: 'stack',
    children: [
      { id: 'fontFamily', type: UiConfigPrimitives.Choice, properties: { events: ['change'], value: 'Inter', transient: true } },
      { id: 'fontFaceId', type: UiConfigPrimitives.Choice, properties: { events: ['change'], value: 'inter-400' } },
    ],
  };

  it('contributes no key when the draft is composed from the tree', () => {
    expect(composeConfigDraft(root, {})).toEqual({ fontFaceId: 'inter-400' });
  });

  it('is ignored when it fires its own change event', () => {
    const data = { fontFaceId: 'inter-400' };

    const result = applyConfigDraftEvent(root, data,
      { nodeId: 'fontFamily', name: UiConfigEvents.Change, data: 'Acme' });

    expect(result).toEqual(data);
    expect(result).toBe(data);
  });

  it('does not stop a sibling change from applying', () => {
    const result = applyConfigDraftEvent(root, { fontFaceId: 'inter-400' },
      { nodeId: 'fontFaceId', name: UiConfigEvents.Change, data: 'acme-700' });

    expect(result).toEqual({ fontFaceId: 'acme-700' });
  });
});
