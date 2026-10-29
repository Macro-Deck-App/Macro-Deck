import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { BehaviorSubject, NEVER, Subject } from 'rxjs';

import { UiSessionService } from './ui-session.service';
import { ToastService } from './toast.service';
import { ApiService, type ConnectionState } from '../transport';
import { LocalizationService } from '../localization';
import { UiConfigEntryPoints, UiModelVersions } from '@macro-deck/runtime';

// Negotiation is local and pre-emptive. A client that attached first and fell back once it saw a tree
// it could not render would produce the same final rendering, so asserting on the DOM would not catch
// it - it burns one of the provider's few session slots and briefly shows an unrenderable tree. The
// assertion has to be that the transport was never called at all.
describe('UiSessionService version negotiation', () => {
  let api: jasmine.SpyObj<ApiService>;
  let toast: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openConfigUiSession',
      'openWidgetUiSession',
      'attachUiSession',
      'detachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
      'flushUiSession',
    ], { connectionState$: new BehaviorSubject<ConnectionState>('connected') });
    api.sendUiEvent.and.resolveTo({ accepted: true });
    api.onUiSessionTreeUpdated.and.returnValue(NEVER);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(NEVER);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(NEVER);
    api.onUiSessionFlushed.and.returnValue(NEVER);

    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: toast },
        {
          provide: LocalizationService,
          useValue: { translateKey: (key: string) => key, translate: (key: string) => key },
        },
      ],
    });
  });

  const request = (configUiModelVersion: number) => ({
    kind: 'config' as const,
    entryPoint: UiConfigEntryPoints.ActionConfig,
    integrationId: 'com.example.demo',
    actionId: 'com.example.demo.toggle',
    configUiModelVersion,
  });

  it('never opens or attaches a session for a UI model version it cannot render', () => {
    const service = TestBed.inject(UiSessionService);

    const handle = service.open(request(UiModelVersions.Current + 1));

    expect(api.openConfigUiSession).not.toHaveBeenCalled();
    expect(api.attachUiSession).not.toHaveBeenCalled();
    // No tree means the consumer keeps rendering the legacy field list.
    expect(handle.root()).toBeNull();
  });

  it('falls back silently rather than reporting a session that never opened', () => {
    const service = TestBed.inject(UiSessionService);

    const handle = service.open(request(UiModelVersions.Minimum - 1));
    handle.send({ nodeId: 'field.a', name: 'change', data: 'x' });
    handle.close();

    expect(api.openConfigUiSession).not.toHaveBeenCalled();
    expect(api.sendUiEvent).not.toHaveBeenCalled();
    expect(api.closeUiSession).not.toHaveBeenCalled();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('opens a session for a caller that states no version to negotiate', () => {
    api.openConfigUiSession.and.returnValue(
      Promise.resolve({ accepted: true, sessionId: 'session-1' }),
    );
    const service = TestBed.inject(UiSessionService);

    // The folder view picker passes 0 deliberately: the provider already agreed the UI capability, and
    // learning the exact major would cost a round trip for a value the picker cannot act on. Negotiating
    // it as a real major declines every such session, because no major is ever zero.
    service.open(request(0));

    expect(api.openConfigUiSession).toHaveBeenCalled();
  });

  it('opens a session for a version it can render', () => {
    api.openConfigUiSession.and.returnValue(
      Promise.resolve({ accepted: true, sessionId: 'session-1' }),
    );
    api.attachUiSession.and.returnValue(
      Promise.resolve({
        accepted: true,
        sessionId: 'session-1',
        surfaceKind: 'config',
        sessionMode: 'exclusive',
        revision: 0,
      }),
    );
    const service = TestBed.inject(UiSessionService);

    service.open(request(UiModelVersions.Current));

    expect(api.openConfigUiSession).toHaveBeenCalled();
  });

  it('opens a widget-config session by widgetId, with no integrationId to carry', () => {
    api.openConfigUiSession.and.returnValue(
      Promise.resolve({ accepted: true, sessionId: 'session-1' }),
    );
    const service = TestBed.inject(UiSessionService);

    service.open({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.WidgetConfig,
      widgetId: 'w1',
      configUiModelVersion: UiModelVersions.Current,
    });

    expect(api.openConfigUiSession).toHaveBeenCalledWith(jasmine.objectContaining({
      entryPoint: UiConfigEntryPoints.WidgetConfig,
      widgetId: 'w1',
    }));
    expect(api.openConfigUiSession.calls.mostRecent().args[0].integrationId).toBeUndefined();
  });
});

const settle = async () => { for (let turn = 0; turn < 10; turn++) await Promise.resolve(); };

describe('UiSessionService widget sessions', () => {
  let api: jasmine.SpyObj<ApiService>;
  let toast: jasmine.SpyObj<ToastService>;
  let invalidated: Subject<{ sessionId: string; code: string; message: string; retryable: boolean }>;
  let treeUpdated: Subject<{ sessionId: string; revision: number; tree: unknown }>;

  beforeEach(() => {
    invalidated = new Subject();
    treeUpdated = new Subject();

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openConfigUiSession',
      'openWidgetUiSession',
      'openUiPreviewSession',
      'attachUiSession',
      'detachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
      'flushUiSession',
    ], { connectionState$: new BehaviorSubject<ConnectionState>('connected') });
    api.sendUiEvent.and.resolveTo({ accepted: true });
    api.onUiSessionTreeUpdated.and.returnValue(treeUpdated);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(invalidated);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(NEVER);
    api.onUiSessionFlushed.and.returnValue(NEVER);
    api.openWidgetUiSession.and.returnValue(Promise.resolve({ accepted: true, sessionId: 'widget-session-1' }));
    api.attachUiSession.and.returnValue(Promise.resolve({
      accepted: true,
      sessionId: 'widget-session-1',
      surfaceKind: 'widget',
      sessionMode: 'shared',
      revision: 0,
    }));

    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: toast },
        {
          provide: LocalizationService,
          useValue: { translateKey: (key: string) => key, translate: (key: string) => key },
        },
      ],
    });
  });

  describe('when the provider has no session slot free', () => {
    const busy = { accepted: false, sessionId: '', code: 'TOO_MANY_SESSIONS', message: 'full' };
    const flush = async () => {
      for (let turn = 0; turn < 6; turn++) await Promise.resolve();
    };

    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('opens a widget again after a backoff instead of showing the refusal', async () => {
      api.openWidgetUiSession.and.returnValues(Promise.resolve(busy), Promise.resolve({
        accepted: true,
        sessionId: 'widget-session-1'
      }));
      const handle = TestBed.inject(UiSessionService).open({ kind: 'widget', widgetId: 'w1' });
      await flush();

      expect(handle.rejection()).toBeNull();
      jasmine.clock().tick(1000);
      await flush();

      expect(api.openWidgetUiSession).toHaveBeenCalledTimes(2);
      expect(api.attachUiSession).toHaveBeenCalledWith('widget-session-1');
      expect(handle.rejection()).toBeNull();
    });

    it('shows the refusal once the last attempt is refused too', async () => {
      api.openWidgetUiSession.and.returnValue(Promise.resolve(busy));
      const handle = TestBed.inject(UiSessionService).open({ kind: 'widget', widgetId: 'w1' });
      await flush();
      for (const delay of [1000, 2000, 4000, 8000, 16000, 16000]) {
        jasmine.clock().tick(delay);
        await flush();
      }

      expect(api.openWidgetUiSession).toHaveBeenCalledTimes(7);
      expect(handle.rejection()).toEqual({ code: 'TOO_MANY_SESSIONS', message: 'full' });
    });

    it('never opens again once closed while it waits', async () => {
      api.openWidgetUiSession.and.returnValue(Promise.resolve(busy));
      const handle = TestBed.inject(UiSessionService).open({ kind: 'widget', widgetId: 'w1' });
      await flush();
      handle.close();
      jasmine.clock().tick(60000);
      await flush();

      expect(api.openWidgetUiSession).toHaveBeenCalledTimes(1);
    });

    it('shows the refusal of a configuration surface at once', async () => {
      api.openConfigUiSession.and.returnValue(Promise.resolve(busy));
      const handle = TestBed.inject(UiSessionService).open({
        kind: 'config',
        entryPoint: UiConfigEntryPoints.WidgetConfig,
        widgetId: 'w1',
        configUiModelVersion: UiModelVersions.Current
      });
      await flush();

      expect(handle.rejection()).toEqual({ code: 'TOO_MANY_SESSIONS', message: 'full' });
    });

    it('shows the refusal of a developer preview at once', async () => {
      api.openUiPreviewSession.and.returnValue(Promise.resolve(busy));
      const handle = TestBed.inject(UiSessionService).open({ kind: 'preview', previewId: 'p1' });
      await flush();

      expect(handle.rejection()).toEqual({ code: 'TOO_MANY_SESSIONS', message: 'full' });
    });
  });

  it('opens via openWidgetUiSession with no version negotiation', () => {
    const service = TestBed.inject(UiSessionService);

    service.open({ kind: 'widget', widgetId: 'w1' });

    expect(api.openConfigUiSession).not.toHaveBeenCalled();
    expect(api.openWidgetUiSession).toHaveBeenCalledWith(jasmine.objectContaining({ widgetId: 'w1' }));
  });

  it('takes the root node out of the tree payload the host actually relays', async () => {
    const service = TestBed.inject(UiSessionService);
    const handle = service.open({ kind: 'widget', widgetId: 'w1' });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    treeUpdated.next({
      sessionId: 'widget-session-1',
      revision: 3,
      tree: {
        revision: 3,
        surface: { kind: 'widget', sessionMode: 'shared', attributes: {} },
        root: { id: 'weather', type: 'ui.stack', properties: {}, children: [] },
      },
    });

    expect(handle.root()?.id).toBe('weather');
    expect(handle.root()?.type).toBe('ui.stack');
    expect(handle.revision()).toBe(3);
  });

  it('ignores a snapshot that carries no usable root', async () => {
    const service = TestBed.inject(UiSessionService);
    const handle = service.open({ kind: 'widget', widgetId: 'w1' });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    treeUpdated.next({ sessionId: 'widget-session-1', revision: 1, tree: { revision: 1, surface: {} } });

    expect(handle.root()).toBeNull();
  });

  it('opens the session again when the host says the view has to be rebuilt', async () => {
    const service = TestBed.inject(UiSessionService);
    const handle = service.open({ kind: 'widget', widgetId: 'w1' });
    await settle();
    treeUpdated.next({ sessionId: 'widget-session-1', revision: 1, tree: { id: 'root', type: 'ui.stack' } });
    api.openWidgetUiSession.calls.reset();

    invalidated.next({
      sessionId: 'widget-session-1',
      code: 'WIDGET_RECONFIGURED',
      message: 'rebuild',
      retryable: true,
    });
    await settle();

    expect(api.openWidgetUiSession).toHaveBeenCalledWith(jasmine.objectContaining({ widgetId: 'w1' }));
    // The tile keeps drawing what it had until the new tree lands, rather than blanking in between.
    expect(handle.root()).not.toBeNull();
  });

  it('opens a replacement from what the caller holds at that moment, and says why it was replaced', async () => {
    const service = TestBed.inject(UiSessionService);
    let draft = 'first';
    const handle = service.open(() => ({ kind: 'widget', widgetId: 'w1', data: { label: draft } }));
    await settle();
    treeUpdated.next({ sessionId: 'widget-session-1', revision: 1, tree: { id: 'root', type: 'ui.stack' } });
    api.openWidgetUiSession.calls.reset();

    draft = 'edited';
    invalidated.next({ sessionId: 'widget-session-1', code: 'PROVIDER_RELOADED', message: 'x', retryable: true });
    await settle();

    expect(api.openWidgetUiSession).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({ widgetId: 'w1', data: { label: 'edited' } }));
    expect(handle.reopenReason?.()).toBe('PROVIDER_RELOADED');
  });

  it('stops reopening a session that is invalidated without ever serving a tree', async () => {
    const service = TestBed.inject(UiSessionService);
    const handle = service.open({ kind: 'widget', widgetId: 'w1' });
    await settle();
    treeUpdated.next({ sessionId: 'widget-session-1', revision: 1, tree: { id: 'root', type: 'ui.stack' } });

    for (let attempt = 0; attempt < 8; attempt++) {
      invalidated.next({ sessionId: 'widget-session-1', code: 'x', message: 'x', retryable: true });
      await settle();
    }

    expect(handle.root()).toBeNull();
    expect(handle.rejection()).toBeNull();
  });

  it('never toasts when a widget session faults, even after showing a tree', async () => {
    const service = TestBed.inject(UiSessionService);
    const handle = service.open({ kind: 'widget', widgetId: 'w1' });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    treeUpdated.next({ sessionId: 'widget-session-1', revision: 1, tree: { id: 'root', type: 'ui.stack' } });

    expect(handle.root()).not.toBeNull();

    invalidated.next({ sessionId: 'widget-session-1', code: 'gone', message: 'gone', retryable: false });

    expect(handle.root()).toBeNull();
    expect(toast.show).not.toHaveBeenCalled();
  });
});

describe('UiSessionService sessions that end before or while they are shown', () => {
  let api: jasmine.SpyObj<ApiService>;
  let toast: jasmine.SpyObj<ToastService>;
  let invalidated: Subject<{ sessionId: string; code: string; message: string; retryable: boolean }>;
  let treeUpdated: Subject<{ sessionId: string; revision: number; tree: unknown }>;
  let patched: Subject<{ sessionId: string; fromRevision: number; toRevision: number; patch: unknown }>;
  let latestSessionId: string;

  const accepted = (sessionId: string) => ({
    accepted: true, sessionId, surfaceKind: 'config', sessionMode: 'exclusive', revision: 0,
  });
  const refused = (sessionId: string) => ({
    accepted: false, sessionId, code: 'SESSION_NOT_FOUND', message: 'gone', surfaceKind: '', sessionMode: '', revision: 0,
  });

  const request = {
    kind: 'config' as const,
    entryPoint: UiConfigEntryPoints.WidgetConfig,
    widgetId: 'w1',
    configUiModelVersion: 0,
  };

  const showTree = (sessionId: string) =>
    treeUpdated.next({ sessionId, revision: 1, tree: { id: 'root', type: 'ui.stack' } });
  const sendUnappliablePatch = (sessionId: string) =>
    patched.next({ sessionId, fromRevision: 7, toRevision: 8, patch: { operations: [] } });
  const end = (sessionId: string, retryable: boolean) =>
    invalidated.next({ sessionId, code: 'PROVIDER_REJECTED', message: 'declined', retryable });

  beforeEach(() => {
    invalidated = new Subject();
    treeUpdated = new Subject();
    patched = new Subject();
    let opened = 0;

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openConfigUiSession',
      'attachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
      'flushUiSession',
    ], { connectionState$: new BehaviorSubject<ConnectionState>('connected') });
    api.sendUiEvent.and.resolveTo({ accepted: true });
    api.onUiSessionTreeUpdated.and.returnValue(treeUpdated);
    api.onUiSessionPatched.and.returnValue(patched);
    api.onUiSessionInvalidated.and.returnValue(invalidated);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(NEVER);
    api.onUiSessionFlushed.and.returnValue(NEVER);
    api.openConfigUiSession.and.callFake(() => {
      latestSessionId = `config-session-${++opened}`;
      return Promise.resolve({ accepted: true, sessionId: latestSessionId });
    });
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(accepted(sessionId)));

    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: toast },
        {
          provide: LocalizationService,
          useValue: { translateKey: (key: string) => key, translate: (key: string) => key },
        },
      ],
    });
  });

  it('ends in a rejection when the provider declines before the first attach, instead of waiting forever', async () => {
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(refused(sessionId)));

    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();

    expect(handle.rejection()).toEqual({ code: 'SESSION_NOT_FOUND', message: 'gone' });
    expect(handle.root()).toBeNull();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('ends in a rejection carrying what the host said when the session ends before any tree', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();

    end(latestSessionId, false);

    expect(handle.rejection()).toEqual({ code: 'PROVIDER_REJECTED', message: 'declined' });
  });

  it('ends in a rejection when reopening never produces a tree', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();

    for (let attempt = 0; attempt < 8; attempt++) {
      end(latestSessionId, true);
      await settle();
    }

    expect(handle.rejection()).not.toBeNull();
  });

  it('says why a shown session ended, and forgets it once a tree arrives again', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();
    showTree(latestSessionId);
    await settle();

    end(latestSessionId, false);
    await settle();
    expect(handle.fault?.()).toEqual({ code: 'PROVIDER_REJECTED', message: 'declined' });
    expect(handle.rejection()).toBeNull();

    showTree(latestSessionId);
    await settle();
    expect(handle.fault?.()).toBeNull();
  });

  it('keeps the shown tree when a re-attach after a failed patch gets no answer at all', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();
    showTree(latestSessionId);
    api.attachUiSession.and.resolveTo(null);

    sendUnappliablePatch(latestSessionId);
    await settle();

    expect(handle.root()).not.toBeNull();
    expect(handle.rejection()).toBeNull();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('leaves the end of a shown view to a caller that explains it itself', async () => {
    const handle = TestBed.inject(UiSessionService).open(request, { notifyFault: false });
    await settle();
    showTree(latestSessionId);

    end(latestSessionId, false);
    await settle();

    expect(handle.root()).toBeNull();
    expect(handle.fault?.()).not.toBeNull();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('reports a lost view once, and no rejection, when a refused re-attach is followed by the end of the session', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();
    showTree(latestSessionId);
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(refused(sessionId)));

    sendUnappliablePatch(latestSessionId);
    await settle();
    end(latestSessionId, false);
    await settle();

    expect(toast.show).toHaveBeenCalledTimes(1);
    expect(handle.rejection()).toBeNull();
    expect(handle.root()).toBeNull();
  });

  it('never blanks the tree when a refused re-attach arrives before the session is reopened', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();
    const first = latestSessionId;
    showTree(first);
    api.attachUiSession.and.callFake(sessionId =>
      Promise.resolve(sessionId === first ? refused(sessionId) : accepted(sessionId)));

    sendUnappliablePatch(first);
    await settle();
    expect(handle.root()).not.toBeNull();

    end(first, true);
    await settle();

    expect(api.openConfigUiSession).toHaveBeenCalledTimes(2);
    expect(handle.root()).not.toBeNull();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('ignores a refused answer about a session it has already replaced', async () => {
    const handle = TestBed.inject(UiSessionService).open(request);
    await settle();
    const first = latestSessionId;
    showTree(first);

    let answerStaleAttach!: (answer: ReturnType<typeof refused>) => void;
    api.attachUiSession.and.returnValue(new Promise(resolve => { answerStaleAttach = resolve; }));
    sendUnappliablePatch(first);
    await settle();

    api.attachUiSession.and.callFake(sessionId => Promise.resolve(accepted(sessionId)));
    end(first, true);
    await settle();
    answerStaleAttach(refused(first));
    await settle();

    expect(toast.show).not.toHaveBeenCalled();
    expect(handle.root()).not.toBeNull();

    end(latestSessionId, false);
    await settle();

    expect(toast.show).toHaveBeenCalledTimes(1);
    expect(handle.root()).toBeNull();
  });
});

describe('UiSessionService reported values and flush', () => {
  let api: jasmine.SpyObj<ApiService>;
  let reported: Subject<{ sessionId: string; values: Record<string, unknown> }>;
  let flushed: Subject<{ sessionId: string; flushId: string }>;

  beforeEach(() => {
    reported = new Subject();
    flushed = new Subject();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openWidgetUiSession',
      'attachUiSession',
      'closeUiSession',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
      'flushUiSession',
    ], { connectionState$: new BehaviorSubject<ConnectionState>('connected') });
    api.onUiSessionTreeUpdated.and.returnValue(NEVER);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(NEVER);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(reported);
    api.onUiSessionFlushed.and.returnValue(flushed);
    api.openWidgetUiSession.and.returnValue(Promise.resolve({ accepted: true, sessionId: 's1' }));
    api.attachUiSession.and.returnValue(Promise.resolve({
      accepted: true, sessionId: 's1', surfaceKind: 'config', sessionMode: 'exclusive', revision: 0,
    }));

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: jasmine.createSpyObj<ToastService>('ToastService', ['show']) },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    });
  });

  async function openHandle() {
    const handle = TestBed.inject(UiSessionService).open({ kind: 'widget', widgetId: 'w1' });
    await settle();
    return handle;
  }

  it('hands a report for its own session to its listeners as it arrives', async () => {
    const handle = await openHandle();
    const received: unknown[] = [];
    handle.onParameterValuesReported!(values => received.push(values));

    reported.next({ sessionId: 'other', values: { title: 'no' } });
    reported.next({ sessionId: 's1', values: { title: 'yes' } });

    expect(received).toEqual([{ title: 'yes' }]);
  });

  it('settles a flush when the host confirms that flush', async () => {
    api.flushUiSession.and.returnValue(Promise.resolve({ accepted: true }));
    const handle = await openHandle();
    let done = false;

    const flush = handle.flush!().then(() => done = true);
    await settle();
    expect(done).toBeFalse();

    const { flushId } = api.flushUiSession.calls.mostRecent().args[0];
    flushed.next({ sessionId: 's1', flushId });
    await flush;

    expect(done).toBeTrue();
  });

  it('treats a host that does not know flushing as flushed at once', async () => {
    api.flushUiSession.and.returnValue(Promise.resolve(null));
    const handle = await openHandle();

    await expectAsync(handle.flush!()).toBeResolved();
  });
});

describe('UiSessionService sessions across a reconnect', () => {
  let api: jasmine.SpyObj<ApiService>;
  let connection: BehaviorSubject<ConnectionState>;
  let opened: number;
  let parameters: Record<string, unknown>;

  const attached = (sessionId: string) => ({
    accepted: true, sessionId, surfaceKind: 'config', sessionMode: 'exclusive', revision: 3,
  });
  const notAttached = (sessionId: string) => ({
    accepted: false, sessionId, code: 'SESSION_NOT_FOUND', message: 'gone', surfaceKind: '', sessionMode: '', revision: 0,
  });
  const configRequest = () => ({
    kind: 'config' as const,
    entryPoint: UiConfigEntryPoints.ActionConfig,
    integrationId: 'com.example.demo',
    actionId: 'com.example.demo.toggle',
    parameters,
    configUiModelVersion: 0,
  });

  beforeEach(() => {
    connection = new BehaviorSubject<ConnectionState>('connected');
    opened = 0;
    parameters = { device: 'old' };
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openConfigUiSession',
      'attachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
      'flushUiSession',
    ], { connectionState$: connection });
    api.onUiSessionTreeUpdated.and.returnValue(NEVER);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(NEVER);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(NEVER);
    api.onUiSessionFlushed.and.returnValue(NEVER);
    api.openConfigUiSession.and.callFake(() => Promise.resolve({ accepted: true, sessionId: `config-session-${++opened}` }));
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(attached(sessionId)));
    api.closeUiSession.and.resolveTo(true);
    api.sendUiEvent.and.resolveTo({ accepted: true });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: jasmine.createSpyObj<ToastService>('ToastService', ['show']) },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    });
  });

  async function openEditor() {
    const handle = TestBed.inject(UiSessionService).open(configRequest);
    await settle();
    return handle;
  }

  async function reconnect() {
    connection.next('reconnecting');
    connection.next('connected');
    await settle();
  }

  const sentTo = () => api.sendUiEvent.calls.allArgs()
    .map(([request]) => ({ sessionId: request.sessionId, nodeId: request.nodeId }));

  it('keeps the session it had while the host still holds it for this device', async () => {
    const handle = await openEditor();

    await reconnect();
    handle.send({ nodeId: 'device', name: 'change', data: 'new' });
    await settle();

    expect(api.openConfigUiSession).toHaveBeenCalledTimes(1);
    expect(api.attachUiSession).toHaveBeenCalledWith('config-session-1');
    expect(sentTo()).toEqual([{ sessionId: 'config-session-1', nodeId: 'device' }]);
    expect(handle.generation()).toBe(1);
  });

  it('opens a new session with the current values once the old one is gone', async () => {
    const handle = await openEditor();
    parameters = { device: 'edited' };
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(
      sessionId === 'config-session-1' ? notAttached(sessionId) : attached(sessionId)));

    await reconnect();
    handle.send({ nodeId: 'device', name: 'change', data: 'newer' });
    await settle();

    expect(api.closeUiSession).toHaveBeenCalledWith('config-session-1');
    expect(api.openConfigUiSession.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({
      parameters: { device: 'edited' },
    }));
    expect(sentTo().at(-1)).toEqual({ sessionId: 'config-session-2', nodeId: 'device' });
    expect(handle.generation()).toBe(2);
  });

  it('re-attaches once and re-sends refused edits in order when the connection is not attached', async () => {
    const handle = await openEditor();
    let attachedAgain = false;
    api.sendUiEvent.and.callFake(() => Promise.resolve(attachedAgain
      ? { accepted: true }
      : { accepted: false, code: 'SESSION_FORBIDDEN', message: 'not attached' }));
    api.attachUiSession.and.callFake(sessionId => {
      attachedAgain = true;
      return Promise.resolve(attached(sessionId));
    });
    api.attachUiSession.calls.reset();

    handle.send({ nodeId: 'first', name: 'change' });
    handle.send({ nodeId: 'second', name: 'change' });
    await settle();

    expect(api.attachUiSession).toHaveBeenCalledTimes(1);
    expect(sentTo()).toEqual([
      { sessionId: 'config-session-1', nodeId: 'first' },
      { sessionId: 'config-session-1', nodeId: 'second' },
      { sessionId: 'config-session-1', nodeId: 'first' },
      { sessionId: 'config-session-1', nodeId: 'second' },
    ]);
    expect(api.openConfigUiSession).toHaveBeenCalledTimes(1);
  });

  it('reopens when an edit is refused because the session no longer exists', async () => {
    const handle = await openEditor();
    api.sendUiEvent.and.resolveTo({ accepted: false, code: 'SESSION_NOT_FOUND', message: 'gone' });
    api.attachUiSession.and.callFake(sessionId => Promise.resolve(
      sessionId === 'config-session-1' ? notAttached(sessionId) : attached(sessionId)));

    handle.send({ nodeId: 'device', name: 'change' });
    await settle();

    expect(api.openConfigUiSession).toHaveBeenCalledTimes(2);
    expect(handle.generation()).toBe(2);
  });

  it('shows the session as ended when the host closed it, instead of reviving it', async () => {
    const handle = await openEditor();
    api.sendUiEvent.and.resolveTo({ accepted: false, code: 'SESSION_CLOSED', message: 'ended' });

    handle.send({ nodeId: 'device', name: 'change' });
    await settle();

    expect(handle.fault?.()).toEqual({ code: 'SESSION_CLOSED', message: 'ended' });
    expect(api.openConfigUiSession).toHaveBeenCalledTimes(1);
  });

  it('brings a view that had ended back after a reconnect', async () => {
    const handle = await openEditor();
    api.sendUiEvent.and.resolveTo({ accepted: false, code: 'SESSION_CLOSED', message: 'ended' });
    handle.send({ nodeId: 'device', name: 'change' });
    await settle();

    await reconnect();

    expect(api.openConfigUiSession).toHaveBeenCalledTimes(2);
    expect(handle.generation()).toBe(2);
  });

  it('closes a session that only finished opening after a reconnect replaced it', async () => {
    let finishFirstOpen: (value: { accepted: boolean; sessionId: string }) => void = () => undefined;
    api.openConfigUiSession.and.returnValues(
      new Promise(resolve => { finishFirstOpen = resolve; }),
      Promise.resolve({ accepted: true, sessionId: 'config-session-2' }));
    const handle = TestBed.inject(UiSessionService).open(configRequest);

    await reconnect();
    finishFirstOpen({ accepted: true, sessionId: 'config-session-1' });
    await settle();
    handle.send({ nodeId: 'device', name: 'change' });
    await settle();

    expect(api.closeUiSession).toHaveBeenCalledWith('config-session-1');
    expect(handle.generation()).toBe(1);
    expect(sentTo()).toEqual([{ sessionId: 'config-session-2', nodeId: 'device' }]);
  });
});
