import { ActionButtonData, ButtonState, SliderData, WidgetType } from './widget.interface';
import { defaultWidgetData, fromEditorJson, parseWidgetData, serializeWidgetData, toEditorJson } from './widget-serialization';

function expectRoundTrip(
  type: WidgetType,
  fixtureBag: Record<string, unknown>,
  edit?: { key: string; value: unknown },
): void {
  const fixtureJson = JSON.stringify(fixtureBag);
  const data = parseWidgetData(type, fixtureJson);

  const editorJson = toEditorJson(type, data);
  const editorBag: Record<string, unknown> = JSON.parse(editorJson);
  if (edit) {
    editorBag[edit.key] = edit.value;
  }

  const resultData = fromEditorJson(type, JSON.stringify(editorBag));
  const resultBag: Record<string, unknown> = JSON.parse(serializeWidgetData(type, resultData));
  const originalBag: Record<string, unknown> = JSON.parse(fixtureJson);
  const expectedBag = edit ? { ...originalBag, [edit.key]: edit.value } : originalBag;

  expect(withoutFlows(resultBag)).toEqual(withoutFlows(expectedBag));

  if ('flows' in originalBag) {
    expect(typeof resultBag['flows']).toBe('string');
    expect(JSON.parse(resultBag['flows'] as string)).toEqual(JSON.parse(originalBag['flows'] as string));
  } else {
    expect('flows' in resultBag).toBeFalse();
  }
}

function withoutFlows(bag: Record<string, unknown>): Record<string, unknown> {
  const { flows: _flows, ...rest } = bag;
  return rest;
}

function saved(type: WidgetType, bag: Record<string, unknown>): Record<string, unknown> {
  return JSON.parse(serializeWidgetData(type, parseWidgetData(type, JSON.stringify(bag))));
}

function stateById(states: unknown, id: string): { id: string; label: string; appearance?: Record<string, unknown> } {
  return (states as { id: string; label: string; appearance?: Record<string, unknown> }[]).find(s => s.id === id)!;
}

describe('toEditorJson / fromEditorJson round trip', () => {
  // A realistic persisted ActionButton payload already in the post-#612 schema (stateMode +
  // array-of-states), plus an unknown forward-compatible property. The legacy mode/isToggled/
  // offState/onState/object-shaped-states aliases are exercised separately below - they upgrade on
  // read rather than round-tripping unchanged, so they do not belong in this "no-op" fixture.
  const actionButtonFixture = {
    label: 'Toggle Light',
    imageUrl: 'icon-abc',
    backgroundColor: '#112233',
    labelColor: '#ffffff',
    stateMode: true,
    activeStateId: 'on',
    states: [
      { id: 'off', label: 'Off', appearance: { backgroundColor: '#112233', label: 'Off' } },
      { id: 'on', label: 'On', appearance: { backgroundColor: '#ef4444', label: 'On' } },
    ],
    flows: JSON.stringify([{ id: 'f1', type: 'action', blockType: 'system.kill-process', children: [] }]),
    futureThing: { x: 1 },
  };

  const musicPlayerFixture = {
    instanceId: 'player-1',
    coverStyle: 'full',
    showHeader: false,
    showTitle: false,
    showArtist: false,
    showAlbum: false,
    showTimeline: false,
    border: { style: 'blink', color: '#ff0000' },
    flows: JSON.stringify([{ id: 'mf1' }]),
    futureThing: { note: 'music extra' },
  };

  const sliderFixture = {
    label: 'Volume',
    orientation: 'vertical',
    color: '#123456',
    labelColor: '#ffffff',
    backgroundColor: '#000000',
    icon: { type: 'icon-pack', reference: 'icon-1' },
    showLabel: false,
    showValue: true,
    border: { style: 'comet', color: '#22c55e' },
    action: { integrationId: 'int', actionId: 'act', valueParameter: 'volume', parameters: {} },
    valueVariable: 'obs_input_volume',
    min: -60,
    max: 0,
    step: 0.5,
    futureThing: { note: 'slider extra' },
  };

  const weatherFixture = {
    instanceId: 'station-1',
    showIcon: false,
    showTemperature: false,
    showCondition: false,
    showForecast: false,
    forecastDays: 3,
    border: { style: 'ants', color: '#00ff00' },
    flows: JSON.stringify([{ id: 'wf1' }]),
    futureThing: { note: 'weather extra' },
  };

  const historyGraphFixture = {
    valueVariable: 'custom_metric',
    title: 'Custom',
    subtitleVariable: 'custom_sub',
    subtitle: 'fallback',
    showSubtitle: false,
    unit: 'MB',
    accentColor: '#abcdef',
    maxValue: 500,
    historyLength: 20,
    border: { style: 'rgb' },
    flows: JSON.stringify([{ id: 'hf1' }]),
    futureThing: { note: 'history extra' },
  };

  const clockFixture = {
    style: 'analog',
    timeZone: 'America/New_York',
    showLabel: true,
    label: 'NYC',
    showSeconds: false,
    showDate: false,
    border: { style: 'heartbeat', color: '#ffffff' },
    flows: JSON.stringify([{ id: 'cf1' }]),
    futureThing: { note: 'clock extra' },
  };

  it('leaves an untouched ActionButton payload unchanged, including unknown keys', () => {
    expectRoundTrip(WidgetType.ActionButton, actionButtonFixture);
  });

  it('changes exactly one property (ActionButton): the rest, including futureThing and flows, is untouched', () => {
    expectRoundTrip(WidgetType.ActionButton, actionButtonFixture, { key: 'label', value: 'New Label' });
  });

  it('changes exactly one property (MusicPlayer)', () => {
    expectRoundTrip(WidgetType.MusicPlayer, musicPlayerFixture, { key: 'instanceId', value: 'player-2' });
  });

  it('changes exactly one property (Slider)', () => {
    expectRoundTrip(WidgetType.Slider, sliderFixture, { key: 'label', value: 'New Volume' });
  });

  it('changes exactly one property (Weather)', () => {
    expectRoundTrip(WidgetType.Weather, weatherFixture, { key: 'instanceId', value: 'station-2' });
  });

  it('changes exactly one property (HistoryGraph)', () => {
    expectRoundTrip(WidgetType.HistoryGraph, historyGraphFixture, { key: 'title', value: 'New Title' });
  });

  it('changes exactly one property (Clock)', () => {
    expectRoundTrip(WidgetType.Clock, clockFixture, { key: 'label', value: 'LA' });
  });
});

describe('flows normalization', () => {
  it('shows a nested flows JSON string as a real array in the editor JSON', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'X',
      flows: '[{"id":"f1"}]',
    }));

    const editorBag = JSON.parse(toEditorJson(WidgetType.ActionButton, data));

    expect(Array.isArray(editorBag.flows)).toBeTrue();
    expect(editorBag.flows).toEqual([{ id: 'f1' }]);
  });

  it('writes flows back as a nested JSON string that parses to the same array', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'X',
      flows: '[{"id":"f1"}]',
    }));
    const editorJson = toEditorJson(WidgetType.ActionButton, data);

    const resultData = fromEditorJson(WidgetType.ActionButton, editorJson);
    const resultBag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, resultData));

    expect(typeof resultBag.flows).toBe('string');
    expect(JSON.parse(resultBag.flows)).toEqual([{ id: 'f1' }]);
  });

  ([
    [WidgetType.Clock, { style: 'digital' }],
    [WidgetType.MusicPlayer, { coverStyle: 'small', showHeader: true }],
    [WidgetType.Weather, { showIcon: true, forecastDays: 5 }],
    [WidgetType.HistoryGraph, { valueVariable: 'system_cpu_usage_percent' }],
    [WidgetType.Slider, { label: 'Volume', orientation: 'horizontal' }],
  ] as const).forEach(([type, persisted]) => {
    it(`never invents a flows key for a widget that has none (${type})`, () => {
      const data = parseWidgetData(type, JSON.stringify(persisted));
      expect('flows' in (data as Record<string, unknown>)).toBeFalse();

      const editorJson = toEditorJson(type, data);
      expect('flows' in JSON.parse(editorJson)).toBeFalse();

      const resultData = fromEditorJson(type, editorJson);
      expect('flows' in (resultData as Record<string, unknown>)).toBeFalse();
      expect(resultData).toEqual(data);
    });
  });
});

describe('ActionButton: offState/onState and imageId are stripped, never written (legacy read path)', () => {
  it('drops offState/onState on save; a legacy states bag is not merged with the stale alias', () => {
    const bag = saved(WidgetType.ActionButton, {
      mode: 'toggle',
      states: {
        off: { label: 'Off', backgroundColor: '#111' },
        on: { label: 'On', backgroundColor: '#ef4444' },
      },
      offState: { label: 'STALE' },
      onState: { label: 'STALE' },
    });

    expect('offState' in bag).toBeFalse();
    expect('onState' in bag).toBeFalse();
    expect(bag['states']).toEqual([
      { id: 'off', label: 'Off', appearance: { label: 'Off', backgroundColor: '#111' } },
      { id: 'on', label: 'On', appearance: { label: 'On', backgroundColor: '#ef4444' } },
    ]);
  });

  it('seeds states from offState/onState when states itself is absent', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'toggle',
      offState: { label: 'Off', backgroundColor: '#111' },
      onState: { label: 'On' },
    })) as ActionButtonData;

    expect(stateById(data.states, 'off').appearance!['label']).toBe('Off');
    expect(stateById(data.states, 'off').appearance!['backgroundColor']).toBe('#111');
    expect(stateById(data.states, 'on').appearance!['label']).toBe('On');

    const bag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, data));
    expect(bag.states).toBeDefined();
    expect('offState' in bag).toBeFalse();
    expect('onState' in bag).toBeFalse();
  });

  it('prefers a legacy states bag over offState/onState when both are present', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'toggle',
      states: { off: { label: 'NEW' }, on: { label: 'NEW-ON' } },
      offState: { label: 'OLD' },
      onState: { label: 'OLD-ON' },
    })) as ActionButtonData;

    expect(stateById(data.states, 'off').appearance!['label']).toBe('NEW');
    expect(stateById(data.states, 'on').appearance!['label']).toBe('NEW-ON');
  });

  it('folds imageId into imageUrl and never writes imageId back', () => {
    const bag = saved(WidgetType.ActionButton, { label: 'X', imageUrl: 'icon-abc', imageId: 'icon-abc' });

    expect(bag['imageUrl']).toBe('icon-abc');
    expect('imageId' in bag).toBeFalse();
  });

  it('imageUrl wins when both are present; imageId alone still resolves imageUrl', () => {
    const onlyId = parseWidgetData(WidgetType.ActionButton, JSON.stringify({ imageId: 'only-id' })) as ActionButtonData;
    expect(onlyId.imageUrl).toBe('only-id');

    const both = parseWidgetData(WidgetType.ActionButton,
      JSON.stringify({ imageUrl: 'the-url', imageId: 'the-id' })) as ActionButtonData;
    expect(both.imageUrl).toBe('the-url');

    expect('imageId' in JSON.parse(serializeWidgetData(WidgetType.ActionButton, onlyId))).toBeFalse();
    expect('imageId' in JSON.parse(serializeWidgetData(WidgetType.ActionButton, both))).toBeFalse();
  });
});

describe('ActionButton: legacy font fields are stripped (issue #457)', () => {
  it('parses a payload carrying only legacy fontFamily/fontBold/fontItalic keys without throwing, and drops them on save', () => {
    const bag = saved(WidgetType.ActionButton, {
      label: 'X',
      fontFamily: 'Arial',
      fontBold: true,
      fontItalic: false,
      states: {
        off: { fontFamily: 'Arial', fontBold: true },
        on: { fontFamily: 'Arial', fontItalic: true },
      },
    });

    expect(bag['label']).toBe('X');
    expect('fontFamily' in bag).toBeFalse();
    expect('fontBold' in bag).toBeFalse();
    expect('fontItalic' in bag).toBeFalse();
    expect('labelFont' in bag).toBeFalse();
    const off = stateById(bag['states'], 'off');
    const on = stateById(bag['states'], 'on');
    expect('fontFamily' in off.appearance!).toBeFalse();
    expect('fontBold' in off.appearance!).toBeFalse();
    expect('fontFamily' in on.appearance!).toBeFalse();
    expect('fontItalic' in on.appearance!).toBeFalse();
  });

  it('does not mutate the live states object while stripping legacy keys on save', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'X',
      stateMode: true,
      states: [{ id: 'off', label: 'Off' }, { id: 'on', label: 'On' }],
    })) as ActionButtonData;
    // Simulate a hand-built ActionButtonData (as an editor's in-memory draft might carry, bypassing
    // the parser) that still has a legacy key on a live state's appearance.
    const off = data.states!.find(s => s.id === 'off')!;
    off.appearance = { fontFamily: 'Arial' } as ButtonState;

    serializeWidgetData(WidgetType.ActionButton, data);

    expect((off.appearance as unknown as Record<string, unknown>)['fontFamily']).toBe('Arial');
  });

  it('leaves fontFaceId untouched through the round trip', () => {
    const bag = saved(WidgetType.ActionButton, {
      label: 'X',
      fontFaceId: 'helvetica-neue-700-5-upright',
      states: { off: { fontFaceId: 'helvetica-neue-400-5-upright' }, on: {} },
    });

    expect(bag['fontFaceId']).toBe('helvetica-neue-700-5-upright');
    expect(stateById(bag['states'], 'off').appearance!['fontFaceId']).toBe('helvetica-neue-400-5-upright');
  });
});

describe('ActionButton: the icon has exactly one home per mode (legacy read path)', () => {
  it('a hand-edited stateMode:false -> true switches the icon into states[0], off and on both exist', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      stateMode: false,
      iconId: 'icon-1',
      iconDisplay: { zoom: 150 },
    }));

    const editorBag = JSON.parse(toEditorJson(WidgetType.ActionButton, data));
    editorBag.stateMode = true;
    const resultData = fromEditorJson(WidgetType.ActionButton, JSON.stringify(editorBag));
    const bag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, resultData)) as {
      states: { id: string; appearance?: { icon?: { type: string; reference: string }; iconDisplay?: unknown } }[];
      icon?: unknown;
      iconId?: unknown;
      iconDisplay?: unknown;
    };

    const off = stateById(bag.states, 'off');
    const on = stateById(bag.states, 'on');
    expect(off.appearance?.['icon']).toEqual({ type: 'icon-pack', reference: 'icon-1' });
    expect(off.appearance?.['iconDisplay']).toEqual({ zoom: 150 });
    expect(on.appearance?.['icon']).toBeUndefined();
    expect(bag.icon).toBeUndefined();
    expect(bag.iconId).toBeUndefined();
    expect(bag.iconDisplay).toBeUndefined();
  });

  it('a hand-edited stateMode:true -> false moves states[0]\'s icon to the root; the other state keeps its own', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'toggle',
      states: { off: { iconId: 'icon-off' }, on: { iconId: 'icon-on' } },
    })) as ActionButtonData;

    const editorBag = JSON.parse(toEditorJson(WidgetType.ActionButton, data));
    editorBag.stateMode = false;
    const resultData = fromEditorJson(WidgetType.ActionButton, JSON.stringify(editorBag)) as ActionButtonData;

    expect(resultData.icon).toEqual({ type: 'icon-pack', reference: 'icon-off' });
    expect(resultData.iconId).toBeUndefined();
    expect(resultData.states!.find(s => s.id === 'off')!.appearance?.icon).toBeUndefined();
    expect(resultData.states!.find(s => s.id === 'on')!.appearance?.icon).toEqual({ type: 'icon-pack', reference: 'icon-on' });
  });

  it('moves only the icon out of a State-Mode-off bag\'s dormant states[0], leaving the rest untouched', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'momentary',
      states: {
        off: { iconId: 'icon-1', iconDisplay: { opacity: 30 }, backgroundColor: '#222' },
        on: {},
      },
    })) as ActionButtonData;

    expect(data.icon).toEqual({ type: 'icon-pack', reference: 'icon-1' });
    expect(data.iconDisplay).toEqual({ opacity: 30 });
    const off = data.states!.find(s => s.id === 'off')!;
    expect(off.appearance?.icon).toBeUndefined();
    expect(off.appearance?.backgroundColor).toBe('#222');
  });

  it('survives a hand-edited legacy bag whose states object is missing a side, rather than resetting the widget', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'toggle', label: 'Keep me', iconId: 'icon-1', states: { on: {} },
    })) as ActionButtonData;

    expect(data.label).toBe('Keep me');
    expect(data.states!.find(s => s.id === 'off')!.appearance?.icon).toEqual({ type: 'icon-pack', reference: 'icon-1' });
    expect(data.states!.find(s => s.id === 'on')).toBeDefined();
  });

  it('lets the root icon win over a dormant states[0] icon, keeping the other state\'s intact', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'momentary',
      iconId: 'root-icon',
      states: { off: { iconId: 'dormant-icon', label: 'Off' }, on: { iconId: 'on-icon' } },
    })) as ActionButtonData;

    expect(data.icon).toEqual({ type: 'icon-pack', reference: 'root-icon' });
    const off = data.states!.find(s => s.id === 'off')!;
    expect(off.appearance?.icon).toBeUndefined();
    expect(off.appearance?.label).toBe('Off');
    expect(data.states!.find(s => s.id === 'on')!.appearance?.icon).toEqual({ type: 'icon-pack', reference: 'on-icon' });
  });

  it('keeps the framing of a legacy imageUrl button, which stores framing with no icon id at all', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      mode: 'momentary',
      imageUrl: 'https://example.invalid/legacy.png',
      states: { off: { iconDisplay: { fit: 'cover' } }, on: {} },
    })) as ActionButtonData;

    expect(data.iconDisplay).toEqual({ fit: 'cover' });
    expect(data.icon).toBeUndefined();
    expect(data.states!.find(s => s.id === 'off')!.appearance?.iconDisplay).toBeUndefined();
  });

  it('root appearance fields round-trip unchanged for a State-Mode button', () => {
    const input = {
      mode: 'toggle',
      label: 'Root Label',
      backgroundColor: '#123456',
      fontSize: 18,
      textAlign: 'right',
      border: { style: 'static', color: '#ffffff' },
      states: { off: {}, on: {} },
    };

    const bag = saved(WidgetType.ActionButton, input);

    expect(bag['label']).toBe(input.label);
    expect(bag['backgroundColor']).toBe(input.backgroundColor);
    expect(bag['fontSize']).toBe(input.fontSize);
    expect(bag['textAlign']).toBe(input.textAlign);
    expect(bag['border']).toEqual(input.border);
  });

  it('a State-Mode-off bag with a root icon and a full legacy states object keeps states untouched aside from the icon fields', () => {
    const input = {
      mode: 'momentary',
      iconId: 'i',
      states: {
        off: { backgroundColor: '#111', label: 'Off' },
        on: { backgroundColor: '#ef4444', label: 'On' },
      },
    };

    const bag = saved(WidgetType.ActionButton, input);

    expect(bag['states']).toEqual([
      { id: 'off', label: 'Off', appearance: { backgroundColor: '#111', label: 'Off' } },
      { id: 'on', label: 'On', appearance: { backgroundColor: '#ef4444', label: 'On' } },
    ]);
    expect(bag['icon']).toEqual({ type: 'icon-pack', reference: 'i' });
    expect('iconId' in bag).toBeFalse();
  });
});

describe('the widget icon reference model (issue #425)', () => {
  it('migrates a legacy root iconId (ActionButton) to the typed icon shape, with no oscillation across a second load/save', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      stateMode: false,
      iconId: 'ICON_A',
      iconDisplay: { fit: 'cover', zoom: 140, offsetX: -12, offsetY: 8, opacity: 85 },
      label: 'Play',
    })) as ActionButtonData;

    expect(data.icon).toEqual({ type: 'icon-pack', reference: 'ICON_A' });
    expect(data.iconId).toBeUndefined();
    expect(data.iconDisplay).toEqual({ fit: 'cover', zoom: 140, offsetX: -12, offsetY: 8, opacity: 85 });

    const savedBag = serializeWidgetData(WidgetType.ActionButton, data);
    const reloaded = parseWidgetData(WidgetType.ActionButton, savedBag) as ActionButtonData;

    expect(reloaded.icon).toEqual(data.icon!);
    expect(reloaded.iconDisplay).toEqual(data.iconDisplay!);
  });

  it('migrates a legacy per-state iconId (ActionButton), and the non-cascading state keeps no icon at all', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      stateMode: true,
      iconId: 'ICON_C',
      states: [
        { id: 'on', label: 'On', appearance: { iconId: 'ICON_A' } },
        { id: 'off', label: 'Off', appearance: {} },
      ],
    })) as ActionButtonData;

    expect(data.states!.find(s => s.id === 'on')!.appearance!.icon).toEqual({ type: 'icon-pack', reference: 'ICON_A' });
    expect(data.states!.find(s => s.id === 'off')!.appearance?.icon).toBeUndefined();
    expect(data.icon).toBeUndefined();
  });

  it('migrates a legacy root iconId on a Slider to the typed icon shape', () => {
    const data = parseWidgetData(WidgetType.Slider, JSON.stringify({
      label: 'Volume',
      iconId: 'ICON_B',
    })) as SliderData;

    expect(data.icon).toEqual({ type: 'icon-pack', reference: 'ICON_B' });
    expect(data.iconId).toBeUndefined();

    const bag = JSON.parse(serializeWidgetData(WidgetType.Slider, data));
    expect(bag.icon).toEqual({ type: 'icon-pack', reference: 'ICON_B' });
    expect('iconId' in bag).toBeFalse();
  });

  it('round-trips a non-GUID reference character-identical, without a GUID-format error blocking the save', () => {
    const nonGuid = { type: 'plugin-asset', reference: 'spotify:album/4aawyAB9vmqN3uQ7FjRGTy' };
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'Now Playing',
      icon: nonGuid,
    })) as ActionButtonData;

    expect(data.icon).toEqual(nonGuid);

    const bag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, data));
    expect(bag.icon).toEqual(nonGuid);
    expect('iconId' in bag).toBeFalse();
  });

  it('keeps an unknown provider type intact through an unrelated edit, rather than collapsing it to a closed enum', () => {
    const unknownProvider = { type: 'future-provider', reference: 'x' };
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'Original',
      icon: unknownProvider,
    })) as ActionButtonData;

    const relabeled: ActionButtonData = { ...data, label: 'Renamed' };
    const bag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, relabeled));

    expect(bag.label).toBe('Renamed');
    expect(bag.icon).toEqual(unknownProvider);
  });

  it('keeps a dangling icon-pack reference stored rather than clearing it on save', () => {
    const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({
      label: 'X',
      iconId: 'does-not-exist',
    })) as ActionButtonData;

    const bag = JSON.parse(serializeWidgetData(WidgetType.ActionButton, data));

    expect(bag.icon).toEqual({ type: 'icon-pack', reference: 'does-not-exist' });
  });
});

describe('fromEditorJson with unparseable text', () => {
  it('throws rather than falling back to defaults or an empty object', () => {
    expect(() => fromEditorJson(WidgetType.ActionButton, '{ not valid json')).toThrow();
  });
});

// A widget type this client was never compiled with - a plugin's - has no `case` in any of the three
// switches. The codec must not fold it into an Action Button's shape: that would both fabricate
// fields the plugin never wrote and, on save, discard whatever its own configuration tree composed.
describe('an unregistered widget type', () => {
  const CUSTOM_TYPE = 'app.example.custom-widget';

  it('round-trips its data through parse and serialize without loss', () => {
    const persisted = { title: 'Custom', threshold: 42, nested: { on: true } };

    const data = parseWidgetData(CUSTOM_TYPE, JSON.stringify(persisted));
    const resaved = JSON.parse(serializeWidgetData(CUSTOM_TYPE, data));

    expect(resaved).toEqual(persisted);
  });

  it('defaults to an empty object rather than an Action Button bag', () => {
    expect(defaultWidgetData(CUSTOM_TYPE)).toEqual({});
  });

  it('parses missing data as an empty object rather than an Action Button bag', () => {
    expect(parseWidgetData(CUSTOM_TYPE, undefined)).toEqual({});
  });
});
