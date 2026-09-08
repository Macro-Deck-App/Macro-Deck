import { Component, EventEmitter, Input, OnInit, Output, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';

import { ClockData, Folder, GridWidget, WidgetData, WidgetType } from '@macro-deck/runtime';
import { IWidgetEditorComponent, ToastService, WidgetRegistryService } from '@shared';
import { ActionCutOriginService } from '../../../services/action-cut-origin.service';
import { FolderService, WidgetSchemaService } from '../../../services';
import { WidgetJsonEditorComponent } from '../../widgets';
import { WidgetEditorPageComponent } from './widget-editor-page.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';

@Component({ selector: 'app-test-widget-editor', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
 template: '' })
class TestWidgetEditorComponent implements IWidgetEditorComponent {
  @Input({ required: true }) widget!: GridWidget;
  @Input() unsavedChanges = false;
  @Output() save = new EventEmitter<Partial<WidgetData>>();
  @Output() close = new EventEmitter<void>();

  reloads = 0;

  reload(): void {
    this.reloads++;
  }
}

@Component({ selector: 'app-deferred-widget-editor', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
 template: '' })
class DeferredWidgetEditorComponent implements IWidgetEditorComponent {
  @Input({ required: true }) widget!: GridWidget;
  @Input() unsavedChanges = false;
  @Output() save = new EventEmitter<Partial<WidgetData>>();
  @Output() close = new EventEmitter<void>();

  private readonly readyState = signal(false);
  readonly ready = this.readyState.asReadonly();

  becomeReady(): void {
    this.readyState.set(true);
  }
}

@Component({ selector: 'app-normalizing-widget-editor', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
 template: '' })
class NormalizingWidgetEditorComponent implements IWidgetEditorComponent, OnInit {
  @Input({ required: true }) widget!: GridWidget;
  @Input() unsavedChanges = false;
  @Output() save = new EventEmitter<Partial<WidgetData>>();
  @Output() close = new EventEmitter<void>();

  ngOnInit(): void {
    this.seed();
  }

  reload(): void {
    this.seed();
  }

  private seed(): void {
    this.widget.data = { ...this.widget.data, style: 'digital', showSeconds: true } as ClockData;
  }
}

@Component({ selector: 'app-widget-json-editor', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
 template: '' })
class TestWidgetJsonEditorComponent {
  @Input({ required: true }) text!: string;
  @Input() schema: object | null = null;
  @Output() textChange = new EventEmitter<string>();
  @Output() validChange = new EventEmitter<boolean>();
  @Output() errorChange = new EventEmitter<string | null>();
}

const MODAL_EXIT_MS = 250;

const DIRTY_POLL_MS = 150;

function widget(overrides: Partial<GridWidget> = {}): GridWidget {
  return { id: 'w1', folderId: 'f1', x: 0, y: 0, w: 1, h: 1, type: WidgetType.Clock, data: {}, ...overrides };
}

function folder(overrides: Partial<Folder> = {}): Folder {
  return {
    id: 'f1', name: 'Folder', parentId: null, order: 0, isExpanded: true, isDefault: true,
    cols: 4, rows: 4, background: '', spacing: null, borderRadius: null, viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null, widgets: [widget()], ...overrides,
  };
}

describe('WidgetEditorPageComponent', () => {
  let folderStub: {
    folders: ReturnType<typeof signal<Folder[]>>;
    loadFolders: jasmine.Spy;
    selectFolder: jasmine.Spy;
    selectedFolderId: ReturnType<typeof signal<string | null>>;
    updateWidget: jasmine.Spy;
    findWidget: (widgetId: string) => GridWidget | undefined;
  };
  let registryStub: { getEditorComponent: jasmine.Spy; getWidgetTypeName: jasmine.Spy };
  let routerStub: { navigate: jasmine.Spy };
  let settleCutOrigin: jasmine.Spy;
  let widgetSchemaStub: { schemaFor: jasmine.Spy };

  function createFixture(widgetId = 'w1'): ComponentFixture<WidgetEditorPageComponent> {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: WidgetRegistryService, useValue: registryStub },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => widgetId } } } },
        { provide: Router, useValue: routerStub },
        { provide: ActionCutOriginService, useValue: { settle: settleCutOrigin } },
        { provide: WidgetSchemaService, useValue: widgetSchemaStub },
      ],
    });
    TestBed.overrideComponent(WidgetEditorPageComponent, {
      remove: { imports: [WidgetJsonEditorComponent] },
      add: { imports: [TestWidgetJsonEditorComponent] },
    });
    return TestBed.createComponent(WidgetEditorPageComponent);
  }

  async function createLoadedFixture(): Promise<ComponentFixture<WidgetEditorPageComponent>> {
    const fixture = createFixture();
    fixture.detectChanges();
    await fixture.whenStable();
    await fixture.whenStable();
    return fixture;
  }

  function editData(fixture: ComponentFixture<WidgetEditorPageComponent>): void {
    (fixture.componentInstance.widget()!.data as ClockData).showDate = true;
  }

  async function answer(fixture: ComponentFixture<WidgetEditorPageComponent>, label: string): Promise<void> {
    await fixture.whenStable();
    const buttons: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('shared-confirmation-modal [modal-footer] shared-button'));
    const button = buttons.find(b => b.textContent?.trim() === label);
    expect(button).withContext(`leave-confirmation button "${label}"`).toBeTruthy();
    button?.click();
    await new Promise<void>(resolve => setTimeout(resolve, MODAL_EXIT_MS));
    await fixture.whenStable();
  }

  beforeEach(() => {
    folderStub = {
      folders: signal<Folder[]>([folder()]),
      loadFolders: jasmine.createSpy('loadFolders').and.resolveTo(undefined),
      selectFolder: jasmine.createSpy('selectFolder'),
      selectedFolderId: signal<string | null>('f1'),
      updateWidget: jasmine.createSpy('updateWidget').and.resolveTo(true),
      findWidget: (widgetId: string) =>
        folderStub.folders().flatMap(f => f.widgets).find(w => w.id === widgetId),
    };
    registryStub = {
      getEditorComponent: jasmine.createSpy('getEditorComponent').and.returnValue(Promise.resolve(TestWidgetEditorComponent)),
      getWidgetTypeName: jasmine.createSpy('getWidgetTypeName').and.returnValue('Clock'),
    };
    routerStub = { navigate: jasmine.createSpy('navigate').and.resolveTo(true) };
    settleCutOrigin = jasmine.createSpy('settle').and.resolveTo(null);
    widgetSchemaStub = { schemaFor: jasmine.createSpy('schemaFor').and.resolveTo(null) };
  });

  it('shows a loading state while the editor chunk resolves, then renders it', async () => {
    let resolveEditor!: (value: typeof TestWidgetEditorComponent) => void;
    registryStub.getEditorComponent.and.returnValue(new Promise(resolve => { resolveEditor = resolve; }));

    const fixture = createFixture();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.editorLoading()).toBeTrue();
    expect(fixture.nativeElement.querySelector('shared-loading-state')).toBeTruthy();

    resolveEditor(TestWidgetEditorComponent);
    await fixture.whenStable();

    expect(fixture.componentInstance.editorLoading()).toBeFalse();
    expect(fixture.nativeElement.querySelector('app-test-widget-editor')).toBeTruthy();
  });

  it('keeps the loading state up until an editor that loads its own layout is ready', async () => {
    // The chunk resolving is only half the wait - a config editor then fetches its UI tree from the
    // host. Revealing it in between shows a bare preview that jumps into the real layout seconds
    // later (issue: widget editor layout flash).
    registryStub.getEditorComponent.and.resolveTo(DeferredWidgetEditorComponent);

    const fixture = await createLoadedFixture();

    expect(fixture.componentInstance.editorLoading()).toBeFalse();
    expect(fixture.componentInstance.showEditorLoading()).toBeTrue();
    expect(fixture.nativeElement.querySelector('shared-loading-state')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.editor-host--hidden')).toBeTruthy();

    const editor = fixture.debugElement
      .query(By.directive(DeferredWidgetEditorComponent)).componentInstance as DeferredWidgetEditorComponent;
    editor.becomeReady();
    await fixture.whenStable();

    expect(fixture.componentInstance.showEditorLoading()).toBeFalse();
    expect(fixture.nativeElement.querySelector('shared-loading-state')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('.editor-host--hidden')).toBeFalsy();
  });

  it('shows a reloadable error when the editor chunk fails to load', async () => {
    // A stale cached shell after an update references a lazy chunk the host no longer
    // serves, so the dynamic import rejects; the view must not stay on "Loading editor…".
    registryStub.getEditorComponent.and.returnValue(
      Promise.reject(new Error('Failed to fetch dynamically imported module')));

    const fixture = createFixture();
    fixture.detectChanges();
    await fixture.whenStable();
    await fixture.whenStable();

    expect(fixture.componentInstance.editorLoading()).toBeFalse();
    expect(fixture.componentInstance.editorError()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.editor-load-error')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-loading-state')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('app-test-widget-editor')).toBeFalsy();
  });

  it('does not create the editor a second time once loaded', async () => {
    const fixture = createFixture();
    fixture.detectChanges();
    await fixture.whenStable();
    await fixture.whenStable();

    expect(registryStub.getEditorComponent).toHaveBeenCalledTimes(1);
  });

  it('shows "not found" for an unknown widget id without loading an editor', async () => {
    const fixture = createFixture('missing');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.notFound()).toBeTrue();
    expect(registryStub.getEditorComponent).not.toHaveBeenCalled();
  });

  describe('the save button coming and going', () => {
    function fab(fixture: ComponentFixture<WidgetEditorPageComponent>): HTMLElement {
      return fixture.nativeElement.querySelector('.editor-save-fab') as HTMLElement;
    }

    function parked(fixture: ComponentFixture<WidgetEditorPageComponent>): boolean {
      return fab(fixture).classList.contains('editor-save-fab--hidden');
    }

    async function settle(fixture: ComponentFixture<WidgetEditorPageComponent>): Promise<void> {
      await fixture.whenStable();
      await fixture.whenStable();
    }

    it('stays parked while the editor holds nothing to save', async () => {
      const fixture = await createLoadedFixture();

      expect(parked(fixture)).toBeTrue();
    });

    it('slides in for an edit that nothing announced', async () => {
      // The mechanism must not depend on the edit landing during an interaction. Adding an action does
      // not: the picker is a modal, so its selection is applied after the exit animation, with no event
      // left to hang a check on. Same for a finishing icon import, and for async option loading.
      // The clock is installed before the page exists, so its timer is the mocked one.
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();
        editData(fixture);

        jasmine.clock().tick(DIRTY_POLL_MS);
        await settle(fixture);

        expect(fixture.componentInstance.dirty()).toBeTrue();
        expect(parked(fixture)).toBeFalse();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('stops comparing once the page is gone', async () => {
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();
        fixture.destroy();
        editData(fixture);

        jasmine.clock().tick(DIRTY_POLL_MS * 20);

        expect(fixture.componentInstance.dirty()).toBeFalse();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    // The button leaving is the confirmation. It used to stay on a "Saved" label for a moment first,
    // which put a second state between the click and the button going away.
    it('leaves as soon as the save lands, with no confirmation state in between', async () => {
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();
        editData(fixture);
        fixture.componentInstance.dirty.set(true);

        await fixture.componentInstance.onSave(new MouseEvent('click'));
        await fixture.whenStable();

        expect(fixture.componentInstance.dirty()).toBeFalse();
        expect(parked(fixture)).toBeTrue();
        expect(fixture.componentInstance.saveLabel()).toBe('Save');

        // Nothing is pending that could bring it back or relabel it once a timer would have run.
        jasmine.clock().tick(5000);
        await fixture.whenStable();

        expect(parked(fixture)).toBeTrue();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('shows a disabled button for an edit the invalid editor cannot save yet', async () => {
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();
        editData(fixture);
        fixture.componentInstance.editorValid.set(false);

        jasmine.clock().tick(DIRTY_POLL_MS);
        await settle(fixture);

        expect(parked(fixture)).toBeFalse();
        expect(fab(fixture).querySelector('button')?.disabled).toBeTrue();
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('saving', () => {
    it('saves and stays in the editor on a plain click', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      await fixture.componentInstance.onSave(new MouseEvent('click'));

      expect(folderStub.updateWidget).toHaveBeenCalledTimes(1);
      expect(routerStub.navigate).not.toHaveBeenCalled();
      expect(fixture.componentInstance.hasUnsavedChanges()).toBeFalse();
    });

    it('settles a pending action move against the flows it just saved', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      await fixture.componentInstance.onSave(new MouseEvent('click'));

      expect(settleCutOrigin)
        .toHaveBeenCalledOnceWith({ kind: 'widget', widgetId: 'w1' }, jasmine.any(Array));
    });

    it('saves and closes when Shift is held', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      // Save & Close starts the shared detail page's exit animation rather than navigating right
      // away - the actual navigation only happens once that animation reports it has finished.
      await fixture.componentInstance.onSave(new MouseEvent('click', { shiftKey: true }));
      fixture.nativeElement.querySelector('.dp-surface').dispatchEvent(new Event('animationend'));
      await fixture.whenStable();

      expect(folderStub.updateWidget).toHaveBeenCalledTimes(1);
      expect(routerStub.navigate).toHaveBeenCalledOnceWith(['/deck']);
    });

    it('stays in the editor when a Shift-click could not be saved', async () => {
      folderStub.updateWidget.and.resolveTo(false);
      const fixture = await createLoadedFixture();
      editData(fixture);

      await fixture.componentInstance.onSave(new MouseEvent('click', { shiftKey: true }));

      expect(routerStub.navigate).not.toHaveBeenCalled();
      expect(fixture.componentInstance.hasUnsavedChanges()).toBeTrue();
      expect(TestBed.inject(ToastService).toasts().map(t => t.variant)).toEqual(['error']);
    });

    it('does not save while the editor is invalid', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      fixture.componentInstance.editorValid.set(false);

      await fixture.componentInstance.onSave(new MouseEvent('click'));

      expect(folderStub.updateWidget).not.toHaveBeenCalled();
    });

    it('announces the pending close on the button label only while it is hovered', async () => {
      const fixture = await createLoadedFixture();

      fixture.componentInstance.onModifierChange(new KeyboardEvent('keydown', { shiftKey: true }));
      expect(fixture.componentInstance.saveLabel()).toBe('Save');

      fixture.nativeElement.querySelector('.editor-save-fab').dispatchEvent(new MouseEvent('mouseenter'));
      expect(fixture.componentInstance.saveLabel()).toBe('Save & Close');

      fixture.componentInstance.onWindowBlur();
      expect(fixture.componentInstance.saveLabel()).toBe('Save');
    });
  });

  describe('leaving with unsaved changes', () => {
    it('leaves without asking when nothing was edited', async () => {
      const fixture = await createLoadedFixture();

      expect(fixture.componentInstance.confirmNavigation()).toBeTrue();
      expect(fixture.componentInstance.confirmingLeave()).toBeFalse();
    });

    it('does not count an editor normalising the data it was handed as an edit', async () => {
      registryStub.getEditorComponent.and.returnValue(Promise.resolve(NormalizingWidgetEditorComponent));
      const fixture = await createLoadedFixture();

      expect(fixture.componentInstance.hasUnsavedChanges()).toBeFalse();
      expect(fixture.componentInstance.confirmNavigation()).toBeTrue();
    });

    it('asks, and stays put when the answer is to keep editing', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      await fixture.whenStable();

      expect(fixture.componentInstance.confirmingLeave()).toBeTrue();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      await answer(fixture, 'Keep editing');

      await expectAsync(leaving).toBeResolvedTo(false);
      expect(fixture.componentInstance.confirmingLeave()).toBeFalse();
      expect(folderStub.updateWidget).not.toHaveBeenCalled();
    });

    it('leaves and drops the edits when the answer is to discard', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      await answer(fixture, 'Discard changes');

      await expectAsync(leaving).toBeResolvedTo(true);
      expect(folderStub.updateWidget).not.toHaveBeenCalled();
    });

    it('saves first when the answer is to save, then leaves', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      await answer(fixture, 'Save & Close');

      expect(folderStub.updateWidget).toHaveBeenCalledTimes(1);
      await expectAsync(leaving).toBeResolvedTo(true);
    });

    it('keeps the page when saving before leaving failed', async () => {
      folderStub.updateWidget.and.resolveTo(false);
      const fixture = await createLoadedFixture();
      editData(fixture);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      await answer(fixture, 'Save & Close');

      await expectAsync(leaving).toBeResolvedTo(false);
      expect(fixture.componentInstance.hasUnsavedChanges()).toBeTrue();
    });

    it('refuses a second navigation while the question is still on screen', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      expect(fixture.componentInstance.confirmNavigation()).toBeFalse();

      await answer(fixture, 'Discard changes');
      await expectAsync(leaving).toBeResolvedTo(true);
    });

    it('does not offer to save while the editor is invalid', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      fixture.componentInstance.editorValid.set(false);

      const leaving = fixture.componentInstance.confirmNavigation() as Promise<boolean>;
      await fixture.whenStable();

      const modal = fixture.nativeElement.querySelector('shared-confirmation-modal');
      expect(modal?.textContent).not.toContain('Save & Close');
      expect(modal?.textContent).toContain('Discard changes');

      await answer(fixture, 'Keep editing');
      await expectAsync(leaving).toBeResolvedTo(false);
    });
  });
  describe('following the widget while it is open', () => {
    function push(data: Partial<ClockData>): void {
      folderStub.folders.update(folders => folders.map(f => ({
        ...f,
        widgets: f.widgets.map(w => w.id === 'w1' ? { ...w, data: { ...w.data, ...data } } : w),
      })));
    }

    function editorDouble(fixture: ComponentFixture<WidgetEditorPageComponent>): TestWidgetEditorComponent {
      return fixture.debugElement.query(
        el => el.componentInstance instanceof TestWidgetEditorComponent).componentInstance;
    }

    it('adopts a change while the draft is clean', async () => {
      const fixture = await createLoadedFixture();

      push({ showDate: true });
      await fixture.whenStable();

      expect((fixture.componentInstance.widget()!.data as ClockData).showDate).toBeTrue();
      expect(editorDouble(fixture).reloads).toBe(1);
    });

    it('adopts an appearance-only change while the draft is clean', async () => {
      const fixture = await createLoadedFixture();

      push({ label: 'restyled by a flow' } as Partial<ClockData>);
      await fixture.whenStable();

      expect((fixture.componentInstance.widget()!.data as { label?: string }).label).toBe('restyled by a flow');
    });

    it('stays clean after adopting, so the save button does not appear on its own', async () => {
      const fixture = await createLoadedFixture();

      push({ showDate: true });
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));

      expect(fixture.componentInstance.hasUnsavedChanges()).toBeFalse();
      expect(fixture.componentInstance.saveVisible()).toBeFalse();
    });

    it('leaves an unsaved edit the change did not touch alone', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));

      push({ showSeconds: false });
      await fixture.whenStable();

      expect((fixture.componentInstance.widget()!.data as ClockData).showDate).toBeTrue();
    });

    it('merges the change into a dirty draft instead of dropping it', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));

      push({ showSeconds: false });
      await fixture.whenStable();

      const data = fixture.componentInstance.widget()!.data as ClockData;
      expect(data.showSeconds).toBeFalse();
      expect(data.showDate).toBeTrue();
      expect(fixture.componentInstance.hasUnsavedChanges()).toBeTrue();
    });

    it('keeps both changes after a second run', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));

      push({ showSeconds: false });
      await fixture.whenStable();
      push({ style: 'analog' } as Partial<ClockData>);
      await fixture.whenStable();

      const data = fixture.componentInstance.widget()!.data as ClockData;
      expect(data.showSeconds).toBeFalse();
      expect(data.style).toBe('analog');
      expect(data.showDate).toBeTrue();
    });

    it('tells the editor about unsaved changes', async () => {
      const fixture = await createLoadedFixture();
      expect(editorDouble(fixture).unsavedChanges).toBeFalse();

      editData(fixture);
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));
      fixture.detectChanges();

      expect(editorDouble(fixture).unsavedChanges).toBeTrue();
    });

    it('does not re-adopt the same data over and over', async () => {
      const fixture = await createLoadedFixture();

      push({ showDate: true });
      await fixture.whenStable();
      push({ showDate: true });
      await fixture.whenStable();

      expect(editorDouble(fixture).reloads).toBe(1);
    });

    it('ignores the echo of its own save', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture);
      await new Promise(resolve => setTimeout(resolve, DIRTY_POLL_MS * 2));
      await fixture.componentInstance.onSave();
      await fixture.whenStable();

      const reloadsAfterSave = editorDouble(fixture).reloads;
      push({ showDate: true });
      await fixture.whenStable();

      expect(editorDouble(fixture).reloads).toBe(reloadsAfterSave);
    });

    it('shows the pushed values when the editor is reopened', async () => {
      const first = await createLoadedFixture();
      push({ showDate: true });
      await first.whenStable();
      first.destroy();

      const reopened = TestBed.createComponent(WidgetEditorPageComponent);
      reopened.detectChanges();
      await reopened.whenStable();

      expect((reopened.componentInstance.widget()!.data as ClockData).showDate).toBeTrue();
    });
  });

  describe('JSON editing mode', () => {
    function modeButton(fixture: ComponentFixture<WidgetEditorPageComponent>, label: 'Visual' | 'JSON'): HTMLButtonElement | undefined {
      const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
      return buttons.find(b => b.textContent?.trim() === label);
    }

    async function switchMode(fixture: ComponentFixture<WidgetEditorPageComponent>, label: 'Visual' | 'JSON'): Promise<void> {
      modeButton(fixture, label)?.click();
      await fixture.whenStable();
    }

    function editorDouble(fixture: ComponentFixture<WidgetEditorPageComponent>): TestWidgetEditorComponent {
      return fixture.debugElement.query(
        el => el.componentInstance instanceof TestWidgetEditorComponent).componentInstance;
    }

    function jsonEditorDouble(fixture: ComponentFixture<WidgetEditorPageComponent>): TestWidgetJsonEditorComponent | null {
      const debugEl = fixture.debugElement.query(el => el.componentInstance instanceof TestWidgetJsonEditorComponent);
      return debugEl ? debugEl.componentInstance : null;
    }

    it('defaults to Visual, offering exactly Visual and JSON, with the JSON editor not in the DOM', async () => {
      const fixture = await createLoadedFixture();

      expect(fixture.componentInstance.mode()).toBe('visual');
      const visual = modeButton(fixture, 'Visual');
      const json = modeButton(fixture, 'JSON');
      expect(visual).withContext('Visual button').toBeTruthy();
      expect(json).withContext('JSON button').toBeTruthy();
      expect(visual?.getAttribute('aria-pressed')).toBe('true');
      expect(json?.getAttribute('aria-pressed')).toBe('false');
      expect(fixture.nativeElement.querySelector('app-widget-json-editor')).toBeFalsy();
    });

    it('shows the live draft, re-serialized on every switch into JSON mode', async () => {
      const fixture = await createLoadedFixture();
      editData(fixture); // showDate = true

      await switchMode(fixture, 'JSON');
      expect(JSON.parse(fixture.componentInstance.jsonText()).showDate).toBeTrue();

      jsonEditorDouble(fixture)?.validChange.emit(false);
      jsonEditorDouble(fixture)?.errorChange.emit('Unexpected token');
      expect(fixture.componentInstance.jsonValid()).toBeFalse();
      expect(fixture.componentInstance.jsonError()).toBe('Unexpected token');
      jsonEditorDouble(fixture)?.validChange.emit(true);
      jsonEditorDouble(fixture)?.errorChange.emit(null);

      await switchMode(fixture, 'Visual');
      (fixture.componentInstance.widget()!.data as ClockData).showSeconds = false;
      await switchMode(fixture, 'JSON');

      const parsed = JSON.parse(fixture.componentInstance.jsonText());
      expect(parsed.showDate).withContext('first change').toBeTrue();
      expect(parsed.showSeconds).withContext('second change').toBeFalse();
    });

    it('applies valid JSON to the model and the visual editor when leaving JSON mode', async () => {
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();
        await switchMode(fixture, 'JSON');
        const reloadsBefore = editorDouble(fixture).reloads;

        fixture.componentInstance.jsonText.set(JSON.stringify({ showDate: true, showSeconds: false }));
        await switchMode(fixture, 'Visual');

        const data = fixture.componentInstance.widget()!.data as ClockData;
        expect(data.showDate).toBeTrue();
        expect(data.showSeconds).toBeFalse();
        expect(editorDouble(fixture).reloads).toBeGreaterThan(reloadsBefore);

        jasmine.clock().tick(DIRTY_POLL_MS);
        await fixture.whenStable();
        expect(fixture.componentInstance.dirty()).toBeTrue();

        await fixture.componentInstance.onSave(new MouseEvent('click'));

        expect(folderStub.updateWidget).toHaveBeenCalledOnceWith('w1', {
          data: jasmine.objectContaining({ showDate: true, showSeconds: false }),
        });
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('never reaches the host saving with an unparseable JSON buffer, called directly as Enter or Save & Close would', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');
      const before: WidgetData = { ...fixture.componentInstance.widget()!.data };

      fixture.componentInstance.jsonText.set('{ this is not JSON');
      // `jsonValid()` still reads its stale default (true) here - nothing drove the 300 ms lint
      // pass in this test - which is exactly the race `onSave` must not trust blindly.
      await fixture.componentInstance.onSave();

      expect(folderStub.updateWidget).not.toHaveBeenCalled();
      expect(fixture.componentInstance.widget()!.data).toEqual(before);
    });

    it('asks before leaving JSON mode with text that parses but violates the schema', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');
      const before: WidgetData = { ...fixture.componentInstance.widget()!.data };
      const reloadsBefore = editorDouble(fixture).reloads;

      fixture.componentInstance.jsonText.set('{ "style": "digital", "style": 123 }');
      jsonEditorDouble(fixture)?.validChange.emit(false);
      await fixture.whenStable();

      await switchMode(fixture, 'Visual');

      expect(fixture.componentInstance.mode()).toBe('json');
      expect(fixture.componentInstance.widget()!.data).toEqual(before);
      expect(editorDouble(fixture).reloads).toBe(reloadsBefore);
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();
    });

    it('never overwrites the model with an invalid buffer, and asks before leaving JSON mode', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');
      const before: WidgetData = { ...fixture.componentInstance.widget()!.data };
      const reloadsBefore = editorDouble(fixture).reloads;
      const invalidText = '{ still not JSON';
      fixture.componentInstance.jsonText.set(invalidText);

      await switchMode(fixture, 'Visual');

      expect(fixture.componentInstance.widget()!.data).toEqual(before);
      expect(editorDouble(fixture).reloads).toBe(reloadsBefore);
      expect(fixture.componentInstance.mode()).toBe('json');
      const modal = fixture.nativeElement.querySelector('shared-confirmation-modal');
      expect(modal?.textContent).toContain('Invalid JSON');

      await answer(fixture, 'Keep editing JSON');
      expect(fixture.componentInstance.mode()).toBe('json');
      expect(fixture.componentInstance.jsonText()).toBe(invalidText);

      await switchMode(fixture, 'Visual');
      await answer(fixture, 'Discard JSON changes');

      expect(fixture.componentInstance.mode()).toBe('visual');
      expect(fixture.componentInstance.widget()!.data).toEqual(before);
    });

    it('counts a dirty JSON buffer as an unsaved change for the navigation guard', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');
      fixture.componentInstance.jsonText.set(JSON.stringify({ showDate: true }));

      const leaving = fixture.componentInstance.confirmNavigation();

      expect(leaving).not.toBe(true);
      expect(typeof (leaving as Promise<boolean>).then).toBe('function');
      await fixture.whenStable();
      expect(fixture.componentInstance.confirmingLeave()).toBeTrue();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      await answer(fixture, 'Discard changes');
      await expectAsync(leaving as Promise<boolean>).toBeResolvedTo(true);
    });

    it('canSave() is false while the JSON buffer is invalid and true once fixed', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');

      fixture.componentInstance.jsonValid.set(false);
      expect(fixture.componentInstance.canSave()).toBeFalse();

      fixture.componentInstance.jsonValid.set(true);
      expect(fixture.componentInstance.canSave()).toBeTrue();
    });

    it('shows no confirmation switching back to Visual with an unchanged JSON buffer', async () => {
      const fixture = await createLoadedFixture();
      await switchMode(fixture, 'JSON');

      await switchMode(fixture, 'Visual');

      expect(fixture.componentInstance.mode()).toBe('visual');
      expect(fixture.componentInstance.confirmingJsonDiscard()).toBeFalse();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeFalsy();
    });

    it('leaves dirty() false for a round trip into JSON mode with no edits', async () => {
      jasmine.clock().install();
      try {
        const fixture = await createLoadedFixture();

        await switchMode(fixture, 'JSON');
        await switchMode(fixture, 'Visual');
        jasmine.clock().tick(DIRTY_POLL_MS);
        await fixture.whenStable();

        expect(fixture.componentInstance.dirty()).toBeFalse();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('lets JSON mode save past an editor the visual side calls incomplete, but not Visual mode itself', async () => {
      const fixture = await createLoadedFixture();
      fixture.componentInstance.editorValid.set(false);
      editData(fixture);

      await fixture.componentInstance.onSave(new MouseEvent('click'));
      expect(folderStub.updateWidget).not.toHaveBeenCalled();

      await switchMode(fixture, 'JSON');
      fixture.componentInstance.jsonText.set(JSON.stringify({ showDate: true }));

      await fixture.componentInstance.onSave(new MouseEvent('click'));
      expect(folderStub.updateWidget).toHaveBeenCalledTimes(1);
    });

    describe('a host push while the JSON buffer is the active pane', () => {
      function push(data: Partial<ClockData>): void {
        folderStub.folders.update(folders => folders.map(f => ({
          ...f,
          widgets: f.widgets.map(w => w.id === 'w1' ? { ...w, data: { ...w.data, ...data } } : w),
        })));
      }

      it('leaves a dirty buffer untouched, but still updates the model', async () => {
        const fixture = await createLoadedFixture();
        await switchMode(fixture, 'JSON');
        const dirtyText = JSON.stringify({ showDate: true });
        fixture.componentInstance.jsonText.set(dirtyText);

        push({ showSeconds: false });
        await fixture.whenStable();

        expect(fixture.componentInstance.jsonText()).toBe(dirtyText);
        expect((fixture.componentInstance.widget()!.data as ClockData).showSeconds).toBeFalse();
      });

      it('re-serializes a clean buffer to follow the host', async () => {
        const fixture = await createLoadedFixture();
        await switchMode(fixture, 'JSON');

        push({ showSeconds: false });
        await fixture.whenStable();

        const parsed = JSON.parse(fixture.componentInstance.jsonText());
        expect(parsed.showSeconds).toBeFalse();
        expect(fixture.componentInstance.hasUnsavedChanges()).toBeFalse();
      });
    });
  });
});
