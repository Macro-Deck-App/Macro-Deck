import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { IpcScript, ScriptCreatedEvent, ScriptDeletedEvent, ScriptUpdatedEvent } from '@macro-deck/runtime';
import { ScriptService } from './script.service';

describe('ScriptService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let created: Subject<ScriptCreatedEvent>;
  let updated: Subject<ScriptUpdatedEvent>;
  let deleted: Subject<ScriptDeletedEvent>;
  let service: ScriptService;

  const FLOW = '[{"triggerId":"onRun","triggerType":"onRun","children":[{"id":"b1"}]}]';

  function ipcScript(overrides: Partial<IpcScript> = {}): IpcScript {
    return {
      id: 'script-1',
      name: 'Start stream',
      description: 'Opens OBS',
      flows: FLOW,
      createdAt: '2026-07-29T00:00:00Z',
      updatedAt: '2026-07-29T00:00:00Z',
      ...overrides,
    };
  }

  beforeEach(() => {
    created = new Subject<ScriptCreatedEvent>();
    updated = new Subject<ScriptUpdatedEvent>();
    deleted = new Subject<ScriptDeletedEvent>();

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'getScripts',
      'getScriptUsages',
      'createScript',
      'updateScript',
      'duplicateScript',
      'deleteScript',
      'runScript',
    ]);
    apiSpy.onNotification.and.callFake((name: string) => {
      switch (name) {
        case 'ScriptCreatedEvent':
          return created.asObservable() as Observable<never>;
        case 'ScriptUpdatedEvent':
          return updated.asObservable() as Observable<never>;
        case 'ScriptDeletedEvent':
          return deleted.asObservable() as Observable<never>;
        default:
          return new Subject<never>().asObservable();
      }
    });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ScriptService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(ScriptService);
  });

  it('parses the flow string into an action tree on load', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript()] });

    await service.loadScripts();

    const script = service.scripts()[0];
    expect(script.flows.length).toBe(1);
    expect(script.flows[0].triggerType).toBe('onRun');
    expect(script.flows[0].children.length).toBe(1);
  });

  it('keeps an unparsable flow from breaking the list', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript({ flows: '{ not json' })] });

    await service.loadScripts();

    expect(service.scripts().length).toBe(1);
    expect(service.scripts()[0].flows).toEqual([]);
  });

  it('serializes the flow tree back to a string on update', async () => {
    apiSpy.updateScript.and.resolveTo({ success: true, script: ipcScript() });

    await service.updateScript('script-1', {
      flows: [{ triggerId: 'onRun', triggerType: 'onRun', children: [] }],
    });

    expect(apiSpy.updateScript).toHaveBeenCalledWith(jasmine.objectContaining({
      id: 'script-1',
      flows: '[{"triggerId":"onRun","triggerType":"onRun","children":[]}]',
    }));
  });

  it('omits the flow when only the name changes, so a rename cannot clobber it', async () => {
    apiSpy.updateScript.and.resolveTo({ success: true, script: ipcScript() });

    await service.updateScript('script-1', { name: 'Renamed' });

    expect(apiSpy.updateScript).toHaveBeenCalledWith(jasmine.objectContaining({
      id: 'script-1',
      name: 'Renamed',
      flows: undefined,
    }));
  });

  it('round-trips input declarations through the DTO mapper', async () => {
    apiSpy.getScripts.and.resolveTo({
      scripts: [ipcScript({
        inputs: [
          { name: 'scene', type: 'text', label: 'Scene', required: true, defaultValue: 'Starting Soon' },
          { name: 'volume', type: 'numeric', defaultValue: '42' },
          { name: 'muted', type: 'boolean', defaultValue: 'false' },
        ],
      })],
    });

    await service.loadScripts();

    expect(service.scripts()[0].inputs).toEqual([
      { name: 'scene', type: 'text', label: 'Scene', required: true, defaultValue: 'Starting Soon' },
      { name: 'volume', type: 'numeric', defaultValue: '42' },
      { name: 'muted', type: 'boolean', defaultValue: 'false' },
    ]);
  });

  it('reads a script without declarations as declaring none, never undefined', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript()] });

    await service.loadScripts();

    expect(service.scripts()[0].inputs).toEqual([]);
  });

  it('sends the declarations on update', async () => {
    apiSpy.updateScript.and.resolveTo({ success: true, script: ipcScript() });

    await service.updateScript('script-1', { inputs: [{ name: 'scene', type: 'text' }] });

    expect(apiSpy.updateScript).toHaveBeenCalledWith(jasmine.objectContaining({
      id: 'script-1',
      inputs: [{ name: 'scene', type: 'text' }],
    }));
  });

  it('sends supplied values with a run, typed as entered', async () => {
    apiSpy.runScript.and.resolveTo({ success: true });

    await service.runScript('script-1', { scene: 'Live', volume: 42, muted: true });

    expect(apiSpy.runScript).toHaveBeenCalledWith(jasmine.objectContaining({
      id: 'script-1',
      inputs: { scene: 'Live', volume: 42, muted: true },
    }));
  });

  it('surfaces a failed mutation instead of touching the list', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript()] });
    await service.loadScripts();
    apiSpy.updateScript.and.resolveTo({ success: false, error: { code: 'NotFound', message: 'gone' } });

    const result = await service.updateScript('script-1', { name: 'Renamed' });

    expect(result.success).toBeFalse();
    expect(result.error?.message).toBe('gone');
    expect(service.scripts()[0].name).toBe('Start stream');
  });

  it('applies a script created by another client', () => {
    created.next({ script: ipcScript({ id: 'script-2', name: 'From elsewhere' }) });

    expect(service.scripts().map(s => s.id)).toEqual(['script-2']);
  });

  it('replaces a script updated by another client rather than adding it twice', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript()] });
    await service.loadScripts();

    updated.next({ script: ipcScript({ name: 'Renamed elsewhere' }) });

    expect(service.scripts().length).toBe(1);
    expect(service.scripts()[0].name).toBe('Renamed elsewhere');
  });

  it('drops a script deleted by another client', async () => {
    apiSpy.getScripts.and.resolveTo({ scripts: [ipcScript()] });
    await service.loadScripts();

    deleted.next({ id: 'script-1' });

    expect(service.scripts()).toEqual([]);
  });

  it('sorts by name for the rail', async () => {
    apiSpy.getScripts.and.resolveTo({
      scripts: [
        ipcScript({ id: '1', name: 'Zulu' }),
        ipcScript({ id: '2', name: 'alpha' }),
      ],
    });

    await service.loadScripts();

    expect(service.sortedScripts().map(s => s.name)).toEqual(['alpha', 'Zulu']);
  });
});
