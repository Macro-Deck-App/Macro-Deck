import { Component, EventEmitter, Input, WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { GridWidget, WidgetData, WidgetType } from '@macro-deck/runtime';

import { IWidgetComponent, IWidgetEditorComponent } from '../widget-definition.interface';
import { provideLocalizationTesting } from '../localization/localization-test-support';
import { WidgetRegistryService } from './widget-registry.service';
import { WidgetTypeCatalogService } from './widget-type-catalog.service';
import { WidgetTypeInfo } from '../transport';
import { WidgetConfigurationEditorComponent } from '../../components/widgets/widget-editors/widget-configuration-editor/widget-configuration-editor.component';
import { UiTreeWidgetComponent } from '../components/widget-types/ui-tree-widget/ui-tree-widget.component';

@Component({ selector: 'shared-test-widget', standalone: true, template: '' })
class TestWidgetComponent implements IWidgetComponent {
  @Input() data: WidgetData = {};
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
}

@Component({ selector: 'shared-test-widget-editor', standalone: true, template: '' })
class TestWidgetEditorComponent implements IWidgetEditorComponent {
  widget!: GridWidget;
  save = new EventEmitter<Partial<WidgetData>>();
  close = new EventEmitter<void>();
}

function catalogEntry(overrides: Partial<WidgetTypeInfo> & { id: string }): WidgetTypeInfo {
  return {
    providerId: '',
    isBuiltIn: true,
    defaultData: {},
    supportsConfigUi: false,
    configUiModelVersion: 0,
    ...overrides,
  };
}

describe('WidgetRegistryService', () => {
  let service: WidgetRegistryService;
  let widgetTypesStub: { infoFor: jasmine.Spy; types: WritableSignal<WidgetTypeInfo[]> };

  beforeEach(() => {
    widgetTypesStub = {
      infoFor: jasmine.createSpy('infoFor').and.resolveTo(null),
      types: signal<WidgetTypeInfo[]>([]),
    };
    TestBed.configureTestingModule({
      providers: [
        provideLocalizationTesting(),
        { provide: WidgetTypeCatalogService, useValue: widgetTypesStub },
      ],
    });
    service = TestBed.inject(WidgetRegistryService);
  });

  it('knows nothing of its own about an unregistered widget type', () => {
    expect(service.get(WidgetType.Clock)).toBeUndefined();
    expect(service.getDefaultData(WidgetType.Clock)).toBeUndefined();
  });

  it('resolves the generic config-tree editor for a type with no registration and no host support', async () => {
    widgetTypesStub.infoFor.and.resolveTo(null);

    await expectAsync(service.getEditorComponent(WidgetType.Clock))
      .toBeResolvedTo(WidgetConfigurationEditorComponent);
  });

  it('resolves the generic config-tree editor over a registered Angular editor once the host reports config UI support', async () => {
    const loadEditorComponent = jasmine.createSpy('loadEditorComponent');
    widgetTypesStub.infoFor.and.resolveTo(
      catalogEntry({ id: WidgetType.Clock, supportsConfigUi: true, configUiModelVersion: 4 }));
    service.register({
      type: WidgetType.Clock,
      component: TestWidgetComponent,
      loadEditorComponent,
    });

    const editorType = await service.getEditorComponent(WidgetType.Clock);

    expect(editorType).toBe(WidgetConfigurationEditorComponent);
    expect(loadEditorComponent).not.toHaveBeenCalled();
  });

  it('still draws an unregistered widget type through the generic tree renderer', () => {
    // A type registered host-side that this client carries no registration for - a plugin's, one day -
    // is a host-built tree like every other widget, so it draws rather than leaving a hole in the deck.
    expect(service.getComponent('com.example.gauge')).toBe(UiTreeWidgetComponent);
  });

  it('stores and returns the display component synchronously', () => {
    widgetTypesStub.types.set([catalogEntry({ id: WidgetType.Clock, name: 'Clock' })]);
    service.register({
      type: WidgetType.Clock,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetEditorComponent),
    });

    expect(service.getComponent(WidgetType.Clock)).toBe(TestWidgetComponent);
    expect(service.getWidgetTypeName(WidgetType.Clock)).toBe('Clock');
  });

  it('resolves the editor component lazily via the registered loader', async () => {
    const loadEditorComponent = jasmine.createSpy('loadEditorComponent')
      .and.returnValue(Promise.resolve(TestWidgetEditorComponent));

    service.register({
      type: WidgetType.Clock,
      component: TestWidgetComponent,
      loadEditorComponent,
    });

    expect(loadEditorComponent).not.toHaveBeenCalled();

    const editorType = await service.getEditorComponent(WidgetType.Clock);

    expect(loadEditorComponent).toHaveBeenCalledTimes(1);
    expect(editorType).toBe(TestWidgetEditorComponent);
  });

  it('returns the host catalogue\'s default data, deep-cloned on every call', () => {
    widgetTypesStub.types.set([catalogEntry({ id: WidgetType.Clock, defaultData: { showSeconds: true } })]);

    const first = service.getDefaultData(WidgetType.Clock) as Record<string, unknown>;
    first['showSeconds'] = false;

    expect(service.getDefaultData(WidgetType.Clock)).toEqual({ showSeconds: true } as unknown as WidgetData);
  });

  it('falls back to the raw type when the host catalogue carries no name for it', () => {
    widgetTypesStub.types.set([catalogEntry({ id: WidgetType.Clock })]);

    expect(service.getWidgetTypeName(WidgetType.Clock)).toBe(WidgetType.Clock);
  });

  it('getAll returns every registered definition', () => {
    service.register({
      type: WidgetType.Clock,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetEditorComponent),
    });
    service.register({
      type: WidgetType.Slider,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetEditorComponent),
    });

    expect(service.getAll().map(d => d.type)).toEqual([WidgetType.Clock, WidgetType.Slider]);
  });
});
