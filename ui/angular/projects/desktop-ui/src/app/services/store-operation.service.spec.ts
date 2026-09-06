import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { StoreOperationBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { StoreOperationService } from './store-operation.service';

function operation(overrides: Partial<StoreOperationBody> = {}): StoreOperationBody {
  return {
    id: 'op-1',
    kind: 'Install',
    extensionKind: 'Plugin',
    packageId: 'app.example.plugin',
    version: '1.0.0',
    displayName: 'Example',
    state: 'Downloading',
    bytesDownloaded: 10,
    totalBytes: 100,
    etaSeconds: 30,
    startedAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:01Z',
    canRetry: false,
    ...overrides,
  };
}

describe('StoreOperationService', () => {
  let service: StoreOperationService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService',
      ['onNotification', 'getStoreOperations', 'retryStoreOperation', 'uninstallStoreExtension']);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getStoreOperations.and.resolveTo({ operations: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(StoreOperationService);
    api = apiSpy;
  });

  it('loads the host snapshot on connect, surfacing an operation this client never started', async () => {
    api.getStoreOperations.and.resolveTo({ operations: [operation({ id: 'op-remote' })] });

    connectionState.set('connected');
    await service.load();
    TestBed.tick();

    const found = service.operationFor('Plugin', 'app.example.plugin')();
    expect(found?.id).toBe('op-remote');
  });

  it('adds a pushed event for an operation id this client has never seen, instead of dropping it', () => {
    push('StoreOperationChangedEvent', { operation: operation({ id: 'brand-new' }) });

    const found = service.operationFor('Plugin', 'app.example.plugin')();
    expect(found?.id).toBe('brand-new');
  });

  it('re-reads the snapshot on a later reconnect', async () => {
    connectionState.set('connected');
    TestBed.tick();
    await service.load();
    const afterFirst = api.getStoreOperations.calls.count();

    connectionState.set('disconnected');
    TestBed.tick();
    connectionState.set('connected');
    TestBed.tick();
    await service.load();

    expect(api.getStoreOperations.calls.count()).toBeGreaterThan(afterFirst);
  });

  it('uninstall calls the host with the exact kind and package id, and returns its response as-is', async () => {
    const response = { success: false, error: { code: 'DependencyInUse', message: 'x' } };
    api.uninstallStoreExtension.and.resolveTo(response);

    const result = await service.uninstall('Plugin', 'app.example.plugin');

    expect(api.uninstallStoreExtension).toHaveBeenCalledWith('Plugin', 'app.example.plugin');
    expect(result).toBe(response);
  });
});
