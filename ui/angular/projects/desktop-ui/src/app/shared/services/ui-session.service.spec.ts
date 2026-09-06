import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { NEVER, Subject } from 'rxjs';

import { UiSessionService } from './ui-session.service';
import { ToastService } from './toast.service';
import { ApiService } from '../transport';
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
    ]);
    api.onUiSessionTreeUpdated.and.returnValue(NEVER);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(NEVER);
    api.onUiSessionClosed.and.returnValue(NEVER);

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
      'attachUiSession',
      'detachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
    ]);
    api.onUiSessionTreeUpdated.and.returnValue(treeUpdated);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(invalidated);
    api.onUiSessionClosed.and.returnValue(NEVER);
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
