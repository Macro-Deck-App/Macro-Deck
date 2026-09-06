import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { GetWeatherInstancesResponse, WeatherInstanceDto, WeatherInstancesChangedNotification, WeatherStateChangedNotification } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { UNAVAILABLE_WEATHER, WeatherService } from './weather.service';

describe('WeatherService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState: BehaviorSubject<ConnectionState>;
  let stateEvents: Subject<WeatherStateChangedNotification>;
  let instanceEvents: Subject<WeatherInstancesChangedNotification>;
  let service: WeatherService;

  function instance(id: string): WeatherInstanceDto {
    return {
      instanceId: id,
      integrationId: 'app.weather',
      providerName: 'Open-Meteo',
      displayName: id,
      hasIcon: false,
    };
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 8; i++) {
      await Promise.resolve();
    }
  }

  beforeEach(() => {
    jasmine.clock().install();
    connectionState = new BehaviorSubject<ConnectionState>('connected');
    stateEvents = new Subject<WeatherStateChangedNotification>();
    instanceEvents = new Subject<WeatherInstancesChangedNotification>();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'getWeatherInstances', 'getWeatherState'],
      { connectionState$: connectionState.asObservable() },
    );
    apiSpy.onNotification.and.callFake(
      (name: string) =>
        (name === 'WeatherInstancesChangedNotification'
          ? instanceEvents.asObservable()
          : stateEvents.asObservable()) as Observable<never>,
    );
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    apiSpy.getWeatherState.and.resolveTo(null);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(WeatherService);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  it('loads instances when the connection is established', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });

    service.start();
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalled();
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);
  });

  it('keeps the previous instances when getWeatherInstances returns null', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    await service.loadInstances();
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);

    apiSpy.getWeatherInstances.and.resolveTo(null);
    await service.loadInstances();

    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);
  });

  it('clears the instances on an explicit empty response', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    await service.loadInstances();

    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    await service.loadInstances();

    expect(service.instances()).toEqual([]);
  });

  it('replaces the instance list from a pushed instances-changed notification', () => {
    service.start();

    instanceEvents.next({ instances: [instance('app.weather::berlin'), instance('app.weather::paris')] });

    expect(service.instances().map(i => i.instanceId))
      .toEqual(['app.weather::berlin', 'app.weather::paris']);

    instanceEvents.next({ instances: [] });

    expect(service.instances()).toEqual([]);
  });

  it('stores pushed weather states per instance', () => {
    service.start();

    stateEvents.next({
      state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin', isAvailable: true },
    });

    expect(service.stateFor('app.weather::berlin').isAvailable).toBeTrue();
    expect(service.stateFor('app.weather::paris')).toBe(UNAVAILABLE_WEATHER);
  });

  it('single-flights concurrent instance loads', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });

    await Promise.all([service.loadInstances(), service.loadInstances(), service.loadInstances()]);

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
  });

  it('reloads the instance list once when a state arrives for an unknown station', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    service.start();
    await flush();
    apiSpy.getWeatherInstances.calls.reset();
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });

    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin' } });
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);

    // A station that is genuinely gone must not reload the list on every push it still emits.
    apiSpy.getWeatherInstances.calls.reset();
    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::gone' } });
    await flush();
    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::gone' } });
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
  });

  it('re-arms the heal when the station comes back into the list', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    service.start();
    await flush();

    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin' } });
    await flush();
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);

    instanceEvents.next({ instances: [] });
    apiSpy.getWeatherInstances.calls.reset();
    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin' } });
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);
  });

  it('re-arms the heal on reconnect', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    service.start();
    await flush();

    // Spend the heal on a station the host does not list, so the reload cannot re-arm it.
    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::gone' } });
    await flush();

    connectionState.next('connecting');
    connectionState.next('connected');
    await flush();
    apiSpy.getWeatherInstances.calls.reset();

    stateEvents.next({ state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::gone' } });
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
  });

  it('does not let a stale empty pull overwrite a list pushed while it was in flight', async () => {
    let resolvePull: (value: GetWeatherInstancesResponse | null) => void = () => undefined;
    apiSpy.getWeatherInstances.and.returnValue(
      new Promise<GetWeatherInstancesResponse | null>(resolve => (resolvePull = resolve)),
    );
    service.start();

    const pull = service.loadInstances();
    instanceEvents.next({ instances: [instance('app.weather::berlin')] });
    resolvePull({ instances: [] });
    await pull;
    await flush();

    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);
  });

  it('retries a failed instances pull instead of waiting for the next reconnect', async () => {
    apiSpy.getWeatherInstances.and.resolveTo(null);
    service.start();
    await flush();

    expect(service.instances()).toEqual([]);

    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    apiSpy.getWeatherInstances.calls.reset();

    jasmine.clock().tick(1999);
    await flush();
    expect(apiSpy.getWeatherInstances).not.toHaveBeenCalled();

    jasmine.clock().tick(1);
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
    expect(service.instances().map(i => i.instanceId)).toEqual(['app.weather::berlin']);
  });

  it('grows the retry delay while pulls keep failing, and stops after the cap', async () => {
    apiSpy.getWeatherInstances.and.resolveTo(null);
    service.start();
    await flush();
    apiSpy.getWeatherInstances.calls.reset();

    for (const delay of [2000, 4000, 8000, 16000, 30000]) {
      jasmine.clock().tick(delay - 1);
      await flush();
      expect(apiSpy.getWeatherInstances).not.toHaveBeenCalled();
      jasmine.clock().tick(1);
      await flush();
      expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
      apiSpy.getWeatherInstances.calls.reset();
    }

    jasmine.clock().tick(600000);
    await flush();

    expect(apiSpy.getWeatherInstances).not.toHaveBeenCalled();
  });

  it('gives the retry ladder a fresh budget on reconnect', async () => {
    apiSpy.getWeatherInstances.and.resolveTo(null);
    service.start();
    await flush();

    for (const delay of [2000, 4000, 8000, 16000, 30000]) {
      jasmine.clock().tick(delay);
      await flush();
    }
    apiSpy.getWeatherInstances.calls.reset();

    connectionState.next('connecting');
    connectionState.next('connected');
    await flush();
    apiSpy.getWeatherInstances.calls.reset();

    jasmine.clock().tick(2000);
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
  });

  it('resets the retry ladder after a successful pull', async () => {
    apiSpy.getWeatherInstances.and.resolveTo(null);
    service.start();
    await flush();

    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    jasmine.clock().tick(2000);
    await flush();

    apiSpy.getWeatherInstances.and.resolveTo(null);
    await service.loadInstances();
    apiSpy.getWeatherInstances.calls.reset();

    jasmine.clock().tick(2000);
    await flush();

    expect(apiSpy.getWeatherInstances).toHaveBeenCalledTimes(1);
  });

  it('does not retry a successful empty response', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [] });
    service.start();
    await flush();
    apiSpy.getWeatherInstances.calls.reset();

    jasmine.clock().tick(60000);
    await flush();

    expect(apiSpy.getWeatherInstances).not.toHaveBeenCalled();
  });

  it('retries a failed state pull on the next ensureState', async () => {
    apiSpy.getWeatherState.and.resolveTo(null);
    await service.ensureState('app.weather::berlin');
    expect(apiSpy.getWeatherState).toHaveBeenCalledTimes(1);

    apiSpy.getWeatherState.and.resolveTo({
      state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin', isAvailable: true },
    });
    await service.ensureState('app.weather::berlin');

    expect(apiSpy.getWeatherState).toHaveBeenCalledTimes(2);
    expect(service.stateFor('app.weather::berlin').isAvailable).toBeTrue();
  });

  it('re-pulls every known state on reconnect', async () => {
    apiSpy.getWeatherInstances.and.resolveTo({ instances: [instance('app.weather::berlin')] });
    apiSpy.getWeatherState.and.resolveTo({
      state: { ...UNAVAILABLE_WEATHER, instanceId: 'app.weather::berlin' },
    });
    service.start();
    await flush();
    expect(apiSpy.getWeatherState).toHaveBeenCalledTimes(1);

    // The host only pushes on change, so a client that was away must re-read rather than trust its cache.
    connectionState.next('connecting');
    connectionState.next('connected');
    await flush();

    expect(apiSpy.getWeatherState).toHaveBeenCalledTimes(2);
  });
});
