import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { PluginCompatibilityReport } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { PluginCompatibilityService } from './plugin-compatibility.service';

function report(pluginId: string, overrides: Partial<PluginCompatibilityReport> = {}): PluginCompatibilityReport {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    state: 'compatible',
    usageSource: 'unknown',
    usageTruncated: false,
    findings: [],
    ...overrides,
  };
}

describe('PluginCompatibilityService', () => {
  let service: PluginCompatibilityService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getPluginCompatibility']);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getPluginCompatibility.and.resolveTo({ plugins: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(PluginCompatibilityService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot when the transport connects', async () => {
      api.getPluginCompatibility.and.resolveTo({ plugins: [report('p1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getPluginCompatibility).toHaveBeenCalled();
      expect(service.reports().map(r => r.pluginId)).toEqual(['p1']);
    });

    it('pulls again on a reconnect, so state missed while offline is repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.load();
      const afterFirst = api.getPluginCompatibility.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.load();

      expect(api.getPluginCompatibility.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one request', async () => {
      const before = api.getPluginCompatibility.calls.count();

      await Promise.all([service.load(), service.load(), service.load()]);

      expect(api.getPluginCompatibility.calls.count()).toBe(before + 1);
    });

    it('sets loadError and leaves the cache untouched on failure', async () => {
      api.getPluginCompatibility.and.rejectWith(new Error('boom'));

      await service.load();

      expect(service.loadError()).toBe('boom');
      expect(service.reports()).toEqual([]);
    });
  });

  describe('push subscription', () => {
    it('reloads on PluginCompatibilityChangedEvent', () => {
      api.getPluginCompatibility.and.resolveTo({ plugins: [report('p1')] });

      push('PluginCompatibilityChangedEvent', {});

      expect(api.getPluginCompatibility).toHaveBeenCalled();
    });
  });

  describe('forPlugin', () => {
    it('finds the report matching a plugin id', async () => {
      api.getPluginCompatibility.and.resolveTo({ plugins: [report('p1'), report('p2', { state: 'incompatible' })] });
      await service.load();

      expect(service.forPlugin('p2')?.state).toBe('incompatible');
    });

    it('returns null when the host has no report for the plugin', async () => {
      api.getPluginCompatibility.and.resolveTo({ plugins: [report('p1')] });
      await service.load();

      expect(service.forPlugin('unknown-plugin')).toBeNull();
    });
  });
});
