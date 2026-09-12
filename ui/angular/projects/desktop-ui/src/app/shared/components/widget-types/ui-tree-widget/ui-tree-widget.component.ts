import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChange,
  SimpleChanges,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';

import {
  ActionButtonTriggerType,
  activationFor,
  emitsEvent,
  findInteractiveNode,
  treeClaimsGesture,
  UiNode,
  UiNodeEvent,
  type UiComponentBox,
  UiComponentEvents,
  WidgetData,
} from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '../../../transport';
import { UiSessionHandle, UiSessionOpenRequest, UiSessionService } from '../../../services/ui-session.service';
import { PressFeedback } from '../../../util/press-feedback';
import { UiNodePressedEvent } from '../../ui-render/ui-node-event-bus';
import { UiWidgetTreeComponent } from '../../ui-render/ui-widget-tree.component';
import { UiWidgetTreeContext } from '../../ui-render/ui-widget-tree-context';

// A preview rebuilds by reopening its session, so this is how long a keystroke in the editor's label
// field takes to show up. Low enough that typing does not feel disconnected from the preview, high
// enough that a burst of keystrokes is still one reopen rather than one each.
const PREVIEW_REOPEN_DEBOUNCE_MS = 120;

@Component({
  selector: 'shared-ui-tree-widget',
  standalone: true,
  imports: [UiWidgetTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiWidgetTreeContext],
  templateUrl: './ui-tree-widget.component.html',
  styleUrls: ['./ui-tree-widget.component.scss'],
})
export class UiTreeWidgetComponent implements OnInit, OnChanges, OnDestroy {
  @Input() data: WidgetData = {} as WidgetData;
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
  @Input() widgetId?: string;
  @Input() widgetType?: string;
  @Input() ghost = false;
  @Input() sample = false;
  @Input() variableScopeWidgetId?: string;

  /**
   * Set by a surface that draws the ring of a root `ui.button` itself, beside this tree rather than
   * inside it (issue #895) - the deck tile, the drag ghost and the editor preview all do, and read
   * the value to draw from {@link treeRoot} via `widgetTileBorder`. Left unset, the button paints its
   * own ring as before.
   */
  @Input() tileDrawsBorder = false;

  @Output() trigger = new EventEmitter<ActionButtonTriggerType>();
  @Output() pressedChange = new EventEmitter<boolean>();

  private readonly LONG_PRESS_MS = 600;
  protected isPressed = false;
  protected readonly pressFeedback = new PressFeedback(pressed => this.pressedChange.emit(pressed));
  private longPressTriggered = false;
  private longPressTimeout: ReturnType<typeof setTimeout> | null = null;

  private readonly treeContext = inject(UiWidgetTreeContext);
  private readonly uiSessions = inject(UiSessionService);
  private readonly api = inject(ApiService);

  protected readonly treeBox = signal<UiComponentBox>({ width: null, height: null });

  private readonly handleSignal = signal<UiSessionHandle | null>(null);
  protected readonly renderedRoot = signal<UiNode | null>(null);

  /** The tree as rendered, for a surface that has to read a host-resolved value off its root. */
  readonly treeRoot = this.renderedRoot.asReadonly();

  readonly rejected = computed(() => this.handleSignal()?.rejection() != null);

  protected readonly treeClaimsGesture = computed(() => treeClaimsGesture(this.renderedRoot()));

  private openedForWidgetId: string | undefined;
  private previewDebounceTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectBaseline: ConnectionState | null = null;

  constructor() {
    effect(() => {
      const root = this.handleSignal()?.root() ?? null;
      if (root !== null) this.renderedRoot.set(root);
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

  ngOnInit(): void {
    this.treeContext.setTileDrawsRootBorder(this.tileDrawsBorder);
    this.openSession();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['tileDrawsBorder']) {
      this.treeContext.setTileDrawsRootBorder(this.tileDrawsBorder);
    }

    if (changes['width'] || changes['height']) {
      this.treeContext.setBasis(Math.min(this.width, this.height));
      this.treeBox.set({ width: this.width || null, height: this.height || null });
    }

    if (isRealChange(changes['widgetId']) && this.widgetId !== this.openedForWidgetId) {
      this.openSession();
      return;
    }

    if (!this.widgetId &&
      (isRealChange(changes['data']) || isRealChange(changes['widgetType']) || isRealChange(changes['sample']) ||
        isRealChange(changes['variableScopeWidgetId']))) {
      this.schedulePreviewReopen();
    }
  }

  ngOnDestroy(): void {
    this.clearPreviewDebounce();
    this.closeSessionHandle();
    this.clearLongPressTimeout();
    this.pressFeedback.dispose();
  }

  protected onTreeEvent(event: UiNodeEvent): void {
    this.handleSignal()?.send(event);
  }

  protected readonly treeNodePressed = signal(false);

  protected onNodePressedChange(event: UiNodePressedEvent): void {
    if (event.pressed && event.nodeId !== this.renderedRoot()?.id) return;
    this.treeNodePressed.set(event.pressed);
    this.pressedChange.emit(event.pressed);
  }

  private openSession(): void {
    this.clearPreviewDebounce();
    this.closeSessionHandle();
    this.openedForWidgetId = this.widgetId;

    const request = this.buildOpenRequest();
    if (!request) return;
    this.handleSignal.set(this.uiSessions.open(request));
  }

  private buildOpenRequest(): UiSessionOpenRequest | null {
    if (this.widgetId) {
      return { kind: 'widget', widgetId: this.widgetId, ghost: this.ghost };
    }
    if (this.widgetType) {
      const preview: UiSessionOpenRequest =
        { kind: 'widget', widgetType: this.widgetType, data: this.data, sample: this.sample };
      if (this.variableScopeWidgetId) preview.variableScopeWidgetId = this.variableScopeWidgetId;
      return preview;
    }
    return null;
  }

  private closeSessionHandle(): void {
    this.handleSignal()?.close();
    this.handleSignal.set(null);
  }

  private schedulePreviewReopen(): void {
    this.clearPreviewDebounce();
    this.previewDebounceTimer = setTimeout(() => {
      this.previewDebounceTimer = null;
      this.openSession();
    }, PREVIEW_REOPEN_DEBOUNCE_MS);
  }

  private clearPreviewDebounce(): void {
    if (this.previewDebounceTimer) {
      clearTimeout(this.previewDebounceTimer);
      this.previewDebounceTimer = null;
    }
  }

  activateFromInput(): void {
    if (this.disabled) return;

    // The same 60ms floor a tap gets. On a device driven by physical controls this flash is the only
    // confirmation the press landed, so it is painted for both paths below.
    this.pressFeedback.press();
    this.pressFeedback.release();

    const interactive = findInteractiveNode(this.renderedRoot());
    if (interactive !== null) {
      const activation = activationFor(interactive);
      if (activation !== null) {
        this.onTreeEvent({ nodeId: interactive.id, name: activation.name, data: activation.payload });
        return;
      }
      for (const name of [UiComponentEvents.PressStart, UiComponentEvents.PressEnd, UiComponentEvents.Press]) {
        // The same gate UiNodeEventBus.emit applies: a node never raises an event it did not declare.
        if (emitsEvent(interactive, name)) {
          this.onTreeEvent({ nodeId: interactive.id, name });
        }
      }
      return;
    }

    this.trigger.emit('onTouchStart');
    this.trigger.emit('onTouchEnd');
    this.trigger.emit('onShortPress');
  }

  protected onPressStart(event: PointerEvent): void {
    if (this.disabled || this.treeClaimsGesture()) return;
    event.preventDefault();
    event.stopPropagation();
    this.clearLongPressTimeout();
    this.longPressTriggered = false;
    this.setPressed(true);
    this.trigger.emit('onTouchStart');
    this.longPressTimeout = setTimeout(() => {
      if (this.isPressed) {
        this.longPressTriggered = true;
        this.trigger.emit('onLongPress');
      }
    }, this.LONG_PRESS_MS);
  }

  protected onPressEnd(event: PointerEvent): void {
    // Gated on whether *this* press started here (isPressed, set only when onPressStart's own
    // treeClaimsGesture() check passed at press-start time), never on the *current* claim - the tree can
    // finish loading and start claiming the gesture between a press starting and ending. Gating on the
    // live value the way onPressStart does would then strand isPressed on true forever: no onTouchEnd
    // ever fires, and the long-press timeout (still armed) fires a spurious onLongPress at 600ms.
    if (this.disabled || !this.isPressed) return;
    event.preventDefault();
    event.stopPropagation();
    this.clearLongPressTimeout();
    this.setPressed(false);
    this.trigger.emit('onTouchEnd');
    if (!this.longPressTriggered) {
      this.trigger.emit('onShortPress');
    }
  }

  protected onPressCancel(): void {
    if (this.isPressed) {
      this.trigger.emit('onTouchEnd');
    }
    this.clearLongPressTimeout();
    this.setPressed(false);
  }

  private setPressed(pressed: boolean): void {
    if (this.isPressed === pressed) return;
    this.isPressed = pressed;
    if (pressed) {
      this.pressFeedback.press();
    } else {
      this.pressFeedback.release();
    }
  }

  private clearLongPressTimeout(): void {
    if (this.longPressTimeout) {
      clearTimeout(this.longPressTimeout);
      this.longPressTimeout = null;
    }
  }
}

function isRealChange(change: SimpleChange | undefined): boolean {
  return !!change && !change.firstChange;
}
