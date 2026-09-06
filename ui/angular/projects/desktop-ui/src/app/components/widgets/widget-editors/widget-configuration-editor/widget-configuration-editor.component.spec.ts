import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  GridWidget, UiConfigEntryPoints, UiConfigEvents, UiConfigPrimitives, UiConfigProperties, UiNode, UiNodeEvent,
  WidgetData, WidgetType,
} from '@macro-deck/runtime';
import {
  ApiService, UiNodeEventBus, UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService,
  WidgetTypeCatalogService, WidgetTypeInfo,
} from '@shared';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';
import { UiRenderContext } from '../../../ui-render/ui-render-context';
import { WidgetConfigurationEditorComponent } from './widget-configuration-editor.component';

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly sent: UiNodeEvent[] = [];
  closed = false;

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }
}

function widget(overrides: Partial<GridWidget> = {}): GridWidget {
  return {
    id: 'w1', folderId: 'f1', x: 0, y: 0, w: 2, h: 2, type: WidgetType.Clock, data: { label: 'Before' },
    ...overrides,
  };
}

function stringField(id: string, value: string, extra: Record<string, unknown> = {}): UiNode {
  return {
    id,
    type: UiConfigPrimitives.String,
    properties: { [UiConfigProperties.Events]: [UiConfigEvents.Change], [UiConfigProperties.Value]: value, ...extra },
  };
}

function colorField(id: string, value: string): UiNode {
  return {
    id,
    type: UiConfigPrimitives.Color,
    properties: { [UiConfigProperties.Events]: [UiConfigEvents.Change], [UiConfigProperties.Value]: value },
  };
}

function propertiesRegion(children: UiNode[]): UiNode {
  return { id: 'properties', type: UiConfigPrimitives.WidgetProperties, children };
}

function editorRegion(children: UiNode[]): UiNode {
  return { id: 'editor', type: UiConfigPrimitives.WidgetEditor, children };
}

function configRoot(children: UiNode[]): UiNode {
  return { id: 'root', type: UiConfigPrimitives.WidgetConfiguration, children };
}

function typeInfo(overrides: Partial<WidgetTypeInfo> = {}): WidgetTypeInfo {
  return {
    id: WidgetType.Clock,
    providerId: '',
    isBuiltIn: true,
    defaultData: {},
    supportsConfigUi: true,
    configUiModelVersion: 4,
    ...overrides,
  };
}

describe('WidgetConfigurationEditorComponent', () => {
  let opens: UiSessionOpenRequest[];
  let handles: FakeUiSessionHandle[];
  let infoFor: jasmine.Spy;
  let api: ApiService;

  function configHandle(): FakeUiSessionHandle {
    const index = opens.findIndex(request => request.kind === 'config');
    return handles[index];
  }

  function configHandle2(): FakeUiSessionHandle {
    const indexes = opens.map((request, index) => request.kind === 'config' ? index : -1).filter(i => i >= 0);
    return handles[indexes[1]];
  }

  async function settle(fixture: ComponentFixture<WidgetConfigurationEditorComponent>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await fixture.whenStable();
  }

  async function createFixture(
    w: GridWidget = widget(),
    info: WidgetTypeInfo | null = typeInfo(),
  ): Promise<ComponentFixture<WidgetConfigurationEditorComponent>> {
    infoFor.and.resolveTo(info);
    const fixture = TestBed.createComponent(WidgetConfigurationEditorComponent);
    fixture.componentRef.setInput('widget', w);
    await settle(fixture);
    return fixture;
  }

  function nodeIdsIn(fixture: ComponentFixture<WidgetConfigurationEditorComponent>, selector: string): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll(`${selector} [data-node-id]`))
      .map(el => (el as HTMLElement).getAttribute('data-node-id'))
      .filter((id): id is string => id !== null);
  }

  function isRendered(fixture: ComponentFixture<WidgetConfigurationEditorComponent>, nodeId: string): boolean {
    const el = fixture.nativeElement.querySelector(`[data-node-id="${nodeId}"]`) as HTMLElement | null;
    return !!el && el.querySelector('*') !== null;
  }

  beforeEach(() => {
    opens = [];
    handles = [];
    infoFor = jasmine.createSpy('infoFor').and.resolveTo(null);

    const fakeUiSessions: Pick<UiSessionService, 'open'> = {
      open: (request: UiSessionOpenRequest): UiSessionHandle => {
        opens.push(request);
        const handle = new FakeUiSessionHandle();
        handles.push(handle);
        return handle;
      },
    };

    TestBed.configureTestingModule({
      imports: [WidgetConfigurationEditorComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: UiSessionService, useValue: fakeUiSessions },
        { provide: WidgetTypeCatalogService, useValue: { infoFor } },
      ],
    });

    api = TestBed.inject(ApiService);
  });

  it('opens the config session with the host-reported UI model major, never a literal', async () => {
    const fixture = await createFixture(widget(), typeInfo({ configUiModelVersion: 9 }));

    const request = opens.find(r => r.kind === 'config');
    expect(request).toEqual(jasmine.objectContaining({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.WidgetConfig,
      widgetId: 'w1',
      configUiModelVersion: 9,
    }));
    void fixture;
  });

  it('opens the config session with the draft being edited, not just the widget id', async () => {
    const fixture = await createFixture(widget({ data: { label: 'Draft' } as WidgetData }));

    const request = opens.find(r => r.kind === 'config');
    expect(request).toEqual(jasmine.objectContaining({ widgetData: JSON.stringify({ label: 'Draft' }) }));
    void fixture;
  });

  describe('reload', () => {
    // The host composes the tree from the data the session was opened with, so a draft that reached the
    // editor from outside the tree - a JSON-mode edit, above all - is invisible until it is reopened.
    it('reopens the session with the new draft when the page reseeds the editor', async () => {
      const fixture = await createFixture(widget({ data: { label: 'Before' } as WidgetData }));
      const first = configHandle();

      fixture.componentRef.setInput('widget', widget({ data: { label: 'After' } as WidgetData }));
      fixture.componentInstance.reload();
      await settle(fixture);

      const configOpens = opens.filter(r => r.kind === 'config');
      expect(configOpens.length).toBe(2);
      expect(configOpens[1]).toEqual(jasmine.objectContaining({
        widgetData: JSON.stringify({ label: 'After' }),
      }));
      expect(first.closed).toBeTrue();
    });

    it('leaves the session alone when the reseed changed nothing the tree is built from', async () => {
      // The page reseeds on every live update it adopts; reopening then would throw away tree-local
      // state - which tab is open, which state is selected - for a tree that would come back identical.
      const fixture = await createFixture(widget({ data: { label: 'Before' } as WidgetData }));
      const first = configHandle();

      fixture.componentInstance.reload();
      await settle(fixture);

      expect(opens.filter(r => r.kind === 'config').length).toBe(1);
      expect(first.closed).toBeFalse();
    });

    it('does not fold the reopened tree into the draft, the way a first tree never is', async () => {
      // A fresh tree carries the provider's defaults for keys the draft never had. Folding those in
      // would write keys nobody asked for and read as an edit - the same reason the very first tree of
      // a session is not folded either.
      const fixture = await createFixture(widget({ data: { label: 'Before' } as WidgetData }));

      const next = widget({ data: { label: 'After' } as WidgetData });
      fixture.componentRef.setInput('widget', next);
      fixture.componentInstance.reload();
      await settle(fixture);

      configHandle2().root.set(configRoot([
        propertiesRegion([stringField('label', 'After'), stringField('seededByProvider', 'default')]),
      ]));
      await settle(fixture);

      expect(next.data).toEqual({ label: 'After' } as WidgetData);
    });
  });

  it('is not ready until the host answers, so the page never reveals the preview-only fallback', async () => {
    // Without a tree there is no editor region, and the single-pane fallback would show the preview
    // alone - which is a legitimate final layout for a type with no editor region, but here only a
    // half-built one that jumps into the split layout when the tree lands seconds later.
    const fixture = await createFixture();

    expect(fixture.componentInstance.ready()).toBeFalse();

    configHandle().root.set(configRoot([
      propertiesRegion([stringField('label', 'Before')]),
      editorRegion([stringField('flows', '')]),
    ]));
    await settle(fixture);

    expect(fixture.componentInstance.ready()).toBeTrue();
  });

  it('is ready once the host rejects the session, so the page shows the rejection rather than a spinner', async () => {
    const fixture = await createFixture();
    configHandle().rejection.set({ code: 'unsupported' });
    await settle(fixture);

    expect(fixture.componentInstance.ready()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.widget-config-rejected')).toBeTruthy();
  });

  it('renders the properties region exclusively in the sidebar and the editor region exclusively in the main pane', async () => {
    const fixture = await createFixture();
    configHandle().root.set(configRoot([
      propertiesRegion([stringField('label', 'Before'), stringField('caption', '')]),
      editorRegion([stringField('flows', '')]),
    ]));
    await settle(fixture);

    const sidebarIds = nodeIdsIn(fixture, '.widget-config-properties');
    const mainIds = nodeIdsIn(fixture, '.widget-config-editor');

    expect(sidebarIds.length).toBe(3); // the region node itself plus its two children
    expect(sidebarIds).toEqual(jasmine.arrayContaining(['properties', 'label', 'caption']));
    expect(sidebarIds).not.toContain('flows');

    expect(mainIds.length).toBe(2); // the region node itself plus its one child
    expect(mainIds).toEqual(jasmine.arrayContaining(['editor', 'flows']));
    expect(mainIds).not.toContain('label');
    expect(mainIds).not.toContain('caption');
  });

  // The Action Button's own properties tree (issue #837), shaped exactly like
  // ActionButtonWidgetConfigView.Build's font row and align/position row: a non-wrapping stack with
  // rowWeight-carrying children, and a stretch segmented tab strip. Rendered inside the real
  // app-widget-editor-shell rather than a bare fixture, so the sidebar width this measures against
  // (300px, minus its own padding) is the one an editor actually gives it - not a number this test
  // made up.
  function actionButtonAppearance(): UiNode {
    const choice = (id: string, value: string, label: string, extra: Record<string, unknown> = {}): UiNode => ({
      id,
      type: UiConfigPrimitives.Choice,
      properties: {
        [UiConfigProperties.Events]: [UiConfigEvents.Change],
        [UiConfigProperties.Value]: value,
        [UiConfigProperties.Label]: label,
        ...extra,
      },
    });

    const iconOptions = (...pairs: [string, string, string][]) => pairs.map(([v, l, icon]) => ({ value: v, label: l, icon }));

    return propertiesRegion([
      {
        id: 'state-row',
        type: UiConfigPrimitives.Stack,
        properties: { [UiConfigProperties.Direction]: 'horizontal', [UiConfigProperties.Wrap]: false },
        children: [
          { ...choice('activeStateId', 'off', '', {
            [UiConfigProperties.HideLabel]: true,
            [UiConfigProperties.RowWeight]: 1,
            [UiConfigProperties.Options]: [{ value: 'off', label: 'Off' }, { value: 'on', label: 'On' }],
          }) },
          { id: 'addState', type: UiConfigPrimitives.Button, properties: { [UiConfigProperties.Label]: 'Add state', [UiConfigProperties.Icon]: 'plus' } },
          { id: 'manageState', type: UiConfigPrimitives.Button, properties: { [UiConfigProperties.Label]: 'Manage this state', [UiConfigProperties.Icon]: 'dots-vertical' } },
        ],
      },
      { id: 'live-state', type: UiConfigPrimitives.Prose, properties: { [UiConfigProperties.Text]: 'Currently On', [UiConfigProperties.Severity]: 'success' } },
      {
        id: 'appearance-tabs',
        type: UiConfigPrimitives.Tabs,
        children: [
          {
            id: 'label-tab',
            type: UiConfigPrimitives.Tab,
            properties: { [UiConfigProperties.Label]: 'Label' },
            children: [
              { ...choice('fontFamily', 'Inter', 'Font', {
                [UiConfigProperties.Transient]: true,
                [UiConfigProperties.Placeholder]: 'Default',
                [UiConfigProperties.Options]: [{ value: 'Inter', label: 'Inter' }, { value: 'Acme', label: 'Acme' }],
              }) },
              {
                id: 'label-appearance-row',
                type: UiConfigPrimitives.Stack,
                properties: { [UiConfigProperties.Direction]: 'horizontal', [UiConfigProperties.Wrap]: false },
                children: [
                  { ...choice('fontFaceId', 'inter-400', 'Style', {
                    [UiConfigProperties.Placeholder]: 'Default',
                    [UiConfigProperties.RowWeight]: 2,
                    [UiConfigProperties.Options]: [{ value: 'inter-400', label: 'Regular' }, { value: 'inter-700', label: 'Bold' }],
                  }) },
                  {
                    id: 'fontSize',
                    type: UiConfigPrimitives.Number,
                    properties: {
                      [UiConfigProperties.Events]: [UiConfigEvents.Change],
                      [UiConfigProperties.Value]: 14,
                      [UiConfigProperties.Label]: 'Size (%)',
                      [UiConfigProperties.RowWeight]: 1,
                    },
                  },
                ],
              },
              {
                id: 'label-alignment-row',
                type: UiConfigPrimitives.Stack,
                properties: { [UiConfigProperties.Direction]: 'horizontal', [UiConfigProperties.Wrap]: false },
                children: [
                  { ...choice('textAlign', 'center', 'Align', {
                    [UiConfigProperties.Segmented]: true,
                    [UiConfigProperties.RowWeight]: 1,
                    [UiConfigProperties.Options]: iconOptions(['left', 'Left', 'align-left'], ['center', 'Center', 'align-center'], ['right', 'Right', 'align-right']),
                  }) },
                  { ...choice('labelPosition', 'center', 'Position', {
                    [UiConfigProperties.Segmented]: true,
                    [UiConfigProperties.RowWeight]: 1,
                    [UiConfigProperties.Options]: iconOptions(['top', 'Top', 'align-top'], ['center', 'Center', 'align-middle'], ['bottom', 'Bottom', 'align-bottom']),
                  }) },
                ],
              },
              { id: 'labelColor', type: UiConfigPrimitives.Color, properties: { [UiConfigProperties.Events]: [UiConfigEvents.Change], [UiConfigProperties.Value]: '#ffffff', [UiConfigProperties.Label]: 'Label Color', [UiConfigProperties.SupportsReset]: true, [UiConfigProperties.DefaultValue]: '' } },
            ],
          },
          { id: 'background-tab', type: UiConfigPrimitives.Tab, properties: { [UiConfigProperties.Label]: 'Background' }, children: [] },
          { id: 'border-tab', type: UiConfigPrimitives.Tab, properties: { [UiConfigProperties.Label]: 'Border' }, children: [] },
        ],
      },
    ]);
  }

  it('fits the real 300px sidebar with no horizontal overflow anywhere (issue #837)', async () => {
    const fixture = await createFixture();
    configHandle().root.set(configRoot([actionButtonAppearance()]));
    await settle(fixture);

    const sidebar = fixture.nativeElement.querySelector('.widget-config-properties') as HTMLElement;
    expect(sidebar).not.toBeNull();

    const overflowing = Array.from(sidebar.querySelectorAll<HTMLElement>('*'))
      .filter(node => node.scrollWidth > node.clientWidth + 1) // +1: sub-pixel layout rounding
      .map(node => ({
        selector: node.className || node.tagName.toLowerCase(),
        scrollWidth: node.scrollWidth,
        clientWidth: node.clientWidth,
      }));

    expect(overflowing).toEqual([]);
  });

  it('renders a single pane, still editable and savable, when the configuration has no editor region', async () => {
    const fixture = await createFixture();
    configHandle().root.set(configRoot([propertiesRegion([stringField('label', 'Before')])]));
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('app-widget-editor-shell')).toBeFalsy();
    const pane = fixture.nativeElement.querySelector('.widget-config-single-pane');
    expect(pane).toBeTruthy();
    expect(nodeIdsIn(fixture, '.widget-config-single-pane')).toEqual(jasmine.arrayContaining(['properties', 'label']));

    const eventBus = fixture.debugElement.injector.get(UiNodeEventBus);
    eventBus.emit({ id: 'label', type: UiConfigPrimitives.String, properties: { events: [UiConfigEvents.Change] } },
      UiConfigEvents.Change, 'After');
    await settle(fixture);

    expect((fixture.componentInstance.widget.data as { label?: string }).label).toBe('After');
  });

  it('forwards a change event to the session and folds it into widget.data, with a nested edit landing as nested JSON', async () => {
    const w = widget({ data: { label: 'Before', border: { style: 'solid', color: '#fff' } } as unknown as WidgetData });
    const fixture = await createFixture(w);

    const borderStyleNode = stringField('border.style', 'solid');
    configHandle().root.set(configRoot([
      propertiesRegion([
        stringField('label', 'Before'),
        { id: 'border', type: UiConfigPrimitives.Object, children: [borderStyleNode] },
      ]),
    ]));
    await settle(fixture);

    const eventBus = fixture.debugElement.injector.get(UiNodeEventBus);
    eventBus.emit(borderStyleNode, UiConfigEvents.Change, 'comet');
    await settle(fixture);

    expect(configHandle().sent).toEqual([{ nodeId: 'border.style', name: UiConfigEvents.Change, data: 'comet' }]);

    const data = fixture.componentInstance.widget.data as unknown as Record<string, unknown>;
    expect((data['border'] as Record<string, unknown>)['style']).toBe('comet');
    expect('border.style' in data).toBeFalse();
    expect(allKeys(data).some(key => key.includes('.'))).toBeFalse();
  });

  it('resolves a visibleWhen in the editor region against a properties-region input, across both regions', async () => {
    const modeField = stringField('mode', 'basic');
    const advancedField: UiNode = {
      id: 'advancedFlag',
      type: UiConfigPrimitives.Boolean,
      properties: {
        [UiConfigProperties.Events]: [UiConfigEvents.Change],
        [UiConfigProperties.VisibleWhen]: { parameterName: 'mode', values: ['advanced'] },
      },
    };

    const fixture = await createFixture();
    configHandle().root.set(configRoot([propertiesRegion([modeField]), editorRegion([advancedField])]));
    await settle(fixture);

    expect(isRendered(fixture, 'advancedFlag')).toBeFalse();

    // `UiRenderContext.setValue` is what a real control calls on edit (`UiInputComponent`'s own
    // handler): it updates the value the renderer's `isVisible()` reads *and* emits through the
    // same bus this component listens on - a raw `UiNodeEventBus.emit` would only do the latter,
    // leaving the overlay (and so visibility) unchanged.
    const context = fixture.debugElement.injector.get(UiRenderContext);
    context.setValue(modeField, 'advanced');
    await settle(fixture);

    expect(isRendered(fixture, 'advancedFlag')).toBeTrue();
  });

  // A provider writes some keys itself rather than in response to a value edit - applying a preset,
  // adopting a state provider. Those reach the client as patches to the tree's own values and never as a
  // `change` event, so a draft folded only from events would keep the old values while the form visibly
  // shows the new ones, and the save would quietly write the stale set.
  it('picks up values the provider wrote itself, with no change event to announce them', async () => {
    const fixture = await createFixture(
      widget({ data: { valueVariable: '', title: '', historyLength: 120 } as never }));
    configHandle().root.set(configRoot([
      propertiesRegion([stringField('valueVariable', ''), stringField('title', '')]),
    ]));
    await settle(fixture);

    // What a preset button does: the provider writes several keys and the tree comes back patched.
    configHandle().root.set(configRoot([
      propertiesRegion([stringField('valueVariable', 'cpu.load'), stringField('title', 'CPU')]),
    ]));
    configHandle().revision.set(1);
    await settle(fixture);

    const data = fixture.componentInstance.widget.data as unknown as Record<string, unknown>;
    expect(data['valueVariable']).toBe('cpu.load');
    expect(data['title']).toBe('CPU');
    // A key the tree never mentions is still untouched by the reconciliation.
    expect(data['historyLength']).toBe(120);
  });

  // Giving a key up is as much a part of the draft as setting one. An Action Button entering state
  // mode is the case that proves it: its states own the background from then on, so the tree stops
  // configuring the root `backgroundColor` and the stored key has to go with it - left behind, the
  // widget goes on falling back to it and the colour comes back on the next save.
  it('removes a key the recomposed draft gives up, rather than leaving the stored one in place', async () => {
    const fixture = await createFixture(
      widget({ data: { backgroundColor: '#22c55e', label: 'Before' } as never }));
    configHandle().root.set(configRoot([
      propertiesRegion([colorField('backgroundColor', '#22c55e'), stringField('label', 'Before')]),
    ]));
    await settle(fixture);

    // What entering state mode does: the colour is given up, and nothing else about the tree changes.
    configHandle().root.set(configRoot([
      propertiesRegion([colorField('backgroundColor', ''), stringField('label', 'Before')]),
    ]));
    await settle(fixture);

    const data = fixture.componentInstance.widget.data as unknown as Record<string, unknown>;
    expect('backgroundColor' in data).toBeFalse();
    expect(data['label']).toBe('Before');
  });

  it('shows the rejection message instead of an empty pane when the session is declined', async () => {
    const fixture = await createFixture();
    configHandle().rejection.set({ code: 'not-configurable', message: 'nope' });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('.widget-config-single-pane')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('app-widget-editor-shell')).toBeFalsy();
    const rejected = fixture.nativeElement.querySelector('.widget-config-rejected');
    expect(rejected).toBeTruthy();
    expect(rejected.textContent).toContain('temporarily unavailable');
  });

  it('never writes to the host: driving several change events issues zero widget update calls', async () => {
    spyOn(api, 'updateWidget');
    spyOn(api, 'updateWidgetData');
    spyOn(api, 'updateWidgetState');

    const fixture = await createFixture();
    configHandle().root.set(configRoot([propertiesRegion([stringField('label', 'Before')])]));
    await settle(fixture);

    const eventBus = fixture.debugElement.injector.get(UiNodeEventBus);
    const labelNode = stringField('label', 'Before');
    eventBus.emit(labelNode, UiConfigEvents.Change, 'One');
    eventBus.emit(labelNode, UiConfigEvents.Change, 'Two');
    eventBus.emit(labelNode, UiConfigEvents.Change, 'Three');
    await settle(fixture);

    expect(api.updateWidget).not.toHaveBeenCalled();
    expect(api.updateWidgetData).not.toHaveBeenCalled();
    expect(api.updateWidgetState).not.toHaveBeenCalled();
  });

  // Issue #895: on the deck every widget's configured ring is drawn by the shared overlay outside the
  // widget tree, while an Action Button's is a property of its own `ui.button` node. The editor
  // preview only rendered the tree, so every non-button type lost its ring there.
  describe('border preview', () => {
    function ring(fixture: ComponentFixture<WidgetConfigurationEditorComponent>): HTMLElement | null {
      return fixture.nativeElement.querySelector('.preview-frame .ring') as HTMLElement | null;
    }

    it('draws the configured border around the preview of a non-button widget', async () => {
      const w = widget({ data: { border: { style: 'static', color: '#ff0000' } } as unknown as WidgetData });
      const fixture = await createFixture(w);
      configHandle().root.set(configRoot([propertiesRegion([stringField('label', 'Before')])]));
      await settle(fixture);

      const rendered = ring(fixture);
      expect(rendered).not.toBeNull();
      expect(rendered!.classList).toContain('wb-static');
      expect(rendered!.style.getPropertyValue('--wb-color')).toBe('#ff0000');
    });

    it('updates the preview border while the border configuration is edited', async () => {
      const w = widget({ data: { border: { style: 'static', color: '#ff0000' } } as unknown as WidgetData });
      const fixture = await createFixture(w);

      const borderStyleNode = stringField('border.style', 'static');
      configHandle().root.set(configRoot([
        propertiesRegion([{ id: 'border', type: UiConfigPrimitives.Object, children: [borderStyleNode] }]),
      ]));
      await settle(fixture);

      const eventBus = fixture.debugElement.injector.get(UiNodeEventBus);
      eventBus.emit(borderStyleNode, UiConfigEvents.Change, 'comet');
      await settle(fixture);

      expect(ring(fixture)!.classList).toContain('wb-comet');
    });

    it('draws no ring around a widget with no border configured', async () => {
      const fixture = await createFixture();
      configHandle().root.set(configRoot([propertiesRegion([stringField('label', 'Before')])]));
      await settle(fixture);

      expect(ring(fixture)).toBeNull();
    });

    // An Action Button's ring is the host's to resolve per active state and arrives on the preview
    // tree's `ui.button` root, so the preview draws that rather than the stored border - the same
    // rule the deck tile applies (issue #895).
    it('draws no Action Button ring from the stored border, which its states override', async () => {
      const w = widget({
        type: WidgetType.ActionButton,
        data: { border: { style: 'static', color: '#ff0000' } } as unknown as WidgetData,
      });
      const fixture = await createFixture(w, typeInfo({ id: WidgetType.ActionButton }));
      configHandle().root.set(configRoot([propertiesRegion([stringField('label', 'Before')])]));
      await settle(fixture);

      expect(ring(fixture)).toBeNull();
    });
  });
});

function allKeys(value: unknown): string[] {
  if (Array.isArray(value)) return value.flatMap(allKeys);
  if (value === null || typeof value !== 'object') return [];

  const record = value as Record<string, unknown>;
  return Object.keys(record).flatMap(key => [key, ...allKeys(record[key])]);
}
