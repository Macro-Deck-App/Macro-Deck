import { Injectable, Signal, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';

import { ApiService, type ConnectionState } from '../transport';
import {
  applyUiPatch,
  AppStrings,
  type OpenConfigUiSessionRequest,
  type OpenFolderUiSessionRequest,
  type OpenModalUiSessionRequest,
  type OpenPageUiSessionRequest,
  type OpenUiPreviewSessionRequest,
  type OpenWidgetUiSessionRequest,
  rootNodeOf,
  supportsUiModelVersion,
  UiNode,
  UiNodeEvent,
  UiPatch,
  type UiContentUploadResponse,
  type UiSessionFlushedEvent,
  type UiSessionParameterValuesReportedEvent,
  type UiSessionPatchedEvent,
  type UiSessionTreeUpdatedEvent,
  UiConfigEvents,
} from '@macro-deck/runtime';
import { LocalizationService } from '../localization';
import { ToastService } from './toast.service';

export type UiSessionOpenRequest =
  | ({ kind: 'config' } & OpenConfigUiSessionRequest & { configUiModelVersion: number })
  | ({ kind: 'widget' } & OpenWidgetUiSessionRequest)
  | ({ kind: 'folder' } & OpenFolderUiSessionRequest)
  | ({ kind: 'modal' } & OpenModalUiSessionRequest)
  | ({ kind: 'preview' } & OpenUiPreviewSessionRequest)
  | ({ kind: 'page' } & OpenPageUiSessionRequest);

export type UiSessionOpenRequestSource = UiSessionOpenRequest | (() => UiSessionOpenRequest);

export interface UiSessionHandle {
  readonly root: Signal<UiNode | null>;
  readonly revision: Signal<number>;
  readonly rejection: Signal<UiSessionRejection | null>;
  readonly fault?: Signal<UiSessionRejection | null>;
  readonly generation: Signal<number>;
  readonly reopenReason?: Signal<string | null>;
  send(event: UiNodeEvent): void;
  close(): void;
  // Synchronous rather than a signal: a save that awaited flush() reads the store right after, before any
  // effect would have run.
  onParameterValuesReported?(listener: (values: Readonly<Record<string, unknown>>) => void): () => void;
  flush?(): Promise<void>;
  // Events sent while an upload is in flight are held until its content-uploaded or content-upload-failed
  // event went out, so the provider reads the edited text before anything the user did afterwards.
  uploadContent?(nodeId: string, text: string): Promise<UiContentUploadResponse | null>;
}

export interface UiSessionOpenOptions {
  notifyFault?: boolean;
}

export interface UiSessionRejection {
  code?: string;
  message?: string;
}

function isNegotiableVersion(version: number): boolean {
  return version > 0;
}

@Injectable({ providedIn: 'root' })
export class UiSessionService {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  open(source: UiSessionOpenRequestSource, options: UiSessionOpenOptions = {}): UiSessionHandle {
    const request = typeof source === 'function' ? untracked(source) : source;
    if (request.kind === 'config' && isNegotiableVersion(request.configUiModelVersion)
      && !supportsUiModelVersion(request.configUiModelVersion)) {
      // Attaching and then falling back would burn one of the provider's few session slots and
      // briefly show an unrenderable tree - negotiation has to happen before OpenConfigUiSession is
      // ever called.
      return new NullUiSessionHandle();
    }

    return new LiveUiSessionHandle(this.api, this.toast, this.localization, request,
      typeof source === 'function' ? source : null, options.notifyFault ?? true);
  }
}

class NullUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly generation = signal(0);

  send(): void {
    // No session was ever opened; nothing to send to.
  }

  close(): void {
    // No session was ever opened; nothing to close.
  }
}

const MAX_REOPENS_WITHOUT_A_TREE = 3;
const MAX_BUSY_RETRIES = 6;

const TOO_MANY_SESSIONS = 'TOO_MANY_SESSIONS';

const SESSION_CLOSED = 'SESSION_CLOSED';
const SESSION_FORBIDDEN = 'SESSION_FORBIDDEN';
const SESSION_NOT_FOUND = 'SESSION_NOT_FOUND';

let nextFlushId = 0;

class LiveUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly fault = signal<UiSessionRejection | null>(null);
  readonly generation = signal(0);
  readonly reopenReason = signal<string | null>(null);

  private sessionId: string | null = null;
  private closed = false;
  // Whether a closed/invalidated notification should surface to the user: only once a tree was
  // actually shown, and never for our own deliberate close().
  private hadTree = false;
  private readonly subscription = new Subscription();
  private reopens = 0;
  private readonly reportListeners = new Set<(values: Readonly<Record<string, unknown>>) => void>();
  private readonly pendingFlushes = new Map<string, () => void>();
  private uploadsInFlight = 0;
  private heldEvents: UiNodeEvent[] = [];
  private starts = 0;
  private busyRetries = 0;
  private busyRetry: ReturnType<typeof setTimeout> | null = null;
  private recovering: Promise<void> | null = null;
  private refusedEvents: UiNodeEvent[] = [];

  constructor(
    private readonly api: ApiService,
    private readonly toast: ToastService,
    private readonly localization: LocalizationService,
    private request: UiSessionOpenRequest,
    private readonly nextRequest: (() => UiSessionOpenRequest) | null,
    private readonly notifyFault: boolean,
  ) {
    // Subscribed before the first realtime request goes out, so no notification for this session can arrive
    // and be missed while sessionId is still unknown.
    this.subscription.add(this.api.onUiSessionTreeUpdated().subscribe(event => this.onTreeUpdated(event)));
    this.subscription.add(this.api.onUiSessionPatched().subscribe(event => this.onPatched(event)));
    this.subscription.add(this.api.onUiSessionInvalidated().subscribe(event => {
      if (event.sessionId !== this.sessionId) return;
      const rejection = { code: event.code, message: event.message };
      if (event.retryable) this.reopen(rejection); else this.onFaulted(rejection);
    }));
    this.subscription.add(this.api.onUiSessionClosed().subscribe(event => {
      if (event.sessionId === this.sessionId) this.onFaulted({ message: event.reason });
    }));
    this.subscription.add(this.api.onUiSessionParameterValuesReported()
      .subscribe(event => this.onParameterValuesReportedEvent(event)));
    this.subscription.add(this.api.onUiSessionFlushed().subscribe(event => this.onFlushed(event)));
    let connection: ConnectionState | null = null;
    this.subscription.add(this.api.connectionState$.subscribe(state => {
      const previous = connection;
      connection = state;
      if (previous === null || previous === 'connected' || state !== 'connected') return;
      this.reopens = 0;
      void this.recover();
    }));

    void this.start();
  }

  send(event: UiNodeEvent): void {
    if (this.closed || !this.sessionId) return;
    if (this.uploadsInFlight > 0) {
      this.heldEvents.push(event);
      return;
    }
    this.dispatch(event);
  }

  async uploadContent(nodeId: string, text: string): Promise<UiContentUploadResponse | null> {
    const sessionId = this.sessionId;
    if (this.closed || !sessionId) return null;

    this.uploadsInFlight++;
    try {
      const uploaded = await this.api.uploadUiContent(sessionId, nodeId, text).catch(() => null);
      if (!this.closed && this.sessionId === sessionId) {
        this.dispatch(uploaded
          ? {
            nodeId,
            name: UiConfigEvents.ContentUploaded,
            data: { uploadId: uploaded.uploadId, length: uploaded.length, contentHash: uploaded.contentHash },
          }
          : { nodeId, name: UiConfigEvents.ContentUploadFailed });
      }
      return uploaded;
    } finally {
      this.uploadsInFlight--;
      if (this.uploadsInFlight === 0) this.releaseHeldEvents();
    }
  }

  private releaseHeldEvents(): void {
    const held = this.heldEvents;
    this.heldEvents = [];
    for (const event of held) this.send(event);
  }

  private dispatch(event: UiNodeEvent): void {
    const sessionId = this.sessionId;
    if (this.closed || !sessionId) return;
    void this.api.sendUiEvent({
      sessionId,
      nodeId: event.nodeId,
      name: event.name,
      data: event.data,
      revision: this.revision(),
    }).then(response => {
      if (!response || response.accepted || this.closed || this.sessionId !== sessionId) return;
      if (response.code === SESSION_CLOSED) {
        this.onFaulted({ code: response.code, message: response.message });
      } else if (response.code === SESSION_FORBIDDEN || response.code === SESSION_NOT_FOUND) {
        this.refusedEvents.push(event);
        void this.recover();
      }
    });
  }

  // The host drains a dropped connection's sessions for a while and lets the same device re-attach them.
  private recover(): Promise<void> {
    this.recovering ??= this.reattachOrReopen().finally(() => {
      this.recovering = null;
    });
    return this.recovering;
  }

  private async reattachOrReopen(): Promise<void> {
    const sessionId = this.sessionId;
    if (this.closed) return;

    let refusal: UiSessionRejection = {};
    if (sessionId && this.fault() === null) {
      const attached = await this.api.attachUiSession(sessionId);
      if (this.closed || this.sessionId !== sessionId) return;
      if (attached?.accepted) {
        this.revision.set(attached.revision);
        const refused = this.refusedEvents;
        this.refusedEvents = [];
        for (const event of refused) this.send(event);
        return;
      }
      refusal = { code: attached?.code, message: attached?.message };
      void this.api.closeUiSession(sessionId);
    }

    this.refusedEvents = [];
    this.reopen(refusal);
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.cancelBusyRetry();
    this.subscription.unsubscribe();
    this.reportListeners.clear();
    this.settleFlushes();
    this.root.set(null);
    if (this.sessionId) {
      void this.api.closeUiSession(this.sessionId);
    }
  }

  onParameterValuesReported(listener: (values: Readonly<Record<string, unknown>>) => void): () => void {
    this.reportListeners.add(listener);
    return () => this.reportListeners.delete(listener);
  }

  flush(): Promise<void> {
    const sessionId = this.sessionId;
    if (this.closed || !sessionId) return Promise.resolve();

    const flushId = `flush-${++nextFlushId}`;
    return new Promise<void>(resolve => {
      this.pendingFlushes.set(flushId, resolve);
      void this.api.flushUiSession({ sessionId, flushId }).then(response => {
        // A host that predates flushing answers with an error, and a refused flush has nothing queued.
        if (!response?.accepted) this.settleFlush(flushId);
      });
    });
  }

  private onParameterValuesReportedEvent(event: UiSessionParameterValuesReportedEvent): void {
    if (event.sessionId !== this.sessionId || this.closed || !event.values) return;
    for (const listener of [...this.reportListeners]) listener(event.values);
  }

  private onFlushed(event: UiSessionFlushedEvent): void {
    if (event.sessionId !== this.sessionId) return;
    this.settleFlush(event.flushId);
  }

  private settleFlush(flushId: string): void {
    const resolve = this.pendingFlushes.get(flushId);
    if (!resolve) return;
    this.pendingFlushes.delete(flushId);
    resolve();
  }

  private settleFlushes(): void {
    for (const resolve of this.pendingFlushes.values()) resolve();
    this.pendingFlushes.clear();
  }

  private async start(): Promise<void> {
    this.cancelBusyRetry();
    const start = ++this.starts;
    const opened = await this.openSession();
    if (this.closed || start !== this.starts) {
      // A reconnect can start a reopen while an earlier open is still in flight.
      if (opened?.accepted) void this.api.closeUiSession(opened.sessionId);
      return;
    }

    if (!opened?.accepted) {
      if (opened?.code === TOO_MANY_SESSIONS && this.retriesWhenBusy() && this.busyRetries < MAX_BUSY_RETRIES) {
        this.retryWhenFree();
        return;
      }
      this.busyRetries = 0;
      this.rejection.set({ code: opened?.code, message: opened?.message });
      return;
    }

    this.busyRetries = 0;
    this.rejection.set(null);
    this.sessionId = opened.sessionId;
    this.generation.update(value => value + 1);
    await this.attach(true);
  }

  private retriesWhenBusy(): boolean {
    return this.request.kind === 'widget' || this.request.kind === 'folder';
  }

  private retryWhenFree(): void {
    const delay = Math.min(1000 * 2 ** this.busyRetries, 16000);
    this.busyRetries++;
    this.busyRetry = setTimeout(() => {
      this.busyRetry = null;
      if (!this.closed) void this.start();
    }, delay);
  }

  private cancelBusyRetry(): void {
    if (this.busyRetry === null) return;
    clearTimeout(this.busyRetry);
    this.busyRetry = null;
  }

  private openSession(): Promise<{ accepted: boolean; sessionId: string; code?: string; message?: string } | null> {
    switch (this.request.kind) {
      case 'config': return this.api.openConfigUiSession(this.request);
      case 'widget': return this.api.openWidgetUiSession(this.request);
      case 'folder': return this.api.openFolderUiSession(this.request);
      case 'modal': return this.api.openModalUiSession(this.request);
      case 'preview': return this.api.openUiPreviewSession(this.request);
      case 'page': return this.api.openPageUiSession(this.request);
    }
  }

  private async attach(first = false): Promise<void> {
    const sessionId = this.sessionId;
    if (this.closed || !sessionId) return;

    const attached = await this.api.attachUiSession(sessionId);
    if (this.closed || this.sessionId !== sessionId || !attached) return;

    if (!attached.accepted) {
      // Only a first attach can learn of its session's end this way: the host sends the terminal event
      // to attached connections alone, so a refused re-attach still gets that event and lets it decide.
      if (first) this.onFaulted({ code: attached.code, message: attached.message });
      return;
    }

    this.revision.set(attached.revision);
    // The tree itself arrives via a tree-updated notification pushed as a side effect of attaching -
    // attach is idempotent and always forces a fresh snapshot, which is exactly what a patch that
    // failed to apply needs.
  }

  private onTreeUpdated(event: UiSessionTreeUpdatedEvent): void {
    if (event.sessionId !== this.sessionId || this.closed) return;

    const root = rootNodeOf(event.tree);
    if (root === null) return;

    this.root.set(root);
    this.revision.set(event.revision);
    this.fault.set(null);
    this.hadTree = true;
    this.reopens = 0;
  }

  private onPatched(event: UiSessionPatchedEvent): void {
    if (event.sessionId !== this.sessionId || this.closed) return;

    const current = this.root();
    if (!current) return;

    const patch: UiPatch = {
      fromRevision: event.fromRevision,
      toRevision: event.toRevision,
      operations: (event.patch as { operations?: UiPatch['operations'] } | null)?.operations ?? [],
    };

    const next = applyUiPatch(current, this.revision(), patch);
    if (next === null) {
      void this.attach();
      return;
    }

    this.root.set(next);
    this.revision.set(event.toRevision);
  }

  private reopen(rejection: UiSessionRejection): void {
    if (this.closed) return;

    if (this.reopens >= MAX_REOPENS_WITHOUT_A_TREE) {
      this.onFaulted(rejection);
      return;
    }

    this.reopens++;
    this.sessionId = null;
    this.settleFlushes();
    this.reopenReason.set(rejection.code ?? null);
    if (this.nextRequest) this.request = untracked(this.nextRequest);
    void this.start();
  }

  private onFaulted(rejection: UiSessionRejection = {}): void {
    if (this.closed) return;

    const wasShowingTree = this.hadTree && this.root() !== null;
    this.settleFlushes();
    this.fault.set(rejection);
    this.root.set(null);
    if (!this.hadTree) this.rejection.set(rejection);

    if (wasShowingTree && this.request.kind === 'config' && this.notifyFault) {
      this.toast.show(this.localization.translateKey(AppStrings.ConfigUi.ViewUnavailable), { variant: 'error' });
    }
  }
}

