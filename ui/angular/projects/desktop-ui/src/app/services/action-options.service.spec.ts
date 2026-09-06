import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { GetActionParameterOptionsRequest, GetActionParameterOptionsResponse } from '@macro-deck/runtime';
import { ActionOptionsService } from './action-options.service';

describe('ActionOptionsService', () => {
  let service: ActionOptionsService;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getActionParameterOptions', 'onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    service = TestBed.inject(ActionOptionsService);
  });

  it('does not share a cache entry between a configuration and a payload parameter of the same name on the same event', async () => {
    const configResponse: GetActionParameterOptionsResponse = {
      options: [{ value: 'config-1', label: 'Config Option' }],
      allowsCustomValue: false,
      cacheSeconds: 60,
    };
    const payloadResponse: GetActionParameterOptionsResponse = {
      options: [{ value: 'payload-1', label: 'Payload Option' }],
      allowsCustomValue: false,
      cacheSeconds: 60,
    };
    apiSpy.getActionParameterOptions.and.callFake((request: GetActionParameterOptionsRequest) =>
      Promise.resolve(request.eventParameterKind === 'payload' ? payloadResponse : configResponse));

    const baseRequest: GetActionParameterOptionsRequest = {
      integrationId: '',
      actionId: '',
      eventId: 'macro-deck::variable-changed',
      parameterName: 'value',
      currentParameters: {},
    };

    const configResult = await service.getOptions({ ...baseRequest, eventParameterKind: 'configuration' });
    const payloadResult = await service.getOptions({ ...baseRequest, eventParameterKind: 'payload' });

    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(2);
    expect(configResult.options[0].value).toBe('config-1');
    expect(payloadResult.options[0].value).toBe('payload-1');

    // Re-requesting either kind within cacheSeconds must hit its own cache entry, not the other's.
    const configAgain = await service.getOptions({ ...baseRequest, eventParameterKind: 'configuration' });
    const payloadAgain = await service.getOptions({ ...baseRequest, eventParameterKind: 'payload' });

    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(2);
    expect(configAgain.options[0].value).toBe('config-1');
    expect(payloadAgain.options[0].value).toBe('payload-1');
  });

  it('invalidates a single parameter of an action without clearing its siblings', async () => {
    apiSpy.getActionParameterOptions.and.callFake((request: GetActionParameterOptionsRequest) =>
      Promise.resolve({
        options: [{ value: `${request.parameterName}-1`, label: 'Option' }],
        allowsCustomValue: false,
        cacheSeconds: 60,
      }));

    const request = (parameterName: string): GetActionParameterOptionsRequest =>
      ({ integrationId: 'app.test', actionId: 'play', parameterName });

    // `deviceName` shares a prefix with `device`, so a prefix that stops short of the separator
    // would clear both.
    await service.getOptions(request('device'));
    await service.getOptions(request('deviceName'));
    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(2);

    service.invalidate('app.test', 'play', 'device');

    await service.getOptions(request('device'));
    await service.getOptions(request('deviceName'));

    // Only the invalidated parameter is refetched; the sibling still answers from cache.
    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(3);
  });

  describe('loadLabeledOptions', () => {
    it('names an option by its value when the host sent no label', async () => {
      apiSpy.getActionParameterOptions.and.resolveTo({ options: [{ value: 'guid-1', label: '' }], allowsCustomValue: false });

      const result = await service.loadLabeledOptions({ integrationId: 'i', actionId: 'a', parameterName: 'p' });

      expect(result.options).toEqual([{ value: 'guid-1', label: 'guid-1', metadata: undefined }]);
      expect(result.error).toBeUndefined();
    });

    it('reports a host error as a message with no options, rather than an empty success', async () => {
      apiSpy.getActionParameterOptions.and.resolveTo({
        options: [],
        allowsCustomValue: false,
        error: { code: 'NO_OPTIONS_PROVIDER', message: 'Nothing can answer for this parameter.' },
      });

      const result = await service.loadLabeledOptions({ integrationId: 'i', actionId: 'a', parameterName: 'p' });

      expect(result.options).toEqual([]);
      expect(result.error).toBe('Nothing can answer for this parameter.');
    });

    it('reports a failed request through the same error, so a caller branches once', async () => {
      apiSpy.getActionParameterOptions.and.rejectWith(new Error('socket closed'));

      const result = await service.loadLabeledOptions({ integrationId: 'i', actionId: 'a', parameterName: 'p' });

      expect(result.options).toEqual([]);
      expect(result.error).toBeTruthy();
    });
  });

  describe('a forced refresh', () => {
    function request(): GetActionParameterOptionsRequest {
      return { integrationId: 'app.macro-deck.scripts', actionId: 'run-script', parameterName: 'scriptId' };
    }

    function response(label: string, cacheSeconds = 300): GetActionParameterOptionsResponse {
      return { options: [{ value: 'a', label }], allowsCustomValue: false, cacheSeconds };
    }

    function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
      let resolve!: (value: T) => void;
      const promise = new Promise<T>(r => (resolve = r));
      return { promise, resolve };
    }

    it('issued while a normal call is pending, starts its own request and resolves with its own response', async () => {
      const pending = deferred<GetActionParameterOptionsResponse>();
      apiSpy.getActionParameterOptions.and.returnValues(pending.promise, Promise.resolve(response('Forced')));

      const normal = service.getOptions(request());
      const forced = service.getOptions(request(), { forceRefresh: true });

      const forcedResult = await forced;
      expect(forcedResult.options[0].label).toBe('Forced');
      expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(2);

      pending.resolve(response('Stale'));
      const normalResult = await normal;
      expect(normalResult.options[0].label).toBe('Stale');

      // The overtaken call landing last must not put its pre-reload answer back in the cache.
      const afterward = await service.getOptions(request());
      expect(afterward.options[0].label).toBe('Forced');
    });

    it('leaves a later normal call the reloaded data, not the pre-reload cached data', async () => {
      apiSpy.getActionParameterOptions.and.resolveTo(response('Original'));
      await service.getOptions(request());

      apiSpy.getActionParameterOptions.and.resolveTo(response('Reloaded'));
      await service.getOptions(request(), { forceRefresh: true });

      const result = await service.getOptions(request());
      expect(result.options[0].label).toBe('Reloaded');
      expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(2);
    });

    it('does not stop a normal call de-duplicating a concurrent identical call into one request', async () => {
      const pending = deferred<GetActionParameterOptionsResponse>();
      apiSpy.getActionParameterOptions.and.returnValue(pending.promise);

      const first = service.getOptions(request());
      const second = service.getOptions(request());

      pending.resolve(response('Shared'));
      const [firstResult, secondResult] = await Promise.all([first, second]);

      expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(1);
      expect(firstResult).toBe(secondResult);
    });
  });
});
