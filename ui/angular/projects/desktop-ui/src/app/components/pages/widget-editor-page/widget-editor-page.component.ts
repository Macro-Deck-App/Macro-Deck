import {
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  DestroyRef,
  HostListener,
  Injector,
  OnDestroy,
  OnInit,
  Type,
  ViewChild,
  ViewContainerRef,
  computed,
  effect,
  inject,
  runInInjectionContext,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { ActionFlow, AppStrings, GridWidget, WidgetData, fromEditorJson, toEditorJson } from '@macro-deck/runtime';
import { ButtonComponent, IWidgetEditorComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, ToastService, TranslatePipe, WidgetRegistryService } from '@shared';
import { CursorPositionComponent } from '../../cursor-position/cursor-position.component';
import { DetailPageComponent } from '../../detail-page/detail-page.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { ActionCutOriginService } from '../../../services/action-cut-origin.service';
import { deepEqual } from '../../../util/deep-equal';
import { applyWidgetDataPatch, diffWidgetData } from '../../../util/widget-data-patch';
import { ConfirmsNavigation } from '../../../guards';
import { FolderService, WidgetSchemaService } from '../../../services';
import { WidgetJsonEditorComponent } from '../../widgets';

const DIRTY_POLL_MS = 150;

@Component({
  selector: 'app-widget-editor-page',
  standalone: true,
  imports: [
    ButtonComponent,
    ConfirmationModalComponent,
    CursorPositionComponent,
    DetailPageComponent,
    LoadingStateComponent,
    SegmentedControlComponent,
    TranslatePipe,
    WidgetJsonEditorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-editor-page.component.html',
  styleUrls: ['./widget-editor-page.component.scss'],
})
export class WidgetEditorPageComponent implements OnInit, OnDestroy, ConfirmsNavigation {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly folderService = inject(FolderService);
  private readonly registry = inject(WidgetRegistryService);
  private readonly toasts = inject(ToastService);
  private readonly cutOrigin = inject(ActionCutOriginService);
  private readonly injector = inject(Injector);
  private readonly widgetSchemas = inject(WidgetSchemaService);
  private readonly localization = inject(LocalizationService);

  readonly widget = signal<GridWidget | null>(null);
  private readonly widgetId = signal<string | null>(null);

  readonly mode = signal<'visual' | 'json'>('visual');
  readonly modeOptions = computed<SegmentedOption[]>(() => [
    { value: 'visual', label: this.localization.translateKey(AppStrings.WidgetEditor.Page.ModeVisual) },
    { value: 'json', label: this.localization.translateKey(AppStrings.WidgetEditor.Page.ModeJson) },
  ]);
  readonly jsonText = signal('');
  readonly jsonValid = signal(true);
  readonly jsonError = signal<string | null>(null);
  readonly jsonSchema = signal<object | null>(null);
  readonly jsonPaneMounted = signal(false);
  readonly confirmingJsonDiscard = signal(false);
  private jsonBaseline = '';

  private readonly liveWidget = computed<GridWidget | null>(() => {
    const id = this.widgetId();
    return id ? this.folderService.findWidget(id) ?? null : null;
  });
  readonly notFound = signal(false);
  readonly editorValid = signal(true);
  readonly canSave = computed(() => this.mode() === 'json' ? this.jsonValid() : this.editorValid());
  readonly saving = signal(false);
  readonly confirmingLeave = signal(false);
  readonly dirty = signal(false);

  readonly editorLoading = signal(false);
  readonly editorError = signal(false);
  private readonly editorReady = signal(false);

  // The lazy chunk is only the first half of the wait: a config editor then has to fetch its UI tree
  // from the host, which is the part that can take seconds. One loading state spans both, so the
  // editor is revealed once, fully laid out, instead of flashing a bare preview first.
  readonly showEditorLoading = computed(() => !this.editorError() && !this.editorReady());

  private readonly shiftHeld = signal(false);
  private readonly saveHovered = signal(false);

  private readonly saveClosesEditor = computed(() => this.shiftHeld() && this.saveHovered());

  readonly saveLabel = computed(() => {
    return this.saveClosesEditor()
      ? this.localization.translateKey(AppStrings.WidgetEditor.Page.SaveAndCloseLabel)
      : this.localization.translateKey(AppStrings.WidgetEditor.Page.SaveLabel);
  });

  readonly saveVisible = computed(() => this.dirty());

  readonly leaveMessage = computed(() =>
    this.canSave()
      ? this.localization.translateKey(AppStrings.WidgetEditor.Page.UnsavedChangesMessage)
      : this.localization.translateKey(AppStrings.WidgetEditor.Page.UnsavedChangesIncompleteMessage));

  private editorRef: ComponentRef<IWidgetEditorComponent> | null = null;
  private editorHostRef: ViewContainerRef | null = null;
  private destroyed = false;
  private savedData: WidgetData | null = null;
  private lastLiveData: WidgetData | null = null;
  private resolveLeave: ((leave: boolean) => void) | null = null;

  @ViewChild('editorHost', { read: ViewContainerRef })
  set editorHost(host: ViewContainerRef | undefined) {
    this.editorHostRef = host ?? null;
    void this.loadEditorComponent();
  }

  @ViewChild(DetailPageComponent)
  private detailPage?: DetailPageComponent;

  constructor() {
    this.watchForEdits();
  }

  private watchForEdits(): void {
    const timer = setInterval(() => this.dirty.set(this.hasUnsavedChanges()), DIRTY_POLL_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  get widgetTypeName(): string {
    const widget = this.widget();
    return widget ? this.registry.getWidgetTypeName(widget.type) : '';
  }

  async ngOnInit(): Promise<void> {
    const widgetId = this.route.snapshot.paramMap.get('widgetId');
    if (!widgetId) {
      this.notFound.set(true);
      return;
    }

    if (this.folderService.folders().length === 0) {
      await this.folderService.loadFolders();
    }

    const folder = this.folderService.folders()
      .find(f => f.widgets.some(w => w.id === widgetId));
    const widget = folder?.widgets.find(w => w.id === widgetId);

    if (!folder || !widget) {
      this.notFound.set(true);
      return;
    }

    if (!widget.isPinned || this.folderService.selectedFolderId() === null) {
      this.folderService.selectFolder(folder.id);
    }
    this.widgetId.set(widgetId);
    this.lastLiveData = structuredClone(widget.data);
    this.widget.set({ ...widget, data: { ...widget.data } });
    this.followLiveWidget();
  }

  private followLiveWidget(): void {
    runInInjectionContext(this.injector, () => {
      effect(() => {
        const live = this.liveWidget();
        untracked(() => this.onLiveWidgetChanged(live));
      });

      effect(() => {
        const dirty = this.dirty();
        untracked(() => this.editorRef?.setInput('unsavedChanges', dirty));
      });
    });
  }

  private onLiveWidgetChanged(live: GridWidget | null): void {
    if (!live || !this.editorRef) return;
    if (this.lastLiveData && deepEqual(this.lastLiveData, live.data)) return;

    const patch = diffWidgetData(this.lastLiveData, live.data);
    this.lastLiveData = structuredClone(live.data);

    const dirty = this.hasUnsavedChanges();
    if (dirty && !patch) return;

    this.adopt(live, dirty ? applyWidgetDataPatch(this.widget()!.data, patch) : structuredClone(live.data), dirty);
  }

  private adopt(live: GridWidget, data: WidgetData, keepDirty: boolean): void {
    const next = { ...live, data };
    this.seedEditor(next);

    if (this.mode() === 'json' && !keepDirty) {
      this.jsonText.set(toEditorJson(next.type, next.data));
    }

    if (!keepDirty) this.markSaved();
  }

  private seedEditor(next: GridWidget): void {
    this.widget.set(next);
    this.editorRef?.setInput('widget', next);
    this.editorRef?.instance.reload?.();
    this.editorRef?.changeDetectorRef.detectChanges();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.editorHostRef?.clear();
    this.editorRef = null;
    this.settleLeave(true);
  }

  @HostListener('window:keydown', ['$event'])
  @HostListener('window:keyup', ['$event'])
  onModifierChange(event: KeyboardEvent): void {
    this.shiftHeld.set(event.shiftKey);
  }

  @HostListener('window:blur')
  onWindowBlur(): void {
    this.shiftHeld.set(false);
  }

  protected onSaveButtonEnter(): void {
    this.saveHovered.set(true);
  }

  protected onSaveButtonLeave(): void {
    this.saveHovered.set(false);
  }

  private async loadEditorComponent(): Promise<void> {
    const widget = this.widget();
    if (!this.editorHostRef || !widget || this.editorRef || this.editorLoading()) return;

    const loadEditor = this.registry.getEditorComponent(widget.type);
    if (!loadEditor) return;

    this.editorLoading.set(true);
    this.editorError.set(false);
    let editorType: Type<IWidgetEditorComponent>;
    try {
      editorType = await loadEditor;
    } catch {
      if (!this.destroyed) {
        this.editorError.set(true);
        this.editorLoading.set(false);
      }
      return;
    }

    if (this.destroyed || !this.editorHostRef) {
      this.editorLoading.set(false);
      return;
    }

    this.editorHostRef.clear();
    this.editorRef = this.editorHostRef.createComponent(editorType);
    this.editorRef.setInput('widget', widget);
    this.editorRef.changeDetectorRef.detectChanges();
    this.editorLoading.set(false);
    this.markSaved();

    const ready = this.editorRef.instance.ready;
    if (ready) {
      runInInjectionContext(this.injector, () => {
        effect(() => this.editorReady.set(ready()));
      });
    } else {
      this.editorReady.set(true);
    }

    this.onLiveWidgetChanged(this.liveWidget());

    const valid = this.editorRef.instance.valid;
    if (valid) {
      runInInjectionContext(this.injector, () => {
        effect(() => this.editorValid.set(valid()));
      });
    } else {
      this.editorValid.set(true);
    }
  }

  async onSave(event?: MouseEvent): Promise<void> {
    const close = event?.shiftKey === true;
    const saved = await this.save();
    if (saved && close) {
      this.requestClose();
    }
  }

  hasUnsavedChanges(): boolean {
    const data = this.widget()?.data;
    const modelDirty = !!data && !!this.savedData && !deepEqual(this.savedData, data);
    const jsonDirty = this.mode() === 'json' && this.jsonText() !== this.jsonBaseline;
    return modelDirty || jsonDirty;
  }

  confirmNavigation(): Promise<boolean> | boolean {
    if (!this.hasUnsavedChanges()) return true;
    if (this.resolveLeave) return false;

    this.confirmingLeave.set(true);
    return new Promise<boolean>(resolve => { this.resolveLeave = resolve; });
  }

  protected onDiscardChanges(): void {
    this.settleLeave(true);
  }

  protected onKeepEditing(): void {
    this.settleLeave(false);
  }

  protected async onSaveAndLeave(): Promise<void> {
    // A save that did not reach the host must not take the changes with it.
    this.settleLeave(await this.save());
  }

  protected requestClose(): void {
    this.detailPage?.requestClose();
  }

  async goBack(): Promise<void> {
    const navigated = await this.router.navigate(['/deck']);
    if (!navigated) {
      this.detailPage?.cancelClose();
    }
  }

  reloadApp(): void {
    window.location.reload();
  }

  private async save(): Promise<boolean> {
    let widget = this.widget();
    if (!widget || !this.canSave() || this.saving()) return false;
    // `jsonValid()` lags behind the buffer by the 300 ms lint delay, so it cannot be trusted alone
    // here - a synchronous re-check of the buffer's own parseability is what makes "invalid JSON can
    // never overwrite a valid widget configuration" true by construction rather than by timing.
    if (this.mode() === 'json' && !this.applyJson()) return false;
    widget = this.widget()!;

    this.saving.set(true);
    const saved = await this.folderService.updateWidget(widget.id, { data: { ...widget.data } });
    this.saving.set(false);

    if (!saved) {
      this.toasts.show(this.localization.translateKey(AppStrings.WidgetEditor.Page.SaveFailedToast), { variant: 'error' });
      return false;
    }

    this.markSaved();
    await this.settleCutSource(widget);
    return true;
  }

  private async settleCutSource(widget: GridWidget): Promise<void> {
    const flows = (widget.data as { flows?: ActionFlow[] }).flows ?? [];
    const settled = await this.cutOrigin.settle({ kind: 'widget', widgetId: widget.id }, flows);
    if (settled && !settled.success) {
      this.toasts.show(this.localization.translateKey(AppStrings.WidgetEditor.Page.CutMoveFailedToast), {
        variant: 'error',
      });
    }
  }

  private markSaved(): void {
    const data = this.widget()?.data;
    this.savedData = data ? structuredClone(data) : null;
    this.lastLiveData = data ? structuredClone(data) : null;
    // Re-baselines to the user's literal current text, not a re-serialization - a save must not
    // reformat the document out from under the cursor.
    this.jsonBaseline = this.jsonText();
    this.dirty.set(false);
  }

  private applyJson(): boolean {
    const widget = this.widget();
    if (!widget) return false;

    let data: WidgetData;
    try {
      data = fromEditorJson(widget.type, this.jsonText());
    } catch {
      return false;
    }

    this.seedEditor({ ...widget, data });
    return true;
  }

  protected onModeChange(next: string): void {
    if (next === this.mode()) return;
    if (next === 'json') {
      this.enterJsonMode();
    } else if (next === 'visual') {
      this.leaveJsonMode();
    }
  }

  private enterJsonMode(): void {
    const widget = this.widget();
    if (!widget) return;

    const text = toEditorJson(widget.type, widget.data);
    this.jsonText.set(text);
    this.jsonBaseline = text;
    this.jsonPaneMounted.set(true);
    this.mode.set('json');

    const { type } = widget;
    void this.widgetSchemas.schemaFor(type).then(schema => {
      if (!this.destroyed && this.widget()?.type === type) {
        this.jsonSchema.set(schema);
      }
    });
  }

  private leaveJsonMode(): void {
    if (this.jsonText() === this.jsonBaseline) {
      this.mode.set('visual');
      return;
    }
    // A schema violation counts as invalid here, not just unparseable text: `{"style": 123}` parses
    // (and so does a duplicate key, which JSON allows), but applying it would put a value the widget
    // cannot render on the model - and Visual mode, whose canSave follows the editor rather than the
    // schema, would then let it be saved. The issue is explicit that the JSON is deserialized only
    // once validation succeeds.
    if (this.jsonValid() && this.applyJson()) {
      this.mode.set('visual');
      return;
    }
    this.confirmingJsonDiscard.set(true);
  }

  protected onDiscardJsonChanges(): void {
    this.confirmingJsonDiscard.set(false);
    this.mode.set('visual');
  }

  protected onKeepEditingJson(): void {
    this.confirmingJsonDiscard.set(false);
  }

  private settleLeave(leave: boolean): void {
    this.confirmingLeave.set(false);
    const resolve = this.resolveLeave;
    this.resolveLeave = null;
    resolve?.(leave);
  }
}
