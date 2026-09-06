import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import {
  UiNode,
  UiNodeEvent,
  UiPreviewEntry,
  UiComponentBox,
  isComponentProfileType,
} from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ConnectionState, TranslatePipe, UiSessionHandle, UiSessionOpenRequest, UiSessionService, UiWidgetTreeComponent, UiWidgetTreeContext } from '@shared';
import { UiTreeComponent } from '../../../ui-render/ui-tree.component';

interface CanvasSize {
  width: number;
  height: number;
}

interface SizePreset {
  width: number;
  height: number;
}

type HandleAxis = 'right' | 'bottom' | 'corner';

const MIN_WIDTH = 40;
const MIN_HEIGHT = 40;
const KEYBOARD_STEP = 8;

const CELL_PX = 150;

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

  protected readonly presets = SIZE_PRESETS;

  private readonly uiSessions = inject(UiSessionService);
  private readonly api = inject(ApiService);
  private readonly treeContext = inject(UiWidgetTreeContext);

  private readonly handleSignal = signal<UiSessionHandle | null>(null);
  protected readonly root = computed<UiNode | null>(() => this.handleSignal()?.root() ?? null);
  protected readonly rejection = computed(() => this.handleSignal()?.rejection() ?? null);

  protected readonly size = signal<CanvasSize>(DEFAULT_SIZE_BY_PROFILE['config']);
  protected readonly box = computed<UiComponentBox>(() => ({ width: this.size().width, height: this.size().height }));

  protected readonly rendererKind = computed<'widget' | 'config' | null>(() => {
    const root = this.root();
    if (!root) return null;
    return isComponentProfileType(root.type) ? 'widget' : 'config';
  });

  private openedForPreviewId: string | null = null;
  private reconnectBaseline: ConnectionState | null = null;
  private dragAxis: HandleAxis | null = null;
  private dragStart: CanvasSize = { width: 0, height: 0 };
  private dragOrigin = { x: 0, y: 0 };
  private dragPointerId: number | null = null;

  constructor() {
    effect(() => {
      const basis = Math.min(this.size().width, this.size().height);
      this.treeContext.setBasis(basis);
    });

    effect(() => {
      const preview = this.preview();
      if (preview?.id !== this.openedForPreviewId) this.openSession();
    });

    effect(() => {
      const state = this.api.connectionStateSignal();
      if (this.reconnectBaseline === null) {
        this.reconnectBaseline = state;
        return;
      }
      if (state === 'connected' && this.reconnectBaseline !== 'connected') {
        this.openSession();
      }
      this.reconnectBaseline = state;
    });
  }

  ngOnDestroy(): void {
    this.closeSessionHandle();
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    this.handleSignal()?.send(event);
  }

  protected refresh(): void {
    this.openSession();
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
    this.size.set(next);
  }

  private openSession(): void {
    this.closeSessionHandle();

    const preview = this.preview();
    // Only a different scenario gets the profile's default size. A refresh recreates the scenario at
    // whatever size the developer is testing at - resetting it would throw away what they were looking for.
    const isDifferentPreview = preview?.id !== this.openedForPreviewId;
    this.openedForPreviewId = preview?.id ?? null;
    if (!preview) return;

    if (isDifferentPreview) {
      this.size.set(DEFAULT_SIZE_BY_PROFILE[preview.profile] ?? DEFAULT_SIZE_BY_PROFILE['config']);
    }

    const request: UiSessionOpenRequest = { kind: 'preview', previewId: preview.id };
    this.handleSignal.set(this.uiSessions.open(request));
  }

  private closeSessionHandle(): void {
    this.handleSignal()?.close();
    this.handleSignal.set(null);
  }
}
