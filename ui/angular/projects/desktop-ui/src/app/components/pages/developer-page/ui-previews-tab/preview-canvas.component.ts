import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import {
  UiNode,
  UiNodeEvent,
  UiPreviewEntry,
  UiComponentBox,
  UI_COMPONENT_CELL,
  isComponentProfileType,
} from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ConnectionState, TranslatePipe, UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService, UiWidgetTreeComponent, UiWidgetTreeContext } from '@shared';
import { UiTreeComponent } from '../../../ui-render/ui-tree.component';

export interface CanvasSize {
  width: number;
  height: number;
}

export type CanvasState = 'live' | 'waiting' | 'gone' | 'closed' | 'failed';

interface SizePreset {
  width: number;
  height: number;
}

type HandleAxis = 'right' | 'bottom' | 'corner';

const MIN_WIDTH = 40;
const MIN_HEIGHT = 40;
const KEYBOARD_STEP = 8;

const CELL_PX = 150;
const REFERENCE_SCALE = CELL_PX / UI_COMPONENT_CELL;
const SIZE_EMIT_DELAY_MS = 250;
const RETRY_DELAYS_MS: readonly number[] = [2000, 4000, 8000, 16000, 30000];

const PROVIDER_AWAY_CODES: ReadonlySet<string> = new Set([
  'PROVIDER_DISCONNECTED',
  'PROVIDER_UNAVAILABLE',
  'PROVIDER_TIMEOUT',
]);

const DEFAULT_SIZE_BY_PROFILE: Readonly<Record<string, CanvasSize>> = {
  widget: { width: CELL_PX, height: CELL_PX },
  config: { width: 3 * CELL_PX, height: 2 * CELL_PX },
};

const PRESET_SPANS: readonly (readonly [columns: number, rows: number])[] = [
  [1, 1],
  [2, 1],
  [1, 2],
  [2, 2],
  [3, 2],
  [4, 2],
];

const SIZE_PRESETS: readonly SizePreset[] = PRESET_SPANS.map(([columns, rows]) => ({
  width: columns * CELL_PX,
  height: rows * CELL_PX,
}));

@Component({
  selector: 'app-preview-canvas',
  standalone: true,
  imports: [ButtonComponent, TranslatePipe, UiTreeComponent, UiWidgetTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiWidgetTreeContext],
  templateUrl: './preview-canvas.component.html',
  styleUrls: ['./preview-canvas.component.scss'],
})
export class PreviewCanvasComponent implements OnDestroy {
  readonly preview = input<UiPreviewEntry | null>(null);
  readonly available = input(true);
  readonly ownerConnected = input(true);
  readonly catalogRevision = input(0);
  readonly initialSize = input<CanvasSize | null>(null);
  readonly sizeChange = output<CanvasSize>();

  protected readonly presets = SIZE_PRESETS;

  private readonly uiSessions = inject(UiSessionService);
  private readonly api = inject(ApiService);
  private readonly treeContext = inject(UiWidgetTreeContext);

  private readonly handleSignal = signal<UiSessionHandle | null>(null);
  private readonly liveRoot = computed<UiNode | null>(() => this.handleSignal()?.root() ?? null);
  private readonly lastRoot = signal<UiNode | null>(null);
  protected readonly root = computed<UiNode | null>(() => this.liveRoot() ?? this.lastRoot());

  private readonly fault = computed<UiSessionRejection | null>(() => this.handleSignal()?.fault?.() ?? null);
  protected readonly rejection = computed<UiSessionRejection | null>(() => this.handleSignal()?.rejection() ?? null);
  private readonly problem = computed<UiSessionRejection | null>(() => this.fault() ?? this.rejection());

  protected readonly state = computed<CanvasState>(() => {
    if (!this.available()) return this.ownerConnected() ? 'gone' : 'waiting';
    const problem = this.problem();
    if (!problem) return 'live';
    if (problem.code !== undefined && PROVIDER_AWAY_CODES.has(problem.code)) return 'waiting';
    return this.fault() !== null && problem.code === undefined ? 'closed' : 'failed';
  });

  protected readonly stale = computed(() => this.state() !== 'live' || this.liveRoot() === null);

  protected readonly size = signal<CanvasSize>(DEFAULT_SIZE_BY_PROFILE['config']);
  protected readonly box = computed<UiComponentBox>(() => ({ width: this.size().width, height: this.size().height }));
  protected readonly referenceScale = REFERENCE_SCALE;
  protected readonly referenceBox = computed<UiComponentBox>(() => ({
    width: this.size().width / REFERENCE_SCALE,
    height: this.size().height / REFERENCE_SCALE,
  }));

  protected readonly rendererKind = computed<'widget' | 'config' | null>(() => {
    const root = this.root();
    if (!root) return null;
    return isComponentProfileType(root.type) ? 'widget' : 'config';
  });

  private currentPreviewId: string | null = null;
  private sizeFollowsProfile = false;
  private initialSizeUsed = false;
  private seenCatalogRevision: number | null = null;
  private sizeEmitTimer: ReturnType<typeof setTimeout> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryAttempt = 0;
  private reconnectBaseline: ConnectionState | null = null;
  private dragAxis: HandleAxis | null = null;
  private dragStart: CanvasSize = { width: 0, height: 0 };
  private dragOrigin = { x: 0, y: 0 };
  private dragPointerId: number | null = null;

  constructor() {
    effect(() => {
      const box = this.rendererKind() === 'widget' ? this.referenceBox() : this.box();
      this.treeContext.setBasis(Math.min(box.width ?? 0, box.height ?? 0));
    });

    effect(() => {
      const root = this.liveRoot();
      if (root) this.lastRoot.set(root);
    });

    effect(() => {
      const preview = this.preview();
      untracked(() => this.onPreviewChanged(preview));
    });

    effect(() => {
      const available = this.available();
      untracked(() => {
        if (!available) this.closeSessionHandle();
      });
    });

    effect(() => {
      const revision = this.catalogRevision();
      untracked(() => this.onCatalogReloaded(revision));
    });

    effect(() => {
      const waitingForProvider = this.available() && this.state() === 'waiting';
      const showingLiveTree = this.state() === 'live' && this.liveRoot() !== null;
      untracked(() => this.updateRetry(waitingForProvider, showingLiveTree));
    });

    effect(() => {
      const size = this.size();
      untracked(() => this.scheduleSizeEmit(size));
    });

    effect(() => {
      const state = this.api.connectionStateSignal();
      if (this.reconnectBaseline === null) {
        this.reconnectBaseline = state;
        return;
      }
      if (state === 'connected' && this.reconnectBaseline !== 'connected' && untracked(this.available)) {
        this.openSession();
      }
      this.reconnectBaseline = state;
    });
  }

  ngOnDestroy(): void {
    if (this.sizeEmitTimer !== null) clearTimeout(this.sizeEmitTimer);
    this.clearRetry();
    this.closeSessionHandle();
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    this.handleSignal()?.send(event);
  }

  protected refresh(): void {
    if (this.available()) this.openSession();
  }

  protected selectPreset(preset: SizePreset): void {
    this.applySize(preset.width, preset.height);
  }

  protected onHandlePointerDown(event: PointerEvent, axis: HandleAxis): void {
    event.preventDefault();
    const target = event.currentTarget as HTMLElement;
    target.setPointerCapture(event.pointerId);
    this.dragAxis = axis;
    this.dragPointerId = event.pointerId;
    this.dragStart = { ...this.size() };
    this.dragOrigin = { x: event.clientX, y: event.clientY };
  }

  protected onHandlePointerMove(event: PointerEvent): void {
    if (this.dragAxis === null || event.pointerId !== this.dragPointerId) return;

    const dx = event.clientX - this.dragOrigin.x;
    const dy = event.clientY - this.dragOrigin.y;
    const width = this.dragAxis === 'bottom' ? this.dragStart.width : this.dragStart.width + dx;
    const height = this.dragAxis === 'right' ? this.dragStart.height : this.dragStart.height + dy;
    this.applySize(width, height);
  }

  protected onHandlePointerUp(event: PointerEvent): void {
    if (event.pointerId !== this.dragPointerId) return;
    const target = event.currentTarget as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) target.releasePointerCapture(event.pointerId);
    this.dragAxis = null;
    this.dragPointerId = null;
  }

  protected onHandleKeyDown(event: KeyboardEvent, axis: HandleAxis): void {
    const current = this.size();
    let width = current.width;
    let height = current.height;

    switch (event.key) {
      case 'ArrowRight':
        if (axis !== 'bottom') width += KEYBOARD_STEP;
        break;
      case 'ArrowLeft':
        if (axis !== 'bottom') width -= KEYBOARD_STEP;
        break;
      case 'ArrowDown':
        if (axis !== 'right') height += KEYBOARD_STEP;
        break;
      case 'ArrowUp':
        if (axis !== 'right') height -= KEYBOARD_STEP;
        break;
      default:
        return;
    }

    event.preventDefault();
    this.applySize(width, height);
  }

  private applySize(width: number, height: number): void {
    const next: CanvasSize = {
      width: Math.max(MIN_WIDTH, Math.round(width)),
      height: Math.max(MIN_HEIGHT, Math.round(height)),
    };
    const current = this.size();
    if (current.width === next.width && current.height === next.height) return;
    this.sizeFollowsProfile = false;
    this.size.set(next);
  }

  private onPreviewChanged(preview: UiPreviewEntry | null): void {
    const id = preview?.id ?? null;

    if (id === this.currentPreviewId) {
      if (preview && this.sizeFollowsProfile && preview.profile) this.size.set(this.defaultSizeFor(preview));
      return;
    }

    this.currentPreviewId = id;
    this.closeSessionHandle();
    this.lastRoot.set(null);
    if (!preview) return;

    const initial = this.initialSizeUsed ? null : this.initialSize();
    this.initialSizeUsed = true;
    this.size.set(initial ?? this.defaultSizeFor(preview));
    this.sizeFollowsProfile = initial === null && !preview.profile;

    if (this.available()) this.openSession();
  }

  private onCatalogReloaded(revision: number): void {
    const first = this.seenCatalogRevision === null;
    const changed = this.seenCatalogRevision !== revision;
    this.seenCatalogRevision = revision;
    if (first || !changed || !this.available() || !this.preview()) return;

    // Only a session the provider lost, or never had, is reopened: one another window closed by
    // refreshing the same preview must not be taken back, or two windows would keep stealing it.
    const handle = this.handleSignal();
    const code = this.problem()?.code;
    if (handle === null || code?.startsWith('PROVIDER_')) this.openSession();
  }

  private updateRetry(waitingForProvider: boolean, showingLiveTree: boolean): void {
    if (showingLiveTree) this.retryAttempt = 0;
    if (!waitingForProvider) {
      this.clearRetry();
      return;
    }
    if (this.retryTimer !== null) return;

    const delay = RETRY_DELAYS_MS[Math.min(this.retryAttempt, RETRY_DELAYS_MS.length - 1)];
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.retryAttempt++;
      if (this.available() && this.state() === 'waiting') this.openSession();
    }, delay);
  }

  private clearRetry(): void {
    if (this.retryTimer !== null) clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  private defaultSizeFor(preview: UiPreviewEntry): CanvasSize {
    return DEFAULT_SIZE_BY_PROFILE[preview.profile] ?? DEFAULT_SIZE_BY_PROFILE['config'];
  }

  private scheduleSizeEmit(size: CanvasSize): void {
    if (this.sizeEmitTimer !== null) clearTimeout(this.sizeEmitTimer);
    this.sizeEmitTimer = setTimeout(() => {
      this.sizeEmitTimer = null;
      this.sizeChange.emit(size);
    }, SIZE_EMIT_DELAY_MS);
  }

  private openSession(): void {
    this.closeSessionHandle();

    const preview = this.preview();
    if (!preview) return;

    const request: UiSessionOpenRequest = { kind: 'preview', previewId: preview.id };
    this.handleSignal.set(this.uiSessions.open(request));
  }

  private closeSessionHandle(): void {
    this.handleSignal()?.close();
    this.handleSignal.set(null);
  }
}
