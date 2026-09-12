import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  computed,
  effect,
  forwardRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import {
  applyConfigDraftEvent,
  composeConfigDraft,
  GridWidget,
  UiConfigEntryPoints,
  UiConfigEvents,
  UiConfigPrimitives,
  UiNode,
  UiNodeEvent,
  WidgetBorder,
  WidgetData,
  widgetTileBorder,
} from '@macro-deck/runtime';
import {
  IWidgetEditorComponent,
  TranslatePipe,
  UiNodeEventBus,
  UiSessionHandle,
  UiSessionService,
  UiTreeWidgetComponent,
  WidgetBorderOverlayComponent,
  WidgetTypeCatalogService,
} from '@shared';
import { UiNodeComponent } from '../../../ui-render/ui-node.component';
import { UiRenderContext } from '../../../ui-render/ui-render-context';
import { WidgetEditorShellComponent } from '../common/widget-editor-shell/widget-editor-shell.component';

const DRAFT_EVENT_NAMES: ReadonlySet<string> = new Set([
  UiConfigEvents.Change,
  UiConfigEvents.Add,
  UiConfigEvents.Remove,
]);

@Component({
  selector: 'app-widget-configuration-editor',
  standalone: true,
  imports: [
    NgTemplateOutlet,
    TranslatePipe,
    // Deferred like `UiNodeComponent`'s own mutual references to `UiChromeComponent`/`UiInputComponent`
    // (see the file comment there): this is a new consumer of the ui-render module graph, so
    // dereferencing the class at this file's own load time risks the same TDZ cycle.
    forwardRef(() => UiNodeComponent),
    UiTreeWidgetComponent,
    WidgetBorderOverlayComponent,
    WidgetEditorShellComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiNodeEventBus, UiRenderContext],
  templateUrl: './widget-configuration-editor.component.html',
  styleUrls: ['../common/widget-editor-sidebar.scss', './widget-configuration-editor.component.scss'],
})
export class WidgetConfigurationEditorComponent implements IWidgetEditorComponent, OnInit, OnDestroy {
  @Input({ required: true }) widget!: GridWidget;

  private _unsavedChanges = false;
  @Input()
  set unsavedChanges(value: boolean) {
    this._unsavedChanges = value;
    this.context.setUnsavedChanges(value);
  }
  get unsavedChanges(): boolean {
    return this._unsavedChanges;
  }

  @Output() save = new EventEmitter<Partial<WidgetData>>();
  @Output() close = new EventEmitter<void>();

  private readonly uiSessions = inject(UiSessionService);
  private readonly widgetTypes = inject(WidgetTypeCatalogService);
  protected readonly context = inject(UiRenderContext);
  private readonly eventBus = inject(UiNodeEventBus);

  private readonly handle = signal<UiSessionHandle | null>(null);

  protected readonly root = computed<UiNode | null>(() => this.handle()?.root() ?? null);
  protected readonly rejection = computed(() => this.handle()?.rejection() ?? null);

  private readonly noConfiguration = signal(false);

  /** Until the host answers with a tree there is only the preview to paint, and the single-pane
   * fallback below would show it alone before the real split layout replaced it. */
  readonly ready = computed(() => this.root() !== null || this.rejection() !== null || this.noConfiguration());

  protected readonly propertiesRegion = computed<UiNode | null>(
    () => findRegion(this.root(), UiConfigPrimitives.WidgetProperties));
  protected readonly editorRegion = computed<UiNode | null>(
    () => findRegion(this.root(), UiConfigPrimitives.WidgetEditor));

  protected readonly previewData = signal<WidgetData>({} as WidgetData);

  private readonly previewTree = viewChild(UiTreeWidgetComponent);

  /** The same rule the deck's tile applies (`widgetTileBorder`): an Action Button's ring comes from
   * its `ui.button` root node, host-resolved per active state, every other type's from the draft. */
  protected readonly previewBorder = computed<WidgetBorder | undefined>(
    () => widgetTileBorder(this.widget.type, this.previewData(), this.previewTree()?.treeRoot() ?? null));

  private seenTree = false;

  /** The draft the current session was opened with, so `reload` can tell a real change from a reseed
   * that left the configuration exactly as the tree already has it. */
  private openedWith: string | null = null;

  constructor() {
    effect(() => this.context.setRoot(this.root()));

    // A provider writes some keys itself rather than in response to a value edit - applying a preset,
    // adopting a state provider. Those arrive as patches to the tree's values and never as `change`,
    // so folding only events would leave the draft holding the old values while the form shows the new
    // ones, and the save would quietly write the stale set. Reconciling on each revision covers every
    // such write without the provider having to announce them.
    //
    // The first tree is deliberately not reconciled. It carries the provider's own defaults for keys the
    // stored data never had, and folding those in would write them into the draft before the user has
    // touched anything - which the page's dirty check reads as an edit, so the editor would open with an
    // enabled Save button and saving would add keys nobody asked for.
    effect(() => {
      const root = this.root();
      if (!root) return;

      if (!this.seenTree) {
        this.seenTree = true;
        return;
      }

      this.applyDraft(composeConfigDraft(root, this.widget.data as unknown as Record<string, unknown>));
    });

    const subscription = this.eventBus.events$.subscribe(event => this.onNodeEvent(event));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  ngOnInit(): void {
    this.previewData.set({ ...this.widget.data });
    this.context.setScopeRefId(this.widget.id);
    void this.openSession();
  }

  ngOnDestroy(): void {
    this.handle()?.close();
  }

  /**
   * Rebuilds the tree from the draft the page has just put on `widget`. The host composes the tree from
   * the data the session was opened with, so a draft that arrived from outside the tree - a JSON-mode
   * edit, a live update adopted while editing - is invisible to it until the session is opened again.
   * Called by the editor page whenever it reseeds this editor.
   */
  reload(): void {
    // The page reseeds this editor on every live update it adopts too, most of which change nothing the
    // tree is built from. Reopening then would throw away tree-local state the draft does not carry -
    // which tab is open, which state is selected - for no gain, so an unchanged draft is left alone.
    if (JSON.stringify(this.widget.data) === this.openedWith) return;

    this.handle()?.close();
    this.handle.set(null);
    // The next tree is a first tree again: it carries the provider's defaults for keys the draft never
    // had, and the effect below must not fold those into the draft - see its own remarks.
    this.seenTree = false;
    this.previewData.set({ ...this.widget.data });
    void this.openSession();
  }

  private async openSession(): Promise<void> {
    // Read before the await, not after it: `reload` compares against this to decide whether to reopen,
    // and a draft captured after an awaited catalogue lookup could already be a later one.
    const widgetData = JSON.stringify(this.widget.data);
    this.openedWith = widgetData;

    const info = await this.widgetTypes.infoFor(this.widget.type);
    if (info && !info.supportsConfigUi) {
      this.noConfiguration.set(true);
      return;
    }

    this.handle.set(this.uiSessions.open({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.WidgetConfig,
      widgetId: this.widget.id,
      // The draft, not the stored record: this editor owns the transaction, and the tree has to render
      // what is being edited rather than what the widget was last saved with.
      widgetData,
      // The host catalogue is what WidgetRegistryService already consulted to mount this editor at
      // all; falling back to 0 ("whatever the host speaks") only matters for the narrow race where
      // that answer is not cached yet.
      configUiModelVersion: info?.configUiModelVersion ?? 0,
    }));
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    this.handle()?.send(event);
    if (!DRAFT_EVENT_NAMES.has(event.name)) return;

    const root = this.root();
    if (!root) return;

    const data = this.widget.data as unknown as Record<string, unknown>;
    this.applyDraft(applyConfigDraftEvent(root, data, event));
  }

  private applyDraft(next: Record<string, unknown>): void {
    const data = this.widget.data as unknown as Record<string, unknown>;

    for (const key of Object.keys(data)) {
      if (!(key in next)) delete data[key];
    }

    Object.assign(data, next);
    this.previewData.set({ ...data });
  }
}

function findRegion(root: UiNode | null, type: string): UiNode | null {
  return root?.children?.find(child => child.type === type) ?? null;
}
