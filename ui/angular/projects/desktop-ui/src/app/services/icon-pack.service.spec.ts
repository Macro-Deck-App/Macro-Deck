import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { IpcIcon, IpcIconPack } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { FileSaveService } from './file-save.service';
import { IconPackService } from './icon-pack.service';

describe('IconPackService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: IconPackService;

  const packId = '11111111-1111-1111-1111-111111111111';
  const otherPackId = '22222222-2222-2222-2222-222222222222';

  function ipcPack(id: string, name: string, overrides: Partial<IpcIconPack> = {}): IpcIconPack {
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

  function ipcIcon(id: string, name: string, overrides: Partial<IpcIcon> = {}): IpcIcon {
    return {
      id,
      packId,
      name,
      isAnimated: false,
      processingState: 'Ready',
      availableSizes: [128],
      createdAt: '2026-01-01T00:00:00Z',
      ...overrides,
    };
  }

  function push(method: string, event: unknown): void {
    notifications.get(method)?.next(event);
  }

  beforeEach(() => {
    notifications = new Map();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'getIconPacks',
      'getIcons',
      'createIconPack',
      'updateIconPack',
      'deleteIconPack',
      'renameIcon',
      'deleteIcon',
      'deleteIcons',
      'uploadIcons',
      'importIconsFromPath',
      'importSingleIconFromPath',
      'importIconPacks',
      'exportIconPack',
      'cancelIconImportBatch',
    ]);
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.getIconPacks.and.resolveTo({ packs: [ipcPack(packId, 'Pack A', { iconCount: 1 })] });
    apiSpy.getIcons.and.resolveTo({ icons: [ipcIcon('icon-1', 'logo')] });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(IconPackService);
  });

  it('loads packs', async () => {
    await service.loadPacks();

    expect(service.packs().length).toBe(1);
    expect(service.packs()[0].name).toBe('Pack A');
  });

  it('sets loadError when loading fails', async () => {
    apiSpy.getIconPacks.and.rejectWith(new Error('offline'));

    await service.loadPacks();

    expect(service.loadError()).not.toBeNull();
  });

  it('lazily loads the icons of a pack once', async () => {
    service.iconsFor(packId);
    await Promise.resolve();
    await Promise.resolve();

    expect(service.iconsFor(packId)().length).toBe(1);
    expect(apiSpy.getIcons).toHaveBeenCalledTimes(1);
  });

  it('creates a pack and merges it into the store', async () => {
    apiSpy.createIconPack.and.resolveTo({ success: true, pack: ipcPack(otherPackId, 'New Pack') });

    const created = await service.createPack({ name: 'New Pack' });

    expect(created?.id).toBe(otherPackId);
    expect(service.packs().some(p => p.id === otherPackId)).toBeTrue();
  });

  it('rolls back an optimistic update when the host rejects it', async () => {
    await service.loadPacks();
    apiSpy.updateIconPack.and.resolveTo({
      success: false,
      error: { code: 'ReadOnly', message: 'nope' },
    });

    const ok = await service.updatePack(packId, { name: 'Renamed' });

    expect(ok).toBeFalse();
    expect(service.packs()[0].name).toBe('Pack A');
  });

  it('rolls back an optimistic delete when the host rejects it', async () => {
    await service.loadPacks();
    apiSpy.deleteIconPack.and.resolveTo({
      success: false,
      error: { code: 'DefaultPackProtected', message: 'nope' },
    });

    const ok = await service.deletePack(packId);

    expect(ok).toBeFalse();
    expect(service.packs().length).toBe(1);
  });

  it('bulk-deletes icons optimistically and adjusts the pack count', async () => {
    apiSpy.getIcons.and.resolveTo({ icons: [ipcIcon('icon-1', 'a'), ipcIcon('icon-2', 'b'), ipcIcon('icon-3', 'c')] });
    await service.loadPacks();
    await service.loadIcons(packId);
    apiSpy.deleteIcons.and.resolveTo({ success: true, deletedCount: 2 });

    const ok = await service.deleteIcons(['icon-1', 'icon-3'], packId);

    expect(ok).toBeTrue();
    expect(service.iconsFor(packId)().map(i => i.id)).toEqual(['icon-2']);
    expect(apiSpy.deleteIcons).toHaveBeenCalledWith({ ids: ['icon-1', 'icon-3'] });
  });

  it('rolls back an optimistic bulk delete when the host rejects it', async () => {
    await service.loadIcons(packId);
    apiSpy.deleteIcons.and.resolveTo({ success: false, deletedCount: 0, error: { code: 'PackReadOnly', message: '' } });

    const ok = await service.deleteIcons(['icon-1'], packId);

    expect(ok).toBeFalse();
    expect(service.iconsFor(packId)().length).toBe(1);
  });

  it('rolls back an optimistic icon rename on failure', async () => {
    await service.loadIcons(packId);
    apiSpy.renameIcon.and.resolveTo({ success: false, error: { code: 'PackReadOnly', message: '' } });

    const ok = await service.renameIcon('icon-1', packId, 'other');

    expect(ok).toBeFalse();
    expect(service.iconsFor(packId)()[0].name).toBe('logo');
  });

  it('merges pack events into the store', async () => {
    await service.loadPacks();

    push('IconPackCreatedEvent', { pack: ipcPack(otherPackId, 'From Event') });
    expect(service.packs().length).toBe(2);

    push('IconPackUpdatedEvent', { pack: ipcPack(otherPackId, 'Renamed Event') });
    expect(service.packs().find(p => p.id === otherPackId)?.name).toBe('Renamed Event');

    push('IconPackDeletedEvent', { packId: otherPackId });
    expect(service.packs().length).toBe(1);
  });

  it('appends streamed icons without duplicating and bumps the count', async () => {
    await service.loadPacks();
    await service.loadIcons(packId);

    push('IconsAddedEvent', {
      packId,
      icons: [ipcIcon('icon-1', 'logo'), ipcIcon('icon-2', 'fresh', { processingState: 'Pending' })],
    });

    expect(service.iconsFor(packId)().length).toBe(2);
    expect(service.packs()[0].iconCount).toBe(3);
  });

  it('replaces an icon on IconUpdatedEvent', async () => {
    await service.loadIcons(packId);

    push('IconUpdatedEvent', { icon: ipcIcon('icon-1', 'logo', { processingState: 'Failed' }) });

    expect(service.iconsFor(packId)()[0].processingState).toBe('Failed');
  });

  it('removes an icon on IconDeletedEvent and decrements the count', async () => {
    await service.loadPacks();
    await service.loadIcons(packId);

    push('IconDeletedEvent', { iconId: 'icon-1', packId });

    expect(service.iconsFor(packId)().length).toBe(0);
    expect(service.packs()[0].iconCount).toBe(0);
  });

  it('tracks import progress events and dismisses batches', async () => {
    push('IconImportProgressEvent', {
      batchId: 'batch-1',
      packId,
      state: 'Processing',
      total: 10,
      processed: 4,
      failed: 1,
    });

    expect(service.activeBatches().get('batch-1')?.processed).toBe(4);

    service.dismissBatch('batch-1');

    expect(service.activeBatches().has('batch-1')).toBeFalse();
  });

  it('cancels a batch without dropping it from activeBatches', async () => {
    push('IconImportProgressEvent', {
      batchId: 'batch-1',
      packId,
      state: 'Processing',
      total: 10,
      processed: 4,
      failed: 1,
    });
    apiSpy.cancelIconImportBatch.and.resolveTo({ cancelled: true });

    const ok = await service.cancelBatch('batch-1');

    expect(ok).toBeTrue();
    expect(apiSpy.cancelIconImportBatch).toHaveBeenCalledWith('batch-1');
    expect(service.activeBatches().has('batch-1')).toBeTrue();
  });

  it('reports a failed cancel without throwing', async () => {
    apiSpy.cancelIconImportBatch.and.rejectWith(new Error('offline'));

    expect(await service.cancelBatch('batch-1')).toBeFalse();
  });

  it('tracks the batch returned by an upload', async () => {
    apiSpy.uploadIcons.and.resolveTo({
      success: true,
      batch: { id: 'batch-2', packId, state: 'Processing', processed: 0, failed: 0, total: 3 },
    });

    const batch = await service.import(packId, [new File([1 as unknown as BlobPart], 'a.png')]);

    expect(batch?.id).toBe('batch-2');
    expect(service.activeBatches().has('batch-2')).toBeTrue();
  });

  it('carries ownerKind and canDelete through mapPack, from the transport payload', async () => {
    apiSpy.getIconPacks.and.resolveTo({
      packs: [ipcPack(packId, 'Managed Pack', { ownerKind: 'Store', canDelete: false })],
    });

    await service.loadPacks();

    expect(service.packs()[0].ownerKind).toBe('Store');
    expect(service.packs()[0].canDelete).toBeFalse();
  });

  it('defaults ownerKind to User and canDelete to true when the payload omits them', async () => {
    const { ownerKind: _ownerKind, canDelete: _canDelete, ...bare } = ipcPack(packId, 'Old Host Pack');
    apiSpy.getIconPacks.and.resolveTo({ packs: [bare as IpcIconPack] });

    await service.loadPacks();

    expect(service.packs()[0].ownerKind).toBe('User');
    expect(service.packs()[0].canDelete).toBeTrue();
  });

  it('sorts the default pack first regardless of server order', async () => {
    apiSpy.getIconPacks.and.resolveTo({
      packs: [
        ipcPack(packId, 'Apple'),
        ipcPack(otherPackId, 'Zebra Default', { isDefault: true }),
      ],
    });

    await service.loadPacks();

    expect(service.packs().map(p => p.name)).toEqual(['Zebra Default', 'Apple']);
  });

  it('keeps the default pack on top when packs arrive via events', async () => {
    apiSpy.getIconPacks.and.resolveTo({
      packs: [ipcPack(otherPackId, 'Zebra Default', { isDefault: true })],
    });
    await service.loadPacks();

    push('IconPackCreatedEvent', { pack: ipcPack(packId, 'Aardvark') });

    expect(service.packs().map(p => p.name)).toEqual(['Zebra Default', 'Aardvark']);
  });

  it('imports pack files, upserts restored packs and tracks the batch', async () => {
    apiSpy.importIconPacks.and.resolveTo({
      success: true,
      packs: [ipcPack(otherPackId, 'Restored Pack', { sourceType: 'MacroDeckImport', iconCount: 3 })],
      batch: { id: 'batch-3', packId, state: 'Discovering', processed: 0, failed: 0 },
    });
    const files = [new File([1 as unknown as BlobPart], 'pack.macroDeckIconPack')];

    const ok = await service.importPacks(files);

    expect(ok).toBeTrue();
    expect(apiSpy.importIconPacks).toHaveBeenCalledWith(files);
    expect(service.packs().find(p => p.id === otherPackId)?.name).toBe('Restored Pack');
    expect(service.activeBatches().has('batch-3')).toBeTrue();
  });

  it('reports a failed pack import', async () => {
    apiSpy.importIconPacks.and.resolveTo({
      success: false,
      error: { code: 'InvalidArchive', message: 'broken' },
    });

    const ok = await service.importPacks([new File([1 as unknown as BlobPart], 'x.zip')]);

    expect(ok).toBeFalse();
    expect(service.packs().length).toBe(0);
  });

  it('exports a pack through the file-save service', async () => {
    apiSpy.exportIconPack.and.resolveTo({
      blob: new Blob(['zip']),
      fileName: 'Pack A.macroDeckIconPack',
    });
    spyOn(URL, 'createObjectURL').and.returnValue('blob:test');
    spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    const result = await service.exportPack(packId);

    expect(result).toEqual({ ok: true, fileName: 'Pack A.macroDeckIconPack', path: undefined });
    expect(apiSpy.exportIconPack).toHaveBeenCalledWith(packId);
    expect(clickSpy).toHaveBeenCalled();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test');
  });

  it('reports a dismissed save dialog as canceled, not as a failure', async () => {
    apiSpy.exportIconPack.and.resolveTo({
      blob: new Blob(['zip']),
      fileName: 'Pack A.macroDeckIconPack',
    });
    spyOn(TestBed.inject(FileSaveService), 'save').and.resolveTo({ status: 'canceled' });

    const result = await service.exportPack(packId);

    expect(result).toEqual({ ok: true, canceled: true, fileName: 'Pack A.macroDeckIconPack' });
  });

  it('reports a failed export', async () => {
    apiSpy.exportIconPack.and.rejectWith(new Error('404'));

    const result = await service.exportPack(packId);

    expect(result.ok).toBeFalse();
    expect(result.error).toBe('404');
  });

  describe('importSingleFromPath', () => {
    it('returns the created icon and omits the pack id when targeting the default pack', async () => {
      apiSpy.importSingleIconFromPath.and.resolveTo({
        success: true,
        icon: ipcIcon('icon-9', 'Spotify', { processingState: 'Pending' }),
      });

      const icon = await service.importSingleFromPath(null, '/Applications/Spotify.app');

      expect(apiSpy.importSingleIconFromPath)
        .toHaveBeenCalledWith({ packId: undefined, path: '/Applications/Spotify.app' });
      expect(icon).toEqual(jasmine.objectContaining({ id: 'icon-9', processingState: 'Pending' }));
    });

    it('returns null when the host rejects the source', async () => {
      apiSpy.importSingleIconFromPath.and.resolveTo({
        success: false,
        error: { code: 'UnsupportedFormat', message: 'nope' },
      });

      expect(await service.importSingleFromPath(null, '/tmp/notes.txt')).toBeNull();
    });

    it('returns null when the request throws', async () => {
      apiSpy.importSingleIconFromPath.and.rejectWith(new Error('offline'));

      expect(await service.importSingleFromPath(packId, '/tmp/logo.png')).toBeNull();
    });
  });

  describe('whenIconReady', () => {
    it('resolves when the icon reports Ready', async () => {
      const pending = service.whenIconReady('icon-7');

      push('IconUpdatedEvent', { icon: ipcIcon('icon-7', 'logo', { processingState: 'Ready' }) });

      expect(await pending).toEqual(jasmine.objectContaining({ id: 'icon-7', processingState: 'Ready' }));
    });

    it('resolves with the failure so the caller can report it', async () => {
      const pending = service.whenIconReady('icon-7');

      push('IconUpdatedEvent', {
        icon: ipcIcon('icon-7', 'logo', { processingState: 'Failed', processingError: 'broken' }),
      });

      expect(await pending).toEqual(jasmine.objectContaining({ processingState: 'Failed' }));
    });

    // The icon was just created in a pack whose icons were never browsed, so the waiter must not
    // depend on that pack being loaded.
    it('resolves for a pack whose icons were never loaded', async () => {
      const pending = service.whenIconReady('icon-7');

      push('IconUpdatedEvent', {
        icon: ipcIcon('icon-7', 'logo', { packId: otherPackId, processingState: 'Ready' }),
      });

      expect(await pending).not.toBeNull();
    });

    it('keeps waiting while the icon is still being processed', async () => {
      let settled = false;
      void service.whenIconReady('icon-7').then(() => (settled = true));

      push('IconUpdatedEvent', { icon: ipcIcon('icon-7', 'logo', { processingState: 'Processing' }) });
      await Promise.resolve();

      expect(settled).toBeFalse();
    });

    it('resolves null when nothing arrives before the timeout', async () => {
      jasmine.clock().install();
      const pending = service.whenIconReady('icon-7', 100);
      jasmine.clock().tick(101);
      jasmine.clock().uninstall();

      expect(await pending).toBeNull();
    });
  });
});
