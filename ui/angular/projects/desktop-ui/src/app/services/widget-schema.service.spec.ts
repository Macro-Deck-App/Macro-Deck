import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { WidgetType } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { WidgetSchemaService } from './widget-schema.service';

describe('WidgetSchemaService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let service: WidgetSchemaService;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getWidgetDataSchemas']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(WidgetSchemaService);
  });

  it('resolves a schema for every WidgetType, keyed by the wire PascalCase name', async () => {
    const wireNameByType: Record<WidgetType, string> = {
      [WidgetType.ActionButton]: 'ActionButton',
      [WidgetType.MusicPlayer]: 'MusicPlayer',
      [WidgetType.Slider]: 'Slider',
      [WidgetType.Weather]: 'Weather',
      [WidgetType.HistoryGraph]: 'HistoryGraph',
      [WidgetType.Clock]: 'Clock',
    };

    const schemas: Record<string, unknown> = {};
    for (const [domainType, wireName] of Object.entries(wireNameByType)) {
      schemas[wireName] = { marker: domainType };
    }
    apiSpy.getWidgetDataSchemas.and.resolveTo({ success: true, schemas });

    for (const [domainType, wireName] of Object.entries(wireNameByType) as [WidgetType, string][]) {
      const schema = await service.schemaFor(domainType);
      expect(schema).withContext(`${domainType} -> ${wireName}`).toEqual({ marker: domainType });
    }

    expect(apiSpy.getWidgetDataSchemas).toHaveBeenCalledTimes(1);
  });

  it("resolves a plugin type's schema keyed by its own id - a plugin type's id is its wire name", async () => {
    const pluginType = 'com.example.gauges::gauge';
    apiSpy.getWidgetDataSchemas.and.resolveTo({ success: true, schemas: { [pluginType]: { marker: 'gauge' } } });

    const schema = await service.schemaFor(pluginType);

    expect(schema).toEqual({ marker: 'gauge' });
  });

  it('leaves the editor usable when the fetch fails, and does not cache that failure permanently', async () => {
    apiSpy.getWidgetDataSchemas.and.rejectWith(new Error('network down'));

    await expectAsync(service.schemaFor(WidgetType.Clock)).toBeResolvedTo(null);
    expect(apiSpy.getWidgetDataSchemas).toHaveBeenCalledTimes(1);

    apiSpy.getWidgetDataSchemas.and.resolveTo({ success: true, schemas: { Clock: { marker: 'recovered' } } });
    const schema = await service.schemaFor(WidgetType.Clock);

    expect(schema).toEqual({ marker: 'recovered' });
    expect(apiSpy.getWidgetDataSchemas).toHaveBeenCalledTimes(2);
  });
});
