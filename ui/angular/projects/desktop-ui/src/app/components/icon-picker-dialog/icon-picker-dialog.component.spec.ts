import { EventEmitter, WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { IconImportBatchState } from '@macro-deck/runtime';
import { ApiService, IconImageService } from '@shared';
import { IconImportBatchModel, IconModel, IconPackModel, IconPackService } from '../../services/icon-pack.service';
import { ShellFileDropService } from '../../services/shell-file-drop.service';
import { IconPickerDialogComponent } from './icon-picker-dialog.component';

interface DialogAccess {
  readonly selectedPackId: () => string | null;
  readonly searchQuery: WritableSignal<string>;
  readonly icons: () => IconModel[];
  readonly canImport: () => boolean;
  readonly importBusy: () => boolean;
  iconPicked: EventEmitter<IconModel>;
  onIconClick(icon: IconModel): void;
  onPackChange(packId: string | null): void;
  onFilesDropped(files: File[]): Promise<void>;
  onPathsDropped(paths: string[]): Promise<void>;
}

describe('IconPickerDialogComponent', () => {
  let fixture: ComponentFixture<IconPickerDialogComponent>;
  let component: DialogAccess;
  let packs: WritableSignal<IconPackModel[]>;
  let activeBatches: WritableSignal<ReadonlyMap<string, IconImportBatchModel>>;
  let iconsByPack: Map<string, WritableSignal<IconModel[]>>;
  let importSpy: jasmine.Spy;
  let importFromPathSpy: jasmine.Spy;

  function pack(id: string, name: string, overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id,
      name,
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

  function icon(id: string, packId: string, name: string, overrides: Partial<IconModel> = {}): IconModel {
    return {
      id,
      packId,
      name,
      isAnimated: false,
      processingState: 'Ready',
      availableSizes: [128],
      ...overrides,
    };
  }

  function batch(id: string, packId: string, state: IconImportBatchState): IconImportBatchModel {
    return { id, packId, state, processed: 0, failed: 0 };
  }

  function iconsSignal(packId: string): WritableSignal<IconModel[]> {
    let existing = iconsByPack.get(packId);
    if (!existing) {
      existing = signal<IconModel[]>([]);
      iconsByPack.set(packId, existing);
    }
    return existing;
  }

  function setIcons(packId: string, icons: IconModel[]): void {
    iconsSignal(packId).set(icons);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function findButtonOrNull(label: string): HTMLButtonElement | null {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('shared-button button')
    ) as HTMLButtonElement[];
    return buttons.find(b => b.textContent?.trim() === label) ?? null;
  }

  function findButton(label: string): HTMLButtonElement {
    const button = findButtonOrNull(label);
    if (!button) {
      throw new Error(`Button "${label}" not found`);
    }
    return button;
  }

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  let restoreFromPathSpy: jasmine.Spy;
  let importPacksSpy: jasmine.Spy;

  beforeEach(() => {
    // The directive reads ShellFileDropService.supported once, at construction - which happens when
    // TestBed.createComponent() builds the view, before any test body runs. So the bridge has to be
    // in place before that call, not inside the individual test that drives a drop through it.
    useShell({ onFileDrop: () => Promise.resolve(() => undefined) });

    packs = signal<IconPackModel[]>([]);
    activeBatches = signal<ReadonlyMap<string, IconImportBatchModel>>(new Map());
    iconsByPack = new Map();
    importSpy = jasmine.createSpy('import').and.resolveTo(null);
    importFromPathSpy = jasmine.createSpy('importFromPath').and.resolveTo(null);
    restoreFromPathSpy = jasmine.createSpy('restoreFromPath').and.resolveTo(null);
    importPacksSpy = jasmine.createSpy('importPacks').and.resolveTo(false);

    const iconPacksStub = {
      packs,
      isLoading: signal(false),
      loadError: signal<string | null>(null),
      activeBatches,
      loadPacks: jasmine.createSpy('loadPacks').and.resolveTo(undefined),
      iconsFor: (packId: string) => iconsSignal(packId).asReadonly(),
      import: importSpy,
      importFromPath: importFromPathSpy,
      restoreFromPath: restoreFromPathSpy,
      importPacks: importPacksSpy,
      dismissBatch: jasmine.createSpy('dismissBatch'),
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [IconPickerDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: IconPackService, useValue: iconPacksStub },
        { provide: IconImageService, useValue: { getIconUrl: () => 'http://host/icon.webp' } },
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    fixture = TestBed.createComponent(IconPickerDialogComponent);
    component = fixture.componentInstance as unknown as DialogAccess;
  });

  afterEach(() => useShell(null));

  it('auto-selects the pack marked isDefault, not merely the first', async () => {
    packs.set([pack('a', 'Pack A'), pack('b', 'Pack B', { isDefault: true })]);
    await settle();

    expect(component.selectedPackId()).toBe('b');
  });

  it('falls back to packs[0] when none is default', async () => {
    packs.set([pack('a', 'Pack A'), pack('b', 'Pack B')]);
    await settle();

    expect(component.selectedPackId()).toBe('a');
  });

  it('re-selects when the currently selected pack disappears from packs()', async () => {
    packs.set([pack('a', 'Pack A'), pack('b', 'Pack B', { isDefault: true })]);
    await settle();
    expect(component.selectedPackId()).toBe('b');

    packs.set([pack('a', 'Pack A')]);
    await settle();

    expect(component.selectedPackId()).toBe('a');
  });

  it('keeps icons that are still processing in the grid, but does not let them be picked', async () => {
    packs.set([pack('a', 'Pack A')]);
    const pending = icon('pending-1', 'a', 'pending icon', { processingState: 'Pending' });
    setIcons('a', [pending, icon('ready-1', 'a', 'ready icon')]);
    await settle();

    expect(component.icons().map(i => i.id)).toEqual(['pending-1', 'ready-1']);

    component.onIconClick(pending);
    fixture.detectChanges();

    expect(findButton('Use icon').disabled).toBeTrue();
  });

  it('narrows the icon list by search, case-insensitively', async () => {
    packs.set([pack('a', 'Pack A')]);
    setIcons('a', [icon('1', 'a', 'Home'), icon('2', 'a', 'Office')]);
    await settle();

    component.searchQuery.set('OFF');
    fixture.detectChanges();

    expect(component.icons().map(i => i.id)).toEqual(['2']);
  });

  it('requires an explicit confirm before emitting the picked icon', async () => {
    packs.set([pack('a', 'Pack A')]);
    const readyIcon = icon('1', 'a', 'Home');
    setIcons('a', [readyIcon]);
    await settle();

    let picked: IconModel | null = null;
    component.iconPicked.subscribe(i => (picked = i));

    component.onIconClick(readyIcon);
    fixture.detectChanges();
    expect(picked).toBeNull();

    const confirmButton = findButton('Use icon');
    expect(confirmButton.disabled).toBeFalse();
    confirmButton.click();
    await new Promise(resolve => setTimeout(resolve, 200));

    expect(picked!).toEqual(readyIcon);
  });

  it('clears the pending selection when the pack changes, disabling the confirm button again', async () => {
    packs.set([pack('a', 'Pack A'), pack('b', 'Pack B')]);
    setIcons('a', [icon('1', 'a', 'Home')]);
    await settle();

    component.onIconClick(component.icons()[0]);
    fixture.detectChanges();
    expect(findButton('Use icon').disabled).toBeFalse();

    component.onPackChange('b');
    fixture.detectChanges();

    expect(findButton('Use icon').disabled).toBeTrue();
  });

  it('offers no import control for a read-only pack', async () => {
    packs.set([pack('ro', 'Read-only', { isReadOnly: true })]);
    await settle();

    expect(component.canImport()).toBeFalse();
    expect(findButtonOrNull('Import icons…')).toBeNull();
  });

  it('imports the picked files into the selected writable pack', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();

    expect(findButtonOrNull('Import icons…')).not.toBeNull();

    const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input).not.toBeNull();
    const file = new File(['icon'], 'icon.png');
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(importSpy).toHaveBeenCalledWith('a', [file]);
  });

  it('imports files dropped on the grid into the selected pack', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();

    const file = new File(['icon'], 'icon.png');
    await component.onFilesDropped([file]);

    expect(importSpy).toHaveBeenCalledWith('a', [file]);
  });

  it('imports every path dropped inside the desktop shell as one batch', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();

    await component.onPathsDropped(['/icons/one.png', '/icons/two.png']);

    expect(importFromPathSpy.calls.allArgs()).toEqual([
      ['a', ['/icons/one.png', '/icons/two.png']],
    ]);
  });

  it('leaks no icon-pack archive into a real shell drop on the grid', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();

    const grid = fixture.nativeElement.querySelector('.grid-area') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(grid);

    TestBed.inject(ShellFileDropService).handleEvent({
      kind: 'drop',
      x: 1,
      y: 1,
      paths: [
        { path: '/d/Neon.macroDeckIconPack', directory: false },
        { path: '/d/logo.png', directory: false },
      ],
    });
    await fixture.whenStable();

    expect(importFromPathSpy).toHaveBeenCalledTimes(1);
    expect(importFromPathSpy).toHaveBeenCalledWith('a', ['/d/logo.png']);
    expect(restoreFromPathSpy).not.toHaveBeenCalled();
    expect(importPacksSpy).not.toHaveBeenCalled();
  });

  it('ignores drops on a read-only pack', async () => {
    packs.set([pack('ro', 'Read-only', { isReadOnly: true })]);
    await settle();

    await component.onFilesDropped([new File(['icon'], 'icon.png')]);
    await component.onPathsDropped(['/icons/one.png']);

    expect(importSpy).not.toHaveBeenCalled();
    expect(importFromPathSpy).not.toHaveBeenCalled();
  });

  it('stays busy while the host is still processing a batch for the selected pack', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();
    expect(component.importBusy()).toBeFalse();

    activeBatches.set(new Map([['batch-1', batch('batch-1', 'a', 'Processing')]]));
    fixture.detectChanges();

    expect(component.importBusy()).toBeTrue();
    expect(findButton('Import icons…').disabled).toBeTrue();
  });

  it('is not busy for a finished batch, nor for a batch on another pack', async () => {
    packs.set([pack('a', 'Pack A')]);
    await settle();

    activeBatches.set(new Map([['batch-1', batch('batch-1', 'a', 'Completed')]]));
    fixture.detectChanges();
    expect(component.importBusy()).toBeFalse();

    activeBatches.set(new Map([['batch-2', batch('batch-2', 'other', 'Processing')]]));
    fixture.detectChanges();
    expect(component.importBusy()).toBeFalse();
  });
});
