import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { type ActionExecutionStatusEvent } from '@macro-deck/runtime';

import { ApiService } from '../transport';
import { LocalizationService } from '../localization';
import { ActionExecutionService } from './action-execution.service';
import { ToastService } from './toast.service';

describe('ActionExecutionService', () => {
  let service: ActionExecutionService;
  let events: Subject<ActionExecutionStatusEvent>;
  let toastSpy: jasmine.SpyObj<ToastService>;

  function statusEvent(executionId: string, overrides: Partial<ActionExecutionStatusEvent> = {}): ActionExecutionStatusEvent {
    return {
      executionId,
      status: 'Succeeded',
      durationMs: 12,
      actions: [],
      ...overrides,
    };
  }

  beforeEach(() => {
    jasmine.clock().install();
    events = new Subject<ActionExecutionStatusEvent>();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      expect(method).toBe('ActionExecutionStatusEvent');
      return events.asObservable() as unknown as Observable<T>;
    });
    toastSpy = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    const localizationSpy = jasmine.createSpyObj<LocalizationService>('LocalizationService', ['translateKey']);
    localizationSpy.translateKey.and.returnValue('Run failed');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ToastService, useValue: toastSpy },
        { provide: LocalizationService, useValue: localizationSpy },
      ],
    });
    service = TestBed.inject(ActionExecutionService);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  it('resolves a waiter registered before the event arrives', async () => {
    const pending = service.waitFor('exec-1');

    events.next(statusEvent('exec-1', { status: 'Failed', error: { code: 'X', message: 'boom' } }));

    const result = await pending;
    expect(result?.status).toBe('Failed');
    expect(result?.error?.message).toBe('boom');
    expect(result?.success).toBeFalse();
  });

  it('resolves immediately when the event already landed before waitFor was called', async () => {
    events.next(statusEvent('exec-2', { status: 'Succeeded' }));

    const result = await service.waitFor('exec-2');

    expect(result?.status).toBe('Succeeded');
    expect(result?.success).toBeTrue();
  });

  it('exposes the latest recorded result as a signal', () => {
    expect(service.latest()).toBeNull();

    events.next(statusEvent('exec-3'));

    expect(service.latest()?.executionId).toBe('exec-3');
  });

  it('bounds the tracked map and evicts the oldest entry', async () => {
    for (let i = 0; i < 51; i++) {
      events.next(statusEvent(`exec-${i}`));
    }

    const evicted = service.waitFor('exec-0', 5);
    jasmine.clock().tick(5);
    expect(await evicted).toBeNull();

    const kept = await service.waitFor('exec-50', 5);
    expect(kept?.executionId).toBe('exec-50');
  });

  it('waitFor times out cleanly and returns null when no event arrives', async () => {
    const pending = service.waitFor('never-arrives', 1000);
    jasmine.clock().tick(1000);

    expect(await pending).toBeNull();
  });

  it('toasts a generic failure for an execution nobody claimed, so a session-driven press with no ' +
    'response to await still tells the user', () => {
    events.next(statusEvent('exec-unclaimed', { status: 'Failed', error: { code: 'X', message: 'boom' } }));

    expect(toastSpy.show).toHaveBeenCalledWith('boom', { variant: 'error' });
  });

  it('stays quiet for an execution someone already claimed, so FolderService is never double-toasted', () => {
    service.claim('exec-claimed');

    events.next(statusEvent('exec-claimed', { status: 'Failed', error: { code: 'X', message: 'boom' } }));

    expect(toastSpy.show).not.toHaveBeenCalled();
  });

  it('never toasts for a successful execution, claimed or not', () => {
    events.next(statusEvent('exec-ok', { status: 'Succeeded' }));

    expect(toastSpy.show).not.toHaveBeenCalled();
  });
});
