import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type { Variable, VariablesChangedEvent } from '@macro-deck/runtime';
import { VariableCatalogService } from './variable-catalog.service';

function boundVariable(id: string, value: string): Variable {
  return {
    id,
    name: `ha_${id}`,
    scope: 'global',
    type: 'text',
    classification: 'integration',
    ownerIntegrationId: 'ha',
    dynamicResourceId: `entity/sensor.${id}/state`,
    value,
  };
}

async function flush(): Promise<void> {
  await new Promise(resolve => setTimeout(resolve, 0));
}

describe('VariableCatalogService', () => {
  let service: VariableCatalogService;
  let api: jasmine.SpyObj<ApiService>;
  let events: Subject<VariablesChangedEvent>;

  beforeEach(async () => {
    events = new Subject<VariablesChangedEvent>();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariableCatalogProviders',
      'discoverCatalogVariables',
      'onNotification',
    ]);
    api.getVariableCatalogProviders.and.resolveTo({ providers: [] });
    api.discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: true });
    api.onNotification.and.callFake((() => events) as never);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });
    service = TestBed.inject(VariableCatalogService);

    service.pageFor('ha', undefined, undefined);
    await flush();
  });

  function rootLoaded(): boolean {
    return service.isLoaded('ha', undefined, undefined);
  }

  it('keeps an integration\'s loaded catalog when a bound variable only changes its value', async () => {
    events.next({ upserted: [boundVariable('power', '10')], deletedIds: [] });
    service.pageFor('ha', undefined, undefined);
    await flush();
    api.discoverCatalogVariables.calls.reset();

    events.next({ upserted: [boundVariable('power', '11')], deletedIds: [] });
    events.next({ upserted: [boundVariable('power', '12')], deletedIds: [] });

    expect(rootLoaded()).toBeTrue();
    expect(api.discoverCatalogVariables).not.toHaveBeenCalled();
  });

  it('reloads an integration\'s catalog when a variable of it is newly bound', () => {
    expect(rootLoaded()).toBeTrue();

    events.next({ upserted: [boundVariable('power', '10')], deletedIds: [] });

    expect(rootLoaded()).toBeFalse();
  });

  it('reloads an integration\'s catalog when one of its bound variables goes away', async () => {
    events.next({ upserted: [boundVariable('power', '10')], deletedIds: [] });
    service.pageFor('ha', undefined, undefined);
    await flush();
    expect(rootLoaded()).toBeTrue();

    events.next({ upserted: [], deletedIds: ['power'] });

    expect(rootLoaded()).toBeFalse();
  });
});
