import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { GridWidget, WidgetType, computeCellDimensions, iconSizeBucket, parseWidgetData } from '@macro-deck/runtime';
import { IconPrefetchService, ProfileService, WidgetClipboardService } from '@shared';
import { IconPackService } from '../../../services/icon-pack.service';
import { PortabilityService } from '../../../services/portability.service';
import { WidgetSelectionService } from '../../../services/widget-selection.service';
import { FileOpenService, FolderService } from '../../../services';
import { DeckPageComponent } from './deck-page.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function widget(overrides: Partial<GridWidget> = {}): GridWidget {
  return { id: 'src-widget', folderId: 'f1', x: 1, y: 1, w: 2, h: 2, type: WidgetType.ActionButton, data: { label: 'x' }, ...overrides };
}

function momentaryButtonWidget(overrides: Partial<GridWidget> & { iconId: string }): GridWidget {
  const { iconId, ...rest } = overrides;
  const data = parseWidgetData(WidgetType.ActionButton, JSON.stringify({ mode: 'momentary', iconId }));
  return widget({ data, ...rest });
}

describe('DeckPageComponent copy/cut/paste', () => {
  let folderStub: {
    selectedFolderId: jasmine.Spy;
    folders: jasmine.Spy;
    pasteWidget: jasmine.Spy;
    removeWidget: jasmine.Spy;
    isCurrentProfileLocked?: unknown;
  };
  let clipboard: WidgetClipboardService;

  function createComponent(): DeckPageComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'p1' } },
        { provide: PortabilityService, useValue: {} },
        { provide: IconPackService, useValue: {} },
        { provide: IconPrefetchService, useValue: { prefetch: jasmine.createSpy('prefetch') } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
      ],
    });
    clipboard = TestBed.inject(WidgetClipboardService);
    return TestBed.createComponent(DeckPageComponent).componentInstance;
  }

  beforeEach(() => {
    folderStub = {
      selectedFolderId: jasmine.createSpy('selectedFolderId').and.returnValue('current-folder'),
      folders: jasmine.createSpy('folders').and.returnValue([]),
      pasteWidget: jasmine.createSpy('pasteWidget').and.resolveTo(widget({ id: 'new-widget' })),
      removeWidget: jasmine.createSpy('removeWidget').and.resolveTo(undefined),
    };
  });

  it('copy puts a non-cut snapshot on the clipboard', () => {
    const component = createComponent();

    component.onWidgetCopy(widget());

    expect(clipboard.entries()[0]?.isCut).toBeFalse();
    expect(clipboard.cutWidgetIds().size).toBe(0);
  });

  it('cut records the source widget and folder for a move', () => {
    const component = createComponent();

    component.onWidgetCut(widget({ id: 'src-widget' }));

    expect(clipboard.entries()[0]?.isCut).toBeTrue();
    expect(clipboard.entries()[0]?.origin).toEqual({ widgetId: 'src-widget', folderId: 'current-folder' });
  });

  it('paste of a copy creates the widget and keeps the clipboard for repeat pastes', async () => {
    const component = createComponent();
    clipboard.copy(widget(), 'current-folder');

    await component.onWidgetPaste({ x: 3, y: 0 });

    expect(folderStub.pasteWidget).toHaveBeenCalledWith(clipboard.entries()[0], 3, 0);
    expect(folderStub.removeWidget).not.toHaveBeenCalled();
    expect(clipboard.hasContent()).toBeTrue();
  });

  it('paste of a cut removes the source and clears the clipboard', async () => {
    const component = createComponent();
    clipboard.cut(widget({ id: 'src-widget' }), 'origin-folder');

    await component.onWidgetPaste({ x: 3, y: 0 });

    expect(folderStub.pasteWidget).toHaveBeenCalled();
    expect(folderStub.removeWidget).toHaveBeenCalledWith('src-widget', 'origin-folder');
    expect(clipboard.hasContent()).toBeFalse();
  });

  it('does not remove the cut source when the paste fails', async () => {
    const component = createComponent();
    folderStub.pasteWidget.and.resolveTo(null);
    clipboard.cut(widget({ id: 'src-widget' }), 'origin-folder');

    await component.onWidgetPaste({ x: 3, y: 0 });

    expect(folderStub.removeWidget).not.toHaveBeenCalled();
    expect(clipboard.hasContent()).toBeTrue();
  });

  it('ignores paste when the clipboard is empty', async () => {
    const component = createComponent();

    await component.onWidgetPaste({ x: 3, y: 0 });

    expect(folderStub.pasteWidget).not.toHaveBeenCalled();
  });
});

// Dropping an application on a free cell builds a launch button and opens the editor on it, so the
// user reviews what was configured for them (issue #395).
describe('DeckPageComponent application drop', () => {
  const created = widget({ id: 'app-widget' });

  let folderStub: { selectedFolderId: jasmine.Spy; folders: jasmine.Spy; addWidgetFromApplication: jasmine.Spy };
  let router: { navigate: jasmine.Spy };
  let takenCells: string[];

  function createComponent(): DeckPageComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'p1' } },
        { provide: PortabilityService, useValue: {} },
        { provide: IconPackService, useValue: {} },
        { provide: IconPrefetchService, useValue: { prefetch: jasmine.createSpy('prefetch') } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: Router, useValue: router },
      ],
    });

    const component = TestBed.createComponent(DeckPageComponent).componentInstance;
    (component as unknown as { widgetGrid: unknown }).widgetGrid = {
      cellAtViewportPoint: (x: number, y: number) => ({ x, y }),
      isCellTaken: (x: number, y: number) => takenCells.includes(`${x},${y}`),
    };
    return component;
  }

  function accepts(component: DeckPageComponent, x: number, y: number): boolean {
    return (component as unknown as {
      acceptDeckDrop: (kind: string, at: { x: number; y: number }) => boolean;
    }).acceptDeckDrop('application', { x, y });
  }

  beforeEach(() => {
    takenCells = [];
    router = { navigate: jasmine.createSpy('navigate').and.resolveTo(true) };
    folderStub = {
      selectedFolderId: jasmine.createSpy('selectedFolderId').and.returnValue('current-folder'),
      folders: jasmine.createSpy('folders').and.returnValue([]),
      addWidgetFromApplication: jasmine.createSpy('addWidgetFromApplication')
        .and.resolveTo({ success: true, data: created }),
    };
  });

  it('creates the widget at the dropped cell without opening the editor', async () => {
    const component = createComponent();

    await component.onShellDropped({ kind: 'application', path: '/Applications/Calculator.app', x: 2, y: 1 });

    expect(folderStub.addWidgetFromApplication)
      .toHaveBeenCalledWith(2, 1, '/Applications/Calculator.app');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  // The drop target spans the whole grid, but the widget lands in one cell - so the affordance has to
  // follow the cursor cell instead of outlining the deck (issue #395).
  it('tracks the hovered cell, and drops it again when the drag leaves', () => {
    const component = createComponent();
    const cell = () => (component as unknown as { dropTargetCell: () => unknown }).dropTargetCell();

    component.onShellDropHover({ kind: 'application', path: '/a.app', x: 2, y: 1 });
    expect(cell()).toEqual({ x: 2, y: 1 });

    component.onShellDropHover(null);
    expect(cell()).toBeNull();
  });

  it('does not claim a cell for an icon pack', () => {
    const component = createComponent();

    component.onShellDropHover({ kind: 'iconPack', path: '/a.macroDeckIconPack', x: 2, y: 1 });

    expect((component as unknown as { dropTargetCell: () => unknown }).dropTargetCell()).toBeNull();
  });

  it('marks the cell busy while the host builds the widget', async () => {
    const component = createComponent();
    const busy = () => (component as unknown as { creatingWidgetCell: () => unknown }).creatingWidgetCell();
    let finish: (result: unknown) => void = () => undefined;
    folderStub.addWidgetFromApplication.and.returnValue(new Promise(resolve => { finish = resolve; }));

    const drop = component.onShellDropped({ kind: 'application', path: '/a.app', x: 2, y: 1 });
    expect(busy()).toEqual({ x: 2, y: 1 });

    finish({ success: true, data: created });
    await drop;

    expect(busy()).toBeNull();
  });

  it('is only accepted over a free cell', () => {
    const component = createComponent();
    takenCells = ['2,1'];

    expect(accepts(component, 2, 1)).toBeFalse();
    expect(accepts(component, 3, 1)).toBeTrue();
  });

  it('shows the host rejection', async () => {
    const component = createComponent();
    folderStub.addWidgetFromApplication.and.resolveTo({
      success: false,
      error: { code: 'ValidationError', message: 'notes.md is not an application' },
    });

    await component.onShellDropped({ kind: 'application', path: '/tmp/notes.md', x: 0, y: 0 });

    expect(router.navigate).not.toHaveBeenCalled();
    expect((component as unknown as { deckError: () => string | null }).deckError())
      .toBe('notes.md is not an application');
  });
});

// The desktop UI warms the icons of the folders it is not showing, so the first switch into one
// does not waterfall its icons over the network (issue #421). The grid here is not full-viewport,
// so the metrics have to come from the laid-out grid rather than the window.
describe('DeckPageComponent icon prefetch', () => {
  const BUTTON = momentaryButtonWidget({ id: 'b1', w: 1, h: 1, iconId: 'icon-a' });

  const selectedProfileId = signal<string | null>('p1');
  const folders = signal<{ id: string; widgets: GridWidget[] }[]>([]);

  let folderStub: { selectedFolderId: jasmine.Spy };
  let profileStub: { isCurrentProfileLocked: () => boolean; selectedProfileId: typeof selectedProfileId };
  let prefetch: jasmine.Spy;

  function createComponent(): DeckPageComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: profileStub },
        { provide: PortabilityService, useValue: {} },
        { provide: IconPackService, useValue: {} },
        { provide: IconPrefetchService, useValue: { prefetch } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
      ],
    });
    const component = TestBed.createComponent(DeckPageComponent).componentInstance;
    TestBed.tick();
    return component;
  }

  function layOutGrid(component: DeckPageComponent): void {
    (component as unknown as { widgetGrid: unknown }).widgetGrid = {
      layoutBounds: { width: 800, height: 600 },
      outerMargin: 16,
    };
  }

  function targets(call = 0): unknown {
    return (prefetch.calls.argsFor(call)[0] as () => unknown)();
  }

  beforeEach(() => {
    prefetch = jasmine.createSpy('prefetch');
    selectedProfileId.set('p1');
    folders.set([
      { id: 'f1', widgets: [BUTTON] },
      { id: 'f2', widgets: [momentaryButtonWidget({ id: 'b2', w: 1, h: 1, iconId: 'icon-b' })] },
    ]);
    profileStub = {
      isCurrentProfileLocked: () => false,
      selectedProfileId,
    };
    folderStub = {
      selectedFolderId: jasmine.createSpy('selectedFolderId').and.returnValue('f1'),
      folders,
      getEffectiveCols: () => 5,
      getEffectiveRows: () => 3,
      getEffectiveSpacing: () => 12,
      isLoading: () => true,
      loadError: () => null,
      selectedFolder: () => null,
      currentFolderName: () => '',
      currentWidgets: () => [],
      currentCols: () => 5,
      currentRows: () => 3,
      currentSpacing: () => 12,
      currentBackground: () => null,
      currentBorderRadius: () => 0,
      minCols: () => 1,
      minRows: () => 1,
    } as unknown as typeof folderStub;
  });

  it('registers a prefetch provider for the loaded profile', () => {
    createComponent();

    expect(prefetch).toHaveBeenCalledTimes(1);
  });

  it('defers until the grid has a layout', () => {
    createComponent();

    expect(targets()).toBeNull();
  });

  it('warms the folders that are not on screen, at the rendition the grid will request', () => {
    const component = createComponent();
    layOutGrid(component);

    const cell = computeCellDimensions(800 - 32, 600 - 32, 5, 3, 12);
    const expected = iconSizeBucket(Math.max(cell.cellWidth, cell.cellHeight) * (window.devicePixelRatio || 1));

    expect(targets()).toEqual([{ iconId: 'icon-b', size: expected }]);
  });

  it('re-registers when the profile changes, but not for an edit inside the same folders', () => {
    createComponent();
    expect(prefetch).toHaveBeenCalledTimes(1);

    // Editing widgets replaces the folder array on every move; the same folders must not re-register.
    folders.set([
      { id: 'f1', widgets: [momentaryButtonWidget({ id: 'b1', iconId: 'icon-moved' })] },
      { id: 'f2', widgets: [] },
    ]);
    TestBed.tick();
    expect(prefetch).toHaveBeenCalledTimes(1);

    selectedProfileId.set('p2');
    TestBed.tick();
    expect(prefetch).toHaveBeenCalledTimes(2);
  });
});

// Batch selection actions and keyboard shortcuts (issue #213). `onKeyDown` is invoked directly
// rather than via a real `document` dispatch - it is the `@HostListener('document:keydown')`
// handler itself, and the test only needs its observable behaviour, not Angular's host-listener
// wiring.
describe('DeckPageComponent keyboard shortcuts and batch actions', () => {
  let folderStub: {
    selectedFolderId: jasmine.Spy;
    selectedFolder: jasmine.Spy;
    folders: jasmine.Spy;
    currentWidgets: jasmine.Spy;
    currentCols: jasmine.Spy;
    currentRows: jasmine.Spy;
    removeWidget: jasmine.Spy;
    removeWidgets: jasmine.Spy;
    pasteWidget: jasmine.Spy;
    pasteWidgets: jasmine.Spy;
    setWidgetsPinned: jasmine.Spy;
    setWidgetPinned: jasmine.Spy;
  };
  let clipboard: WidgetClipboardService;
  let selection: WidgetSelectionService;
  let widgets: GridWidget[];

  function fireKeyDown(component: DeckPageComponent, init: KeyboardEventInit & { key: string }, target?: EventTarget): void {
    const event = new KeyboardEvent('keydown', init);
    if (target) {
      Object.defineProperty(event, 'target', { value: target });
    }
    (component as unknown as { onKeyDown: (e: KeyboardEvent) => void }).onKeyDown(event);
  }

  function createComponent(): DeckPageComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'p1' } },
        { provide: PortabilityService, useValue: {} },
        { provide: IconPackService, useValue: {} },
        { provide: IconPrefetchService, useValue: { prefetch: jasmine.createSpy('prefetch') } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
      ],
    });
    clipboard = TestBed.inject(WidgetClipboardService);
    selection = TestBed.inject(WidgetSelectionService);
    return TestBed.createComponent(DeckPageComponent).componentInstance;
  }

  beforeEach(() => {
    widgets = [
      widget({ id: 'w1', folderId: 'f1', x: 0, y: 0, w: 1, h: 1 }),
      widget({ id: 'w2', folderId: 'f1', x: 1, y: 0, w: 1, h: 1 }),
      widget({ id: 'w3', folderId: 'f1', x: 2, y: 0, w: 1, h: 1 }),
    ];
    folderStub = {
      selectedFolderId: jasmine.createSpy('selectedFolderId').and.returnValue('f1'),
      selectedFolder: jasmine.createSpy('selectedFolder').and.returnValue({ id: 'f1' }),
      folders: jasmine.createSpy('folders').and.returnValue([]),
      currentWidgets: jasmine.createSpy('currentWidgets').and.callFake(() => widgets),
      currentCols: jasmine.createSpy('currentCols').and.returnValue(5),
      currentRows: jasmine.createSpy('currentRows').and.returnValue(3),
      removeWidget: jasmine.createSpy('removeWidget').and.resolveTo(undefined),
      removeWidgets: jasmine.createSpy('removeWidgets').and.resolveTo({ success: true }),
      pasteWidget: jasmine.createSpy('pasteWidget').and.resolveTo(widget({ id: 'pasted' })),
      pasteWidgets: jasmine.createSpy('pasteWidgets').and.resolveTo({ success: true }),
      setWidgetsPinned: jasmine.createSpy('setWidgetsPinned').and.resolveTo({ success: true }),
      setWidgetPinned: jasmine.createSpy('setWidgetPinned').and.resolveTo({ success: true }),
    };
  });

  it('Delete opens a confirmation naming the number of selected widgets', () => {
    const component = createComponent();
    selection.selectAll(widgets);

    fireKeyDown(component, { key: 'Delete' });

    expect(component.showDeleteConfirm()).toBeTrue();
    expect(component.widgetToDelete()).toEqual(['w1', 'w2', 'w3']);
    expect((component as unknown as { deleteConfirmMessage: () => string }).deleteConfirmMessage()).toContain('3');
  });

  it('Shift+click on an already-selected widget removes just that widget', () => {
    const component = createComponent();
    selection.selectAll(widgets);

    component.onWidgetSelect({ widget: widgets[1], toggle: false, range: true });

    expect(selection.ids()).toEqual(new Set(['w1', 'w3']));
  });

  it('Shift+click on an unselected widget still extends the range', () => {
    const component = createComponent();
    selection.selectOnly('w1');

    component.onWidgetSelect({ widget: widgets[2], toggle: false, range: true });

    expect(selection.ids().has('w1')).toBeTrue();
    expect(selection.ids().has('w3')).toBeTrue();
  });

  it('Ctrl/Cmd+A selects every widget in the folder', () => {
    const component = createComponent();

    fireKeyDown(component, { key: 'a', ctrlKey: true });

    expect(selection.ids()).toEqual(new Set(['w1', 'w2', 'w3']));
  });

  it('Ctrl/Cmd+C copies the whole selection onto the clipboard, without issuing any request', () => {
    const component = createComponent();
    selection.selectAll(widgets);

    fireKeyDown(component, { key: 'c', ctrlKey: true });

    expect(clipboard.entries().length).toBe(3);
    expect(clipboard.entries().every(e => !e.isCut)).toBeTrue();
    expect(folderStub.removeWidgets).not.toHaveBeenCalled();
  });

  it('Ctrl/Cmd+X cuts the whole selection onto the clipboard, without issuing any request', () => {
    const component = createComponent();
    selection.selectAll(widgets);

    fireKeyDown(component, { key: 'x', ctrlKey: true });

    expect(clipboard.entries().length).toBe(3);
    expect(clipboard.entries().every(e => e.isCut)).toBeTrue();
    expect(folderStub.removeWidgets).not.toHaveBeenCalled();
  });

  it('Ctrl/Cmd+V issues ONE batch paste request listing every clipboard entry, not N requests', async () => {
    const component = createComponent();
    selection.selectAll(widgets);
    clipboard.copyMany(widgets, 'f1');

    fireKeyDown(component, { key: 'v', ctrlKey: true });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    expect(folderStub.pasteWidgets).toHaveBeenCalledTimes(1);
    const [entries] = folderStub.pasteWidgets.calls.mostRecent().args;
    expect(entries.length).toBe(3);
  });

  it('Ctrl/Cmd+X then Ctrl/Cmd+V for a same-folder group sends the cut ids as replaceIds in ONE ' +
    'request, and issues no separate delete (issue #213)', async () => {
    const component = createComponent();
    selection.selectAll(widgets);
    clipboard.cutMany(widgets, 'f1');
    folderStub.pasteWidgets.and.resolveTo({ success: true, data: { replacedWidgetIds: ['w1', 'w2', 'w3'] } });

    fireKeyDown(component, { key: 'v', ctrlKey: true });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    expect(folderStub.pasteWidgets).toHaveBeenCalledTimes(1);
    expect(folderStub.removeWidgets).not.toHaveBeenCalled();
    expect(folderStub.removeWidget).not.toHaveBeenCalled();
    expect(clipboard.hasContent()).toBeFalse();
  });

  it('a same-folder cut+paste still deletes a source the host did NOT report as replaced', async () => {
    const component = createComponent();
    selection.selectAll(widgets);
    clipboard.cutMany(widgets, 'f1');
    folderStub.pasteWidgets.and.resolveTo({ success: true, data: { replacedWidgetIds: ['w1', 'w2'] } });

    fireKeyDown(component, { key: 'v', ctrlKey: true });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    expect(folderStub.removeWidget).toHaveBeenCalledWith('w3', 'f1');
    expect(folderStub.removeWidgets).not.toHaveBeenCalled();
  });

  it('Delete then confirm issues ONE batch delete request listing all ids, not N requests', async () => {
    const component = createComponent();
    selection.selectAll(widgets);

    fireKeyDown(component, { key: 'Delete' });
    await component.confirmWidgetDelete();

    expect(folderStub.removeWidgets).toHaveBeenCalledTimes(1);
    expect(folderStub.removeWidgets).toHaveBeenCalledWith(['w1', 'w2', 'w3']);
    expect(folderStub.removeWidget).not.toHaveBeenCalled();
  });

  it('a single selected widget still deletes through the single-widget call, not the batch one', async () => {
    const component = createComponent();
    selection.selectOnly('w1');

    fireKeyDown(component, { key: 'Delete' });
    await component.confirmWidgetDelete();

    expect(folderStub.removeWidget).toHaveBeenCalledWith('w1');
    expect(folderStub.removeWidgets).not.toHaveBeenCalled();
  });

  it('Escape clears the selection', () => {
    const component = createComponent();
    selection.selectAll(widgets);
    expect(selection.hasSelection()).toBeTrue();

    fireKeyDown(component, { key: 'Escape' });

    expect(selection.hasSelection()).toBeFalse();
  });

  it('none of the shortcuts fire while a confirmation modal is open', () => {
    const component = createComponent();
    selection.selectAll(widgets);
    component.showDeleteConfirm.set(true);
    component.widgetToDelete.set(['w1']);

    fireKeyDown(component, { key: 'Escape' });

    // Escape would otherwise clear the selection - it must not while the modal is open.
    expect(selection.hasSelection()).toBeTrue();
  });

  it('none of the shortcuts fire while focus is in a text input', () => {
    const component = createComponent();
    selection.selectAll(widgets);
    const input = document.createElement('input');

    fireKeyDown(component, { key: 'Delete' }, input);

    expect(component.showDeleteConfirm()).toBeFalse();
    expect(selection.hasSelection()).toBeTrue();
  });

  it('none of the shortcuts fire while focus is in a textarea', () => {
    const component = createComponent();
    selection.selectAll(widgets);
    const textarea = document.createElement('textarea');

    fireKeyDown(component, { key: 'Escape' }, textarea);

    expect(selection.hasSelection()).toBeTrue();
  });

  it('no shortcut acts when the deck is not in edit mode', () => {
    const component = createComponent();
    selection.selectAll(widgets);
    component.editMode.set(false);

    fireKeyDown(component, { key: 'Delete' });

    expect(component.showDeleteConfirm()).toBeFalse();
  });
});
