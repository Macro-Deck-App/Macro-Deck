import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { AutomationCreatedEvent, AutomationDeletedEvent, AutomationUpdatedEvent, IpcAutomation } from '@macro-deck/runtime';
import { AutomationService } from './automation.service';

describe('AutomationService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let created: Subject<AutomationCreatedEvent>;
  let updated: Subject<AutomationUpdatedEvent>;
  let deleted: Subject<AutomationDeletedEvent>;
  let service: AutomationService;

  const FLOWS =
    '[{"triggerId":"t1","triggerType":"onEvent","event":{"providerId":"obs","eventId":"scene-changed"},'
    + '"children":[{"id":"b1"}]}]';

  function ipcAutomation(overrides: Partial<IpcAutomation> = {}): IpcAutomation {
    return {
      id: 'automation-1',
      name: 'Dim on stream start',
      description: 'Turns the lights down',
      enabled: true,
      flows: FLOWS,
      createdAt: '2026-07-29T00:00:00Z',
      updatedAt: '2026-07-29T00:00:00Z',
      ...overrides,
    };
  }

  beforeEach(() => {
    created = new Subject<AutomationCreatedEvent>();
    updated = new Subject<AutomationUpdatedEvent>();
    deleted = new Subject<AutomationDeletedEvent>();

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'getAutomations',
      'createAutomation',
      'updateAutomation',
      'duplicateAutomation',
      'deleteAutomation',
    ]);
    apiSpy.onNotification.and.callFake((name: string) => {
      switch (name) {
        case 'AutomationCreatedEvent':
          return created.asObservable() as Observable<never>;
        case 'AutomationUpdatedEvent':
          return updated.asObservable() as Observable<never>;
        case 'AutomationDeletedEvent':
          return deleted.asObservable() as Observable<never>;
        default:
          return new Subject<never>().asObservable();
      }
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        AutomationService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    service = TestBed.inject(AutomationService);
  });

  it('parses the flow array out of the wire string', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation()] });

    await service.loadAutomations();

    const [automation] = service.automations();
    expect(automation.flows.length).toBe(1);
    expect(automation.flows[0].triggerType).toBe('onEvent');
    expect(automation.flows[0].event?.eventId).toBe('scene-changed');
    expect(automation.enabled).toBeTrue();
  });

  it('survives unparseable flows', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation({ flows: '[ not json' })] });

    await service.loadAutomations();

    expect(service.automations()[0].flows).toEqual([]);
  });

  it('records a load failure instead of throwing', async () => {
    apiSpy.getAutomations.and.rejectWith(new Error('offline'));

    await service.loadAutomations();

    expect(service.loadError()).toBe('offline');
    expect(service.isLoading()).toBeFalse();
  });

  it('serializes the flow array on create', async () => {
    apiSpy.createAutomation.and.resolveTo({ success: true, automation: ipcAutomation() });

    await service.createAutomation('Name', 'Description', [
      { triggerId: 't1', triggerType: 'onEvent', children: [] },
    ]);

    const request = apiSpy.createAutomation.calls.mostRecent().args[0];
    expect(request.name).toBe('Name');
    expect(JSON.parse(request.flows ?? '[]')).toEqual([
      { triggerId: 't1', triggerType: 'onEvent', children: [] },
    ]);
  });

  it('sends only enabled when toggling', async () => {
    apiSpy.updateAutomation.and.resolveTo({ success: true, automation: ipcAutomation({ enabled: false }) });

    await service.setEnabled('automation-1', false);

    const request = apiSpy.updateAutomation.calls.mostRecent().args[0];
    expect(request).toEqual({
      id: 'automation-1',
      name: undefined,
      description: undefined,
      flows: undefined,
      enabled: false,
    });
  });

  it('leaves the flow out of a rename', async () => {
    apiSpy.updateAutomation.and.resolveTo({ success: true, automation: ipcAutomation() });

    await service.updateAutomation('automation-1', { name: 'Renamed' });

    expect(apiSpy.updateAutomation.calls.mostRecent().args[0].flows).toBeUndefined();
  });

  it('reports a rejected mutation rather than throwing', async () => {
    apiSpy.updateAutomation.and.resolveTo({ success: false, error: { code: 'x', message: 'nope' } });

    const result = await service.updateAutomation('automation-1', { name: 'Renamed' });

    expect(result.success).toBeFalse();
    expect(result.error?.message).toBe('nope');
  });

  it('upserts an automation created in another window', () => {
    created.next({ automation: ipcAutomation({ id: 'from-elsewhere' }) });

    expect(service.automations().map(a => a.id)).toEqual(['from-elsewhere']);
  });

  it('replaces rather than duplicates on an update pushed from the host', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation()] });
    await service.loadAutomations();

    updated.next({ automation: ipcAutomation({ enabled: false }) });

    expect(service.automations().length).toBe(1);
    expect(service.automations()[0].enabled).toBeFalse();
  });

  it('drops an automation deleted elsewhere', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation()] });
    await service.loadAutomations();

    deleted.next({ id: 'automation-1' });

    expect(service.automations()).toEqual([]);
  });

  it('sorts by name', async () => {
    apiSpy.getAutomations.and.resolveTo({
      automations: [
        ipcAutomation({ id: '1', name: 'zulu' }),
        ipcAutomation({ id: '2', name: 'Alpha' }),
      ],
    });

    await service.loadAutomations();

    expect(service.sortedAutomations().map(a => a.name)).toEqual(['Alpha', 'zulu']);
  });

  it('removes an automation from state after a successful delete', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation()] });
    await service.loadAutomations();
    apiSpy.deleteAutomation.and.resolveTo({ success: true });

    const result = await service.deleteAutomation('automation-1');

    expect(result.success).toBeTrue();
    expect(service.automations()).toEqual([]);
  });

  it('keeps an automation that the host refused to delete', async () => {
    apiSpy.getAutomations.and.resolveTo({ automations: [ipcAutomation()] });
    await service.loadAutomations();
    apiSpy.deleteAutomation.and.resolveTo({ success: false, error: { code: 'x', message: 'nope' } });

    await service.deleteAutomation('automation-1');

    expect(service.automations().length).toBe(1);
  });
});
