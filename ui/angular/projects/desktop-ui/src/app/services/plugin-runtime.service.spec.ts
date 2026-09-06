import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { PluginRuntimeInfo } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { PluginRuntimeService } from './plugin-runtime.service';

function plugin(pluginId: string, overrides: Partial<PluginRuntimeInfo> = {}): PluginRuntimeInfo {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    version: '1.0.0',
    state: 'running',
    health: 'healthy',
    managed: true,
    lastStopReason: 'none',
    consecutiveHealthFailures: 0,
    restartCount: 0,
    bootstrapOutput: [],
    ...overrides,
  };
}

describe('PluginRuntimeService', () => {
  let service: PluginRuntimeService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getPluginRuntime', 'startPlugin', 'stopPlugin', 'restartPlugin',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getPluginRuntime.and.resolveTo({ plugins: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(PluginRuntimeService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot when the transport connects', async () => {
      api.getPluginRuntime.and.resolveTo({ plugins: [plugin('p1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getPluginRuntime).toHaveBeenCalled();
      expect(service.plugins().map(p => p.pluginId)).toEqual(['p1']);
    });

    it('pulls again on a reconnect, so state missed while offline is repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.load();
      const afterFirst = api.getPluginRuntime.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.load();

      expect(api.getPluginRuntime.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one request', async () => {
      const before = api.getPluginRuntime.calls.count();

      await Promise.all([service.load(), service.load(), service.load()]);

      expect(api.getPluginRuntime.calls.count()).toBe(before + 1);
    });

    it('sets loadError and leaves the cache untouched on failure', async () => {
      api.getPluginRuntime.and.rejectWith(new Error('boom'));

      await service.load();

      expect(service.loadError()).toBe('boom');
      expect(service.plugins()).toEqual([]);
    });
  });

  describe('push subscription', () => {
    it('reloads on PluginRuntimeChangedEvent', () => {
      api.getPluginRuntime.and.resolveTo({ plugins: [plugin('p1')] });

      push('PluginRuntimeChangedEvent', {});

      expect(api.getPluginRuntime).toHaveBeenCalled();
    });
  });

  describe('mutations', () => {
    it('marks a plugin busy while start is in flight and clears it after', async () => {
      let resolveStart!: (value: { success: boolean }) => void;
      api.startPlugin.and.returnValue(new Promise(resolve => { resolveStart = resolve; }));

      const pending = service.start('p1');
      expect(service.busy().has('p1')).toBeTrue();

      resolveStart({ success: true });
      await pending;

      expect(service.busy().has('p1')).toBeFalse();
    });

    it('does not optimistically rewrite plugin state - it reloads instead', async () => {
      api.getPluginRuntime.and.resolveTo({ plugins: [plugin('p1', { state: 'stopped' })] });
      api.startPlugin.and.resolveTo({ success: true });

      await service.start('p1');

      expect(api.getPluginRuntime).toHaveBeenCalled();
    });

    it('stop calls the API and reloads', async () => {
      api.stopPlugin.and.resolveTo({ success: true });

      await service.stop('p1');

      expect(api.stopPlugin).toHaveBeenCalledWith('p1');
      expect(api.getPluginRuntime).toHaveBeenCalled();
    });

    it('restart calls the API and reloads', async () => {
      api.restartPlugin.and.resolveTo({ success: true });

      await service.restart('p1');

      expect(api.restartPlugin).toHaveBeenCalledWith('p1');
      expect(api.getPluginRuntime).toHaveBeenCalled();
    });

    it('clears busy even when the call fails', async () => {
      api.startPlugin.and.rejectWith(new Error('boom'));

      await expectAsync(service.start('p1')).toBeRejected();

      expect(service.busy().has('p1')).toBeFalse();
    });
  });
});
