import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService, ContextMenuItem, LocalizationService, ToastService } from '@shared';
import { IconTileClick } from '../../icon-grid/icon-tile.component';
import { CopyTextModalComponent } from '../../overlay/copy-text-modal/copy-text-modal.component';
import { IconModel, IconPackModel, IconPackService } from '../../../services/icon-pack.service';
import { ShellFileDropService } from '../../../services/shell-file-drop.service';
import { TextClipboardService } from '../../../services/text-clipboard.service';
import { IconPacksPageComponent } from './icon-packs-page.component';

interface SelectionAccess {
  onIconClick(click: IconTileClick): void;
  clearSelection(): void;
  selectedIconIds(): ReadonlySet<string>;
  filteredIcons(): IconModel[];
}

describe('IconPacksPageComponent icon selection', () => {
  let component: SelectionAccess;

  function icon(id: string): IconModel {
    return {
      id,
      packId: 'pack',
      name: id,
      isAnimated: false,
      processingState: 'Ready',
      availableSizes: [128],
    } as IconModel;
  }

  function click(id: string, options: Partial<Omit<IconTileClick, 'icon'>> = {}): IconTileClick {
    return { icon: icon(id), toggle: false, range: false, ...options };
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    const fixture = TestBed.createComponent(IconPacksPageComponent);
    component = fixture.componentInstance as unknown as SelectionAccess;
  });

  it('accumulates the selection across repeated Ctrl/Cmd clicks on different icons', () => {
    component.onIconClick(click('a', { toggle: true }));
    component.onIconClick(click('b', { toggle: true }));
    component.onIconClick(click('c', { toggle: true }));

    expect(component.selectedIconIds()).toEqual(new Set(['a', 'b', 'c']));
  });

  it('removes an already-selected icon when Ctrl/Cmd clicking it again', () => {
    component.onIconClick(click('a', { toggle: true }));
    component.onIconClick(click('b', { toggle: true }));
    component.onIconClick(click('a', { toggle: true }));

    expect(component.selectedIconIds()).toEqual(new Set(['b']));
  });

  it('treats a plain click as a toggle while a selection is active', () => {
    component.onIconClick(click('a', { toggle: true }));
    component.onIconClick(click('b'));

    expect(component.selectedIconIds()).toEqual(new Set(['a', 'b']));
  });

  it('replaces the selection on a plain click when nothing is selected', () => {
    component.onIconClick(click('a'));

    expect(component.selectedIconIds()).toEqual(new Set(['a']));
  });

  it('clears the selection', () => {
    component.onIconClick(click('a', { toggle: true }));
    component.clearSelection();

    expect(component.selectedIconIds().size).toBe(0);
  });
});

interface PackImportExportAccess {
  onPacksPicked(input: HTMLInputElement): Promise<void>;
  importFilesInto(pack: IconPackModel, files: File[]): Promise<void>;
  onPackMenuAction(action: string): void;
  exportPack(pack: IconPackModel): Promise<void>;
  packMenu: { set(state: { pack: IconPackModel; x: number; y: number } | null): void };
  packMenuItems(): ContextMenuItem[];
  addPackMenuItems: ContextMenuItem[];
  onAddPackMenuAction(action: string): void;
  packDialogMode(): 'create' | 'edit' | null;
  packInput?: { nativeElement: HTMLInputElement };
}

describe('IconPacksPageComponent pack import/export', () => {
  let fixture: ComponentFixture<IconPacksPageComponent>;
  let component: PackImportExportAccess;
  let iconPacks: IconPackService;

  function pack(id: string, overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id,
      name: id,
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 0,
      ownerKind: 'User',
      canDelete: true,
      ...overrides,
    };
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    iconPacks = TestBed.inject(IconPackService);
    fixture = TestBed.createComponent(IconPacksPageComponent);
    component = fixture.componentInstance as unknown as PackImportExportAccess;
    fixture.detectChanges();
  });

  it('offers Create pack and Import pack in the add-pack menu', () => {
    const ids = component.addPackMenuItems.map(i => i.id);

    expect(ids).toEqual(['create', 'import']);
  });

  it('opens the create dialog from the add-pack menu', () => {
    component.onAddPackMenuAction('create');

    expect(component.packDialogMode()).toBe('create');
  });

  it('opens the pack file picker from the add-pack menu', () => {
    const input = component.packInput?.nativeElement;
    expect(input).toBeDefined();
    const clickSpy = spyOn(input!, 'click');

    component.onAddPackMenuAction('import');

    expect(clickSpy).toHaveBeenCalled();
  });

  it('accepts all pack formats in the pack file input', () => {
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>(
      'input[accept=".macroDeckIconPack,.streamDeckIconPack,.tpi,.zip"]'
    );

    expect(input).not.toBeNull();
  });

  it('accepts .tpi, .macroDeckIconPack and the icon containers a path drop accepts, never .app', () => {
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('input[multiple]:not([webkitdirectory])');
    const accept = input?.accept ?? '';

    for (const type of ['.ico', '.icns', '.exe', '.dll', '.png', '.lottie', '.json', '.tpi', '.macroDeckIconPack']) {
      expect(accept).toContain(type);
    }
    expect(accept).not.toContain('.app');
  });

  it('forwards picked pack files to importPacks and clears the input', async () => {
    const importSpy = spyOn(iconPacks, 'importPacks').and.resolveTo(true);
    const input = document.createElement('input');
    input.type = 'file';
    const file = new File([1 as unknown as BlobPart], 'x.tpi');
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;

    await component.onPacksPicked(input);

    expect(importSpy).toHaveBeenCalledWith([file]);
    expect(input.value).toBe('');
  });

  it('imports dropped files into the dropped-on pack', async () => {
    const importSpy = spyOn(iconPacks, 'import').and.resolveTo(null);
    const files = [new File([1 as unknown as BlobPart], 'x.macroDeckIconPack')];

    await component.importFilesInto(pack('target-pack'), files);

    expect(importSpy).toHaveBeenCalledWith('target-pack', files);
  });

  it('ignores drops on read-only packs', async () => {
    const importSpy = spyOn(iconPacks, 'import');

    await component.importFilesInto(pack('ro', { isReadOnly: true }), [
      new File([1 as unknown as BlobPart], 'x.zip'),
    ]);

    expect(importSpy).not.toHaveBeenCalled();
  });

  it('offers an enabled export entry even for read-only and default packs', () => {
    component.packMenu.set({ pack: pack('ro', { isReadOnly: true, isDefault: true }), x: 0, y: 0 });

    const exportItem = component.packMenuItems().find(i => i.id === 'export');

    expect(exportItem).toBeDefined();
    expect(exportItem?.disabled).toBeFalsy();
  });

  it('exports the pack from the context menu', () => {
    const exportSpy = spyOn(iconPacks, 'exportPack')
      .and.resolveTo({ ok: true, fileName: 'Pack 1.macroDeckIconPack' });
    component.packMenu.set({ pack: pack('pack-1'), x: 0, y: 0 });

    component.onPackMenuAction('export');

    expect(exportSpy).toHaveBeenCalledWith('pack-1');
  });

  it('confirms a finished export with a toast naming the location', async () => {
    spyOn(iconPacks, 'exportPack')
      .and.resolveTo({ ok: true, fileName: 'Pack 1.macroDeckIconPack', path: '/tmp/Pack 1.macroDeckIconPack' });

    await component.exportPack(pack('pack-1'));

    expect(TestBed.inject(ToastService).toasts()).toEqual([
      jasmine.objectContaining({ detail: 'Saved to /tmp/Pack 1.macroDeckIconPack', variant: 'success' }),
    ]);
  });

  it('stays silent when the user dismisses the save dialog', async () => {
    spyOn(iconPacks, 'exportPack')
      .and.resolveTo({ ok: true, canceled: true, fileName: 'Pack 1.macroDeckIconPack' });

    await component.exportPack(pack('pack-1'));

    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });

  it('reports a failed export as an error toast', async () => {
    spyOn(iconPacks, 'exportPack').and.resolveTo({ ok: false, error: 'disk full' });

    await component.exportPack(pack('pack-1'));

    expect(TestBed.inject(ToastService).toasts()).toEqual([
      jasmine.objectContaining({ detail: 'disk full', variant: 'error' }),
    ]);
  });
});

interface PackDeletionAccess {
  packMenuItems(): ContextMenuItem[];
  packMenu: { set(state: { pack: IconPackModel; x: number; y: number } | null): void };
  requestDeletePack(pack: IconPackModel): void;
  packPendingDeletion(): IconPackModel | null;
  confirmDeletePack(): Promise<void>;
  deletePackMessage(pack: IconPackModel): string;
}

describe('IconPacksPageComponent pack deletion and ownership', () => {
  let component: PackDeletionAccess;
  let iconPacks: IconPackService;

  function pack(id: string, overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id,
      name: id,
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 0,
      ownerKind: 'User',
      canDelete: true,
      ...overrides,
    };
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification', 'deleteIconPack']);
    apiSpy.getIconPacks.and.resolveTo({ packs: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    iconPacks = TestBed.inject(IconPackService);
    const fixture = TestBed.createComponent(IconPacksPageComponent);
    component = fixture.componentInstance as unknown as PackDeletionAccess;
    fixture.detectChanges();
  });

  // Counterexample to "hide delete for managed packs": a Store-owned pack must stay directly
  // deletable, with the host routing the removal through the owning provider.
  it('keeps the delete entry enabled for a Store-owned pack and deletes it exactly once on confirm', async () => {
    const deleteSpy = spyOn(iconPacks, 'deletePack').and.resolveTo(true);
    const storePack = pack('store-pack', { ownerKind: 'Store', canDelete: true });
    component.packMenu.set({ pack: storePack, x: 0, y: 0 });

    expect(component.packMenuItems().find(i => i.id === 'delete')?.disabled).toBeFalsy();

    component.requestDeletePack(storePack);
    expect(component.packPendingDeletion()).toBe(storePack);
    await component.confirmDeletePack();

    expect(deleteSpy).toHaveBeenCalledOnceWith('store-pack');
  });

  it('disables the delete entry and refuses the action for a pack whose owner forbids removal', () => {
    const deleteSpy = spyOn(iconPacks, 'deletePack');
    const forbidden = pack('forbidden-pack', { ownerKind: 'Plugin', canDelete: false });
    component.packMenu.set({ pack: forbidden, x: 0, y: 0 });

    expect(component.packMenuItems().find(i => i.id === 'delete')?.disabled).toBeTrue();

    component.requestDeletePack(forbidden);

    expect(component.packPendingDeletion()).toBeNull();
    expect(deleteSpy).not.toHaveBeenCalled();
  });

  it('obeys canDelete rather than sourceType', () => {
    const storeOwnedDeletable = pack('a', { sourceType: 'ExtensionStore', ownerKind: 'Store', canDelete: true });
    const userStampedForbidden = pack('b', { sourceType: 'User', ownerKind: 'Plugin', canDelete: false });

    component.packMenu.set({ pack: storeOwnedDeletable, x: 0, y: 0 });
    expect(component.packMenuItems().find(i => i.id === 'delete')?.disabled).toBeFalsy();

    component.packMenu.set({ pack: userStampedForbidden, x: 0, y: 0 });
    expect(component.packMenuItems().find(i => i.id === 'delete')?.disabled).toBeTrue();
  });

  // Finding 4: DeleteStorePackMessage exists specifically so a Store-owned pack's confirmation names
  // the Store; this pins that the component actually reaches for it.
  it('uses the store-specific confirmation message for a Store-owned pack', () => {
    const localization = TestBed.inject(LocalizationService);
    const storePack = pack('store-pack', { ownerKind: 'Store', name: 'Neon', iconCount: 3 });
    const expected = localization.translateKey(AppStrings.IconPacks.DeleteStorePackMessage, {
      name: 'Neon',
      count: 3,
    });

    expect(component.deletePackMessage(storePack)).toBe(expected);
  });
});

describe('IconPacksPageComponent source label rendering', () => {
  let fixture: ComponentFixture<IconPacksPageComponent>;

  function pack(id: string, overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id,
      name: id,
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 0,
      ownerKind: 'User',
      canDelete: true,
      ...overrides,
    };
  }

  function railRow(name: string): Element {
    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('shared-rail-item')
    );
    const row = rows.find(r => r.textContent?.includes(name));
    if (!row) {
      throw new Error(`Rail row "${name}" not found`);
    }
    return row;
  }

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({
      packs: [pack('store-pack', { ownerKind: 'Store' }), pack('user-pack', { ownerKind: 'User' })],
    });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    fixture = TestBed.createComponent(IconPacksPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  // Finding 3: renders the actual template rather than calling the sourceLabel() helper directly, so
  // deleting the @if (sourceLabel(pack); as source) blocks from the template fails this test.
  it('renders a source tag for a Store-owned pack and none for a user-created one', () => {
    const localization = TestBed.inject(LocalizationService);
    const expected = localization.translateKey(AppStrings.IconPacks.SourceStore);

    expect(railRow('store-pack').querySelector('.tag')?.textContent?.trim()).toBe(expected);
    expect(railRow('user-pack').querySelector('.tag')).toBeNull();
  });
});

interface IconCopyAccess {
  iconMenu: { set(state: { icon: IconModel; x: number; y: number } | null): void };
  onIconMenuAction(action: string): Promise<void>;
  manualCopy(): { value: string; message: string } | null;
}

describe('IconPacksPageComponent copy icon id', () => {
  let fixture: ComponentFixture<IconPacksPageComponent>;
  let component: IconCopyAccess;
  let clipboard: TextClipboardService;

  function icon(id: string): IconModel {
    return {
      id,
      packId: 'pack',
      name: id,
      isAnimated: false,
      processingState: 'Ready',
      availableSizes: [128],
    } as IconModel;
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    clipboard = TestBed.inject(TextClipboardService);
    fixture = TestBed.createComponent(IconPacksPageComponent);
    component = fixture.componentInstance as unknown as IconCopyAccess;
    fixture.detectChanges();
  });

  it('copies the exact icon id and shows a success toast once the copy resolves', async () => {
    spyOn(clipboard, 'copyText').and.resolveTo({ status: 'copied', via: 'clipboard-api' });
    component.iconMenu.set({ icon: icon('icon-1'), x: 0, y: 0 });

    const promise = component.onIconMenuAction('copy-id');
    // The toast confirms an actual copy, so it must not appear before the promise settles.
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
    await promise;

    expect(clipboard.copyText).toHaveBeenCalledWith('icon-1');
    expect(TestBed.inject(ToastService).toasts()).toEqual([
      jasmine.objectContaining({ message: 'Icon id copied', detail: 'icon-1' }),
    ]);
    expect(component.manualCopy()).toBeNull();
  });

  it('shows the manual-copy modal with the exact id and no toast when the copy fails', async () => {
    spyOn(clipboard, 'copyText').and.resolveTo({ status: 'failed', reason: 'insecure-context' });
    component.iconMenu.set({ icon: icon('icon-2'), x: 0, y: 0 });

    await component.onIconMenuAction('copy-id');
    fixture.detectChanges();

    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
    expect(component.manualCopy()).toEqual(jasmine.objectContaining({ value: 'icon-2' }));

    const modal = (fixture.nativeElement as HTMLElement).querySelector('shared-copy-text-modal');
    expect(modal).not.toBeNull();
    expect((modal?.querySelector('input') as HTMLInputElement)?.value).toBe('icon-2');
  });

  it('clears manualCopy when the modal emits closed', async () => {
    spyOn(clipboard, 'copyText').and.resolveTo({ status: 'failed', reason: 'unsupported' });
    component.iconMenu.set({ icon: icon('icon-3'), x: 0, y: 0 });
    await component.onIconMenuAction('copy-id');
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.directive(CopyTextModalComponent));
    expect(modal).toBeTruthy();
    (modal.componentInstance as CopyTextModalComponent).closed.emit();

    expect(component.manualCopy()).toBeNull();
  });
});

describe('IconPacksPageComponent shell drag-and-drop', () => {
  let fixture: ComponentFixture<IconPacksPageComponent>;
  let iconPacks: IconPackService;
  let importFromPathSpy: jasmine.Spy;
  let restoreFromPathSpy: jasmine.Spy;
  let importSpy: jasmine.Spy;

  function pack(id: string, overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id,
      name: id,
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 0,
      ownerKind: 'User',
      canDelete: true,
      ...overrides,
    };
  }

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  let elementFromPointSpy: jasmine.Spy | undefined;

  async function dropAt(target: Element, paths: { path: string; directory: boolean }[]): Promise<void> {
    if (elementFromPointSpy) {
      elementFromPointSpy.and.returnValue(target);
    } else {
      elementFromPointSpy = spyOn(document, 'elementFromPoint').and.returnValue(target);
    }
    TestBed.inject(ShellFileDropService).handleEvent({ kind: 'drop', x: 1, y: 1, paths });
    await settle();
  }

  function railRow(name: string): Element {
    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('shared-rail-item')
    );
    const row = rows.find(r => r.textContent?.includes(name));
    if (!row) {
      throw new Error(`Rail row "${name}" not found`);
    }
    return row;
  }

  function detailPane(): Element {
    const detail = fixture.nativeElement.querySelector('.pack-detail');
    if (!detail) {
      throw new Error('Detail pane not found');
    }
    return detail;
  }

  beforeEach(() => {
    useShell({ onFileDrop: () => Promise.resolve(() => undefined) });
    elementFromPointSpy = undefined;
  });

  afterEach(() => useShell(null));

  async function createWithPacks(packs: IconPackModel[]): Promise<void> {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconPacksPageComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    iconPacks = TestBed.inject(IconPackService);
    importFromPathSpy = spyOn(iconPacks, 'importFromPath').and.resolveTo(null);
    restoreFromPathSpy = spyOn(iconPacks, 'restoreFromPath').and.resolveTo(null);
    importSpy = spyOn(iconPacks, 'import').and.resolveTo(null);
    fixture = TestBed.createComponent(IconPacksPageComponent);
    await settle();
  }

  it('imports everything dropped on a rail row into that row\'s pack, archive included', async () => {
    await createWithPacks([pack('a'), pack('b')]);

    await dropAt(railRow('b'), [
      { path: '/d/logo.png', directory: false },
      { path: '/d/Neon.macroDeckIconPack', directory: false },
      { path: '/d/Spotify.app', directory: true },
    ]);

    expect(importFromPathSpy).toHaveBeenCalledTimes(1);
    expect(importFromPathSpy).toHaveBeenCalledWith(
      'b', ['/d/logo.png', '/d/Neon.macroDeckIconPack', '/d/Spotify.app']
    );
    expect(restoreFromPathSpy).not.toHaveBeenCalled();
    expect(importSpy).not.toHaveBeenCalled();
  });

  it('creates a new pack from a .macroDeckIconPack dropped on the detail pane', async () => {
    await createWithPacks([pack('a')]);
    restoreFromPathSpy.and.resolveTo('Neon');

    await dropAt(detailPane(), [{ path: '/d/Neon.macroDeckIconPack', directory: false }]);

    expect(restoreFromPathSpy).toHaveBeenCalledWith('/d/Neon.macroDeckIconPack');
    expect(importFromPathSpy).not.toHaveBeenCalled();
    expect(TestBed.inject(ToastService).toasts()).toEqual([
      jasmine.objectContaining({ detail: 'Neon' }),
    ]);
  });

  it('imports everything else dropped on the detail pane into the selected pack', async () => {
    await createWithPacks([pack('a')]);

    await dropAt(detailPane(), [
      { path: '/d/logo.png', directory: false },
      { path: '/d/Spotify.app', directory: true },
      { path: '/d/apps', directory: true },
    ]);

    expect(importFromPathSpy).toHaveBeenCalledTimes(1);
    expect(importFromPathSpy).toHaveBeenCalledWith(
      'a', ['/d/logo.png', '/d/Spotify.app', '/d/apps']
    );
    expect(restoreFromPathSpy).not.toHaveBeenCalled();
  });

  it('does both halves of a mixed drop on the detail pane', async () => {
    await createWithPacks([pack('a')]);
    restoreFromPathSpy.and.resolveTo('Neon');

    await dropAt(detailPane(), [
      { path: '/d/logo.png', directory: false },
      { path: '/d/Neon.macroDeckIconPack', directory: false },
    ]);

    expect(restoreFromPathSpy).toHaveBeenCalledWith('/d/Neon.macroDeckIconPack');
    expect(importFromPathSpy).toHaveBeenCalledWith('a', ['/d/logo.png']);
  });

  it('refuses drops on a read-only pack, on both the rail row and the detail pane', async () => {
    await createWithPacks([pack('ro', { isReadOnly: true })]);

    await dropAt(railRow('ro'), [{ path: '/d/logo.png', directory: false }]);
    await dropAt(detailPane(), [{ path: '/d/logo.png', directory: false }]);

    expect(importFromPathSpy).not.toHaveBeenCalled();
    expect(importSpy).not.toHaveBeenCalled();
    expect(restoreFromPathSpy).not.toHaveBeenCalled();
  });

  it('does nothing and does not throw when the detail pane is dropped on with no pack selected', async () => {
    await createWithPacks([]);

    await expectAsync(dropAt(detailPane(), [{ path: '/d/logo.png', directory: false }])).toBeResolved();

    expect(importFromPathSpy).not.toHaveBeenCalled();
    expect(restoreFromPathSpy).not.toHaveBeenCalled();
  });
});
