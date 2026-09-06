import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { STATE_LABEL_VARIABLE_NAME, STATE_VARIABLE_NAME, Variable } from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { VariableService } from './variable.service';

function globalVar(name: string): Variable {
  return {
    id: `global-${name}`,
    name,
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
  };
}

function buttonVar(name: string, scopeRefId: string, classification: Variable['classification'] = 'user'): Variable {
  return {
    id: `button-${scopeRefId}-${name}`,
    name,
    scope: 'widget',
    scopeRefId,
    type: 'text',
    classification,
    value: 'off',
  };
}

describe('VariableService', () => {
  const widgetId = 'widget-1';
  let service: VariableService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getVariables']);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(VariableService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot when the transport connects', async () => {
      api.getVariables.and.resolveTo({ variables: [globalVar('cpu')] });

      connectionState.set('connected');
      await TestBed.inject(VariableService).loadVariables();
      TestBed.tick();

      expect(api.getVariables).toHaveBeenCalled();
      expect(service.variables().map(v => v.name)).toEqual(['cpu']);
    });

    it('pulls again on a reconnect, so values missed while offline are repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.loadVariables();
      const afterFirst = api.getVariables.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.loadVariables();

      expect(api.getVariables.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one request', async () => {
      const before = api.getVariables.calls.count();

      await Promise.all([service.loadVariables(), service.loadVariables(), service.loadVariables()]);

      expect(api.getVariables.calls.count()).toBe(before + 1);
    });

    it('keeps a push that arrived while the snapshot was in flight', async () => {
      const stale = { ...globalVar('cpu'), value: '10' };
      const fresh = { ...globalVar('cpu'), value: '90' };
      let release: (value: { variables: Variable[] }) => void = () => undefined;
      api.getVariables.and.returnValue(new Promise(resolve => {
        release = resolve;
      }));

      const load = service.loadVariables();
      push('VariablesChangedEvent', { upserted: [fresh], deletedIds: [] });
      release({ variables: [stale] });
      await load;

      expect(service.variables()).toEqual([fresh]);
    });

    it('keeps a deletion that arrived while the snapshot was in flight', async () => {
      const doomed = globalVar('cpu');
      let release: (value: { variables: Variable[] }) => void = () => undefined;
      api.getVariables.and.returnValue(new Promise(resolve => {
        release = resolve;
      }));

      const load = service.loadVariables();
      push('VariablesChangedEvent', { upserted: [], deletedIds: [doomed.id] });
      release({ variables: [doomed] });
      await load;

      expect(service.variables()).toEqual([]);
    });

    // Guards the notification name itself: a client left subscribed to a retired event name would
    // silently freeze every value on screen instead of throwing.
    it('applies a batch pushed under the VariablesChangedEvent name, not a per-variable event', async () => {
      const a = { ...globalVar('a'), value: '1' };
      const b = { ...globalVar('b'), value: '2' };
      service.variables.set([a, b]);

      const freshA = { ...a, value: '9' };
      const c = { ...globalVar('c'), value: '3' };
      push('VariablesChangedEvent', { upserted: [freshA, c], deletedIds: [b.id] });

      expect(new Set(service.variables().map(v => v.id))).toEqual(new Set(['a', 'c'].map(n => `global-${n}`)));
      expect(service.resolve('a')!.value).toBe('9');
      expect(service.resolve('b')).toBeUndefined();
    });
  });

  describe('applyBatch', () => {
    it('lets a deletion win over an upsert for the same id, defensively, though the host guarantees they are disjoint', () => {
      const x = globalVar('x');
      service.variables.set([x]);

      service.applyBatch([x], [x.id]);

      expect(service.variables()).toEqual([]);
    });

    it('keeps the cache repair overlay in sync so an in-flight loadVariables cannot roll a batch back', async () => {
      const a = { ...globalVar('a'), value: '1' };
      const b = globalVar('b');
      let release: (value: { variables: Variable[] }) => void = () => undefined;
      api.getVariables.and.returnValue(new Promise(resolve => {
        release = resolve;
      }));

      const load = service.loadVariables();
      service.applyBatch([{ ...a, value: '9' }], [b.id]);
      release({ variables: [{ ...a, value: '1' }, b] });
      await load;

      expect(service.resolve('a')!.value).toBe('9');
      expect(service.resolve('b')).toBeUndefined();
    });

    it('tolerates deleting an id that does not exist', () => {
      service.variables.set([globalVar('a')]);

      expect(() => service.applyBatch([], ['no-such-id'])).not.toThrow();
      expect(service.variables().map(v => v.id)).toEqual(['global-a']);
    });

    it('keeps the last value when the same id is upserted twice in one batch', () => {
      const x1 = { ...globalVar('x'), value: 'v1' };
      const x2 = { ...globalVar('x'), value: 'v2' };

      service.applyBatch([x1, x2], []);

      expect(service.variables().length).toBe(1);
      expect(service.variables()[0].value).toBe('v2');
    });

    it('does not touch the signal for a no-op batch', () => {
      service.variables.set([globalVar('a')]);
      const before = service.variables();

      service.applyBatch([], []);

      expect(service.variables()).toBe(before);
    });
  });

  // G12 / resolution #10: `vars.state`/`vars.stateLabel` are pickable the instant State Mode is
  // switched on (before any save creates the host-managed variable), and disappear again - like
  // `vars.toggled` used to - the instant it is switched off.
  describe('visibleForActionButtonEditor state variables', () => {
    it('offers pending state and stateLabel stand-ins in State Mode before the widget is saved', () => {
      service.variables.set([globalVar('foo')]);

      const visible = service.visibleForActionButtonEditor(widgetId, true);

      expect(visible.map(v => v.name)).toContain(STATE_VARIABLE_NAME);
      expect(visible.map(v => v.name)).toContain(STATE_LABEL_VARIABLE_NAME);
      expect(visible.map(v => v.name)).toContain('foo');
    });

    it('uses the real host variables instead of the stand-ins once they exist', () => {
      const realState = buttonVar(STATE_VARIABLE_NAME, widgetId, 'widget');
      const realLabel = buttonVar(STATE_LABEL_VARIABLE_NAME, widgetId, 'widget');
      service.variables.set([realState, realLabel]);

      const visible = service.visibleForActionButtonEditor(widgetId, true);

      expect(visible.filter(v => v.name === STATE_VARIABLE_NAME)).toEqual([realState]);
      expect(visible.filter(v => v.name === STATE_LABEL_VARIABLE_NAME)).toEqual([realLabel]);
    });

    it('hides both host state variables once the edited mode is State-Mode-off', () => {
      service.variables.set([
        buttonVar(STATE_VARIABLE_NAME, widgetId, 'widget'),
        buttonVar(STATE_LABEL_VARIABLE_NAME, widgetId, 'widget'),
        globalVar('foo'),
      ]);

      const visible = service.visibleForActionButtonEditor(widgetId, false);

      expect(visible.map(v => v.name)).not.toContain(STATE_VARIABLE_NAME);
      expect(visible.map(v => v.name)).not.toContain(STATE_LABEL_VARIABLE_NAME);
      expect(visible.map(v => v.name)).toContain('foo');
    });

    it('keeps a user-created button variable named "state" while State Mode is off', () => {
      service.variables.set([buttonVar(STATE_VARIABLE_NAME, widgetId, 'user')]);

      const visible = service.visibleForActionButtonEditor(widgetId, false);

      expect(visible.map(v => v.name)).toContain(STATE_VARIABLE_NAME);
    });

    it('lets button-scoped variables shadow globals with the same name', () => {
      service.variables.set([globalVar('foo'), buttonVar('foo', widgetId)]);

      const visible = service.visibleForActionButtonEditor(widgetId, false);

      const foos = visible.filter(v => v.name === 'foo');
      expect(foos.length).toBe(1);
      expect(foos[0].scope).toBe('widget');
    });

    it('falls back to globals when no scopeRefId is given', () => {
      service.variables.set([globalVar('foo'), buttonVar('bar', widgetId)]);

      const visible = service.visibleForActionButtonEditor(undefined, true);

      expect(visible.map(v => v.name)).toEqual(['foo']);
    });
  });
});
