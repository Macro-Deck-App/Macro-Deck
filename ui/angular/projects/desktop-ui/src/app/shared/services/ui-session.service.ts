import { Injectable, Signal, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { ApiService } from '../transport';
import {
  applyUiPatch,
  AppStrings,
  type OpenConfigUiSessionRequest,
  type OpenFolderUiSessionRequest,
  type OpenModalUiSessionRequest,
  type OpenUiPreviewSessionRequest,
  type OpenWidgetUiSessionRequest,
  rootNodeOf,
  supportsUiModelVersion,
  UiNode,
  UiNodeEvent,
  UiPatch,
  type UiSessionPatchedEvent,
  type UiSessionTreeUpdatedEvent,
} from '@macro-deck/runtime';
import { LocalizationService } from '../localization';
import { ToastService } from './toast.service';

export type UiSessionOpenRequest =
  | ({ kind: 'config' } & OpenConfigUiSessionRequest & { configUiModelVersion: number })
  | ({ kind: 'widget' } & OpenWidgetUiSessionRequest)
  | ({ kind: 'folder' } & OpenFolderUiSessionRequest)
  | ({ kind: 'modal' } & OpenModalUiSessionRequest)
  | ({ kind: 'preview' } & OpenUiPreviewSessionRequest);

export interface UiSessionHandle {
  readonly root: Signal<UiNode | null>;
  readonly revision: Signal<number>;
  readonly rejection: Signal<UiSessionRejection | null>;
  send(event: UiNodeEvent): void;
  close(): void;
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

  open(request: UiSessionOpenRequest): UiSessionHandle {
    if (request.kind === 'config' && isNegotiableVersion(request.configUiModelVersion)
      && !supportsUiModelVersion(request.configUiModelVersion)) {
      // Attaching and then falling back would burn one of the provider's few session slots and
      // briefly show an unrenderable tree - negotiation has to happen before OpenConfigUiSession is
      // ever called.
      return new NullUiSessionHandle();
    }

    return new LiveUiSessionHandle(this.api, this.toast, this.localization, request);
  }
}

class NullUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);

  send(): void {
    // No session was ever opened; nothing to send to.
  }

  close(): void {
    // No session was ever opened; nothing to close.
  }
}

const MAX_REOPENS_WITHOUT_A_TREE = 3;

class LiveUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);

  private sessionId: string | null = null;
  private closed = false;
  // Whether a closed/invalidated notification should surface to the user: only once a tree was
  // actually shown, and never for our own deliberate close().
  private hadTree = false;
  private readonly subscription = new Subscription();
  private reopens = 0;

  constructor(
    private readonly api: ApiService,
    private readonly toast: ToastService,
    private readonly localization: LocalizationService,
    private readonly request: UiSessionOpenRequest,
  ) {
    // Subscribed before the first realtime request goes out, so no notification for this session can arrive
    // and be missed while sessionId is still unknown.
    this.subscription.add(this.api.onUiSessionTreeUpdated().subscribe(event => this.onTreeUpdated(event)));
    this.subscription.add(this.api.onUiSessionPatched().subscribe(event => this.onPatched(event)));
    this.subscription.add(this.api.onUiSessionInvalidated().subscribe(event => {
      if (event.sessionId !== this.sessionId) return;
      if (event.retryable) this.reopen(); else this.onFaulted();
    }));
    this.subscription.add(this.api.onUiSessionClosed().subscribe(event => {
      if (event.sessionId === this.sessionId) this.onFaulted();
    }));

    void this.start();
  }

  send(event: UiNodeEvent): void {
    if (this.closed || !this.sessionId) return;
    void this.api.sendUiEvent({
      sessionId: this.sessionId,
      nodeId: event.nodeId,
      name: event.name,
      data: event.data,
      revision: this.revision(),
    });
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.subscription.unsubscribe();
    this.root.set(null);
    if (this.sessionId) {
      void this.api.closeUiSession(this.sessionId);
    }
  }

  private async start(): Promise<void> {
    const opened = await this.openSession();
    if (this.closed) return;

    if (!opened?.accepted) {
      this.rejection.set({ code: opened?.code, message: opened?.message });
      return;
    }

    this.rejection.set(null);
    this.sessionId = opened.sessionId;
    await this.attach();
  }

  private openSession(): Promise<{ accepted: boolean; sessionId: string; code?: string; message?: string } | null> {
    switch (this.request.kind) {
      case 'config': return this.api.openConfigUiSession(this.request);
      case 'widget': return this.api.openWidgetUiSession(this.request);
      case 'folder': return this.api.openFolderUiSession(this.request);
      case 'modal': return this.api.openModalUiSession(this.request);
      case 'preview': return this.api.openUiPreviewSession(this.request);
    }
  }

  private async attach(): Promise<void> {
    if (this.closed || !this.sessionId) return;

    const attached = await this.api.attachUiSession(this.sessionId);
    if (this.closed || !attached?.accepted) return;

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

  private reopen(): void {
    if (this.closed) return;

    if (this.reopens >= MAX_REOPENS_WITHOUT_A_TREE) {
      this.onFaulted();
      return;
    }

    this.reopens++;
    this.sessionId = null;
    void this.start();
  }

  private onFaulted(): void {
    if (this.closed) return;

    const wasShowingTree = this.hadTree && this.root() !== null;
    this.root.set(null);

    if (wasShowingTree && this.request.kind === 'config') {
      this.toast.show(this.localization.translateKey(AppStrings.ConfigUi.ViewUnavailable), { variant: 'error' });
    }
  }
}

