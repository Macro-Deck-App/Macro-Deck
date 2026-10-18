import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ArchiveSummary, IpcFolder, IpcProfile, IpcWidget } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ProfileService } from '@shared';
import { FileSaveService } from './file-save.service';
import { PortabilityService, fileSource, pathSource } from './portability.service';

describe('PortabilityService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let profileSpy: jasmine.SpyObj<ProfileService>;
  let service: PortabilityService;

  beforeEach(() => {
    localStorage.clear();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'exportProfile',
      'importProfile',
      'importProfileFromPath',
      'exportFolder',
      'importFolder',
      'importFolderFromPath',
      'exportWidgets',
      'importWidgets',
      'importWidgetsFromPath',
      'inspectArchive',
      'inspectArchivePath',
      'onNotification',
      'getLocalization',
    ]);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    profileSpy = jasmine.createSpyObj<ProfileService>('ProfileService', ['loadProfiles', 'selectProfile']);
    profileSpy.loadProfiles.and.resolveTo();

    spyOn(URL, 'createObjectURL').and.returnValue('blob:test');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        PortabilityService,
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: profileSpy },
      ],
    });
    service = TestBed.inject(PortabilityService);
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('dir');
    document.documentElement.lang = 'en';
  });

  function diagnosticResponse(location: string) {
    return JSON.parse(JSON.stringify({
      success: false,
      error: {
        code: 'InvalidJson',
        message: {
          $localized: {
            scope: 'macrodeck.app',
            key: 'Errors.Portability.InvalidJson',
            arguments: { location },
          },
        },
      },
    }));
  }

  for (const kind of ['profile', 'folder', 'widgets'] as const) {
    it(`resolves a localized ${kind} import error from the wire before showing it`, async () => {
      const location = 'content.json:$.profile.defaultRows:1:153';
      const response = diagnosticResponse(location);
      apiSpy.importProfile.and.resolveTo(response);
      apiSpy.importFolder.and.resolveTo(response);
      apiSpy.importWidgets.and.resolveTo(response);
      const source = fileSource(new File(['x'], 'sample'));

      const outcome = kind === 'profile' ? await service.importProfile(source)
        : kind === 'folder' ? await service.importFolder('pid', null, source)
        : await service.importWidgets('fid', 0, 0, source);

      expect(outcome).toEqual({
        status: 'error',
        message: `Invalid JSON data at ${location}. Check the field type and syntax.`,
      });
    });
  }

  it('resolves localized inspection errors as readable text', async () => {
    const location = 'manifest.json:$.kind:1:20';
    apiSpy.inspectArchive.and.resolveTo(diagnosticResponse(location));

    const outcome = await service.inspectArchive(fileSource(new File(['x'], 'sample')));

    expect(outcome).toEqual({
      status: 'error',
      message: `Invalid JSON data at ${location}. Check the field type and syntax.`,
    });
  });

  it('uses the client language and preserves the import field coordinates', async () => {
    const location = 'content.json:$.profile.defaultRows:1:153';
    apiSpy.getLocalization.and.resolveTo({
      culture: 'fr',
      fallbackCulture: 'en',
      followSystem: false,
      availableCultures: ['en', 'fr'],
      translations: {
        'macrodeck.app:Errors.Portability.InvalidJson':
          'Données JSON invalides à {location}. Vérifie le type du champ et la syntaxe.',
      },
    });
    await TestBed.inject(LocalizationService).loadFromHost();
    apiSpy.importProfile.and.resolveTo(diagnosticResponse(location));

    const outcome = await service.importProfile(fileSource(new File(['x'], 'sample')));

    expect(outcome).toEqual({
      status: 'error',
      message: `Données JSON invalides à ${location}. Vérifie le type du champ et la syntaxe.`,
    });
  });

  it('exportProfile forwards the password only when including secrets', async () => {
    apiSpy.exportProfile.and.resolveTo({ blob: new Blob(['zip']), fileName: 'p.macroDeckProfile' });

    const result = await service.exportProfile('pid', { includeSecrets: true, password: 'pw' });

    expect(result.ok).toBeTrue();
    expect(result.fileName).toBe('p.macroDeckProfile');
    expect(apiSpy.exportProfile)
      .toHaveBeenCalledWith('pid', { includeIcons: true, includeSecrets: true, password: 'pw' });
  });

  it('exportProfile reports the location the shell saved the archive to', async () => {
    apiSpy.exportProfile.and.resolveTo({ blob: new Blob(['zip']), fileName: 'p.macroDeckProfile' });
    spyOn(TestBed.inject(FileSaveService), 'save').and.resolveTo({
      status: 'saved',
      path: '/tmp/p.macroDeckProfile',
      viaDialog: true,
    });

    const result = await service.exportProfile('pid', { includeSecrets: false });

    expect(result).toEqual({ ok: true, fileName: 'p.macroDeckProfile', path: '/tmp/p.macroDeckProfile' });
  });

  it('exportProfile reports a dismissed save dialog as canceled, not as a failure', async () => {
    apiSpy.exportProfile.and.resolveTo({ blob: new Blob(['zip']), fileName: 'p.macroDeckProfile' });
    spyOn(TestBed.inject(FileSaveService), 'save').and.resolveTo({ status: 'canceled' });

    const result = await service.exportProfile('pid', { includeSecrets: false });

    expect(result).toEqual({ ok: true, canceled: true, fileName: 'p.macroDeckProfile' });
  });

  it('exportWidgets reports a failed write as an error', async () => {
    apiSpy.exportWidgets.and.resolveTo({ blob: new Blob(['zip']), fileName: 'w.macroDeckWidget' });
    spyOn(TestBed.inject(FileSaveService), 'save').and.resolveTo({ status: 'error', message: 'disk full' });

    const result = await service.exportWidgets('fid', ['wid'], { includeSecrets: false });

    expect(result).toEqual({ ok: false, error: 'disk full' });
  });

  it('exportProfile drops the password when not including secrets', async () => {
    apiSpy.exportProfile.and.resolveTo({ blob: new Blob(['zip']), fileName: 'p.macroDeckProfile' });

    await service.exportProfile('pid', { includeSecrets: false, password: 'ignored' });

    expect(apiSpy.exportProfile)
      .toHaveBeenCalledWith('pid', { includeIcons: true, includeSecrets: false, password: undefined });
  });

  it('exportProfile forwards a declined icon bundle, and defaults to bundling them', async () => {
    apiSpy.exportProfile.and.resolveTo({ blob: new Blob(['zip']), fileName: 'p.macroDeckProfile' });

    await service.exportProfile('pid', { includeSecrets: false, includeIcons: false });

    expect(apiSpy.exportProfile)
      .toHaveBeenCalledWith('pid', { includeIcons: false, includeSecrets: false, password: undefined });
  });

  it('inspectArchive answers with what the archive holds', async () => {
    const archive = { kind: 'Profile', name: 'Streaming' } as ArchiveSummary;
    apiSpy.inspectArchive.and.resolveTo({ success: true, archive });

    const outcome = await service.inspectArchive(fileSource(new File(['x'], 'p.macroDeckProfile')));

    expect(outcome).toEqual({ status: 'success', archive });
  });

  it('inspectArchive reports a file the host cannot read', async () => {
    apiSpy.inspectArchive.and.resolveTo({ success: false, error: { code: 'InvalidArchive', message: 'not an archive' } });

    const outcome = await service.inspectArchive(fileSource(new File(['x'], 'p')));

    expect(outcome).toEqual({ status: 'error', message: 'not an archive' });
  });

  it('exportProfile reports an error when the API throws', async () => {
    apiSpy.exportProfile.and.rejectWith(new Error('boom'));

    const result = await service.exportProfile('pid', { includeSecrets: false });

    expect(result).toEqual({ ok: false, error: 'boom' });
  });

  it('importProfile selects the new profile on success', async () => {
    apiSpy.importProfile.and.resolveTo({ success: true, profile: { id: 'new' } as IpcProfile });

    const outcome = await service.importProfile(fileSource(new File(['x'], 'p.macroDeckProfile')));

    expect(outcome.status).toBe('success');
    expect(profileSpy.loadProfiles).toHaveBeenCalled();
    expect(profileSpy.selectProfile).toHaveBeenCalledWith('new');
  });

  it('importProfile maps a PasswordRequired error', async () => {
    apiSpy.importProfile.and.resolveTo({ success: false, error: { code: 'PasswordRequired', message: '' } });

    const outcome = await service.importProfile(fileSource(new File(['x'], 'p')));

    expect(outcome.status).toBe('passwordRequired');
  });

  it('importProfile maps an InvalidPassword error', async () => {
    apiSpy.importProfile.and.resolveTo({ success: false, error: { code: 'InvalidPassword', message: '' } });

    const outcome = await service.importProfile(fileSource(new File(['x'], 'p')), 'wrong');

    expect(outcome.status).toBe('invalidPassword');
  });

  it('exportFolder forwards the subfolder choice and the password', async () => {
    apiSpy.exportFolder.and.resolveTo({ blob: new Blob(['zip']), fileName: 'f.macroDeckFolder' });

    const result = await service.exportFolder('fid', {
      includeSubfolders: true,
      includeSecrets: true,
      password: 'pw',
    });

    expect(result.fileName).toBe('f.macroDeckFolder');
    expect(apiSpy.exportFolder).toHaveBeenCalledWith('fid', {
      includeSubfolders: true,
      includeIcons: true,
      includeSecrets: true,
      password: 'pw',
    });
  });

  it('exportFolder defaults the subfolder choice to false when the dialog did not offer it', async () => {
    apiSpy.exportFolder.and.resolveTo({ blob: new Blob(['zip']), fileName: 'f.macroDeckFolder' });

    await service.exportFolder('fid', { includeSecrets: false });

    expect(apiSpy.exportFolder).toHaveBeenCalledWith('fid', {
      includeSubfolders: false,
      includeIcons: true,
      includeSecrets: false,
      password: undefined,
    });
  });

  it('importFolder returns the imported folder id and count on success', async () => {
    apiSpy.importFolder.and.resolveTo({
      success: true,
      folder: { id: 'imported' } as IpcFolder,
      folderCount: 3,
    });

    const outcome = await service.importFolder('pid', 'parent', fileSource(new File(['x'], 'f.macroDeckFolder')));

    expect(outcome).toEqual({ status: 'success', count: 3, folderId: 'imported' });
    expect(apiSpy.importFolder).toHaveBeenCalledWith('pid', 'parent', jasmine.any(File), undefined);
  });

  it('importFolder maps a PasswordRequired error', async () => {
    apiSpy.importFolder.and.resolveTo({
      success: false,
      error: { code: 'PasswordRequired', message: '' },
      folderCount: 0,
    });

    const outcome = await service.importFolder('pid', null, fileSource(new File(['x'], 'f')));

    expect(outcome.status).toBe('passwordRequired');
  });

  it('importWidgets returns the created count on success', async () => {
    apiSpy.importWidgets.and.resolveTo({ success: true, widgets: [{}, {}] as IpcWidget[] });

    const outcome = await service.importWidgets('fid', 1, 2, fileSource(new File(['x'], 'b')));

    expect(outcome).toEqual({ status: 'success', count: 2 });
    expect(apiSpy.importWidgets).toHaveBeenCalledWith('fid', 1, 2, jasmine.any(File), undefined);
  });

  it('a path source takes the path route for every kind', async () => {
    apiSpy.inspectArchivePath.and.resolveTo({ success: true, archive: {} as ArchiveSummary });
    apiSpy.importProfileFromPath.and.resolveTo({ success: true, profile: { id: 'new' } as IpcProfile });
    apiSpy.importFolderFromPath.and.resolveTo({ success: true, folder: { id: 'f' } as IpcFolder, folderCount: 1 });
    apiSpy.importWidgetsFromPath.and.resolveTo({ success: true, widgets: [{}] as IpcWidget[] });

    await service.inspectArchive(pathSource('/tmp/a.macroDeckProfile'));
    await service.importProfile(pathSource('/tmp/a.macroDeckProfile'));
    await service.importFolder('pid', 'parent', pathSource('/tmp/a.macroDeckFolder'), 'pw');
    await service.importWidgets('fid', 3, 4, pathSource('/tmp/a.macroDeckWidget'));

    expect(apiSpy.inspectArchivePath).toHaveBeenCalledWith('/tmp/a.macroDeckProfile');
    expect(apiSpy.importProfileFromPath).toHaveBeenCalledWith('/tmp/a.macroDeckProfile', undefined);
    expect(apiSpy.importFolderFromPath).toHaveBeenCalledWith('pid', 'parent', '/tmp/a.macroDeckFolder', 'pw');
    expect(apiSpy.importWidgetsFromPath).toHaveBeenCalledWith('fid', 3, 4, '/tmp/a.macroDeckWidget', undefined);
    expect(apiSpy.importProfile).not.toHaveBeenCalled();
  });

  it('a path import still drives the password prompt', async () => {
    apiSpy.importProfileFromPath.and.resolveTo({ success: false, error: { code: 'InvalidPassword', message: '' } });

    const outcome = await service.importProfile(pathSource('/tmp/a.macroDeckProfile'), 'wrong');

    expect(outcome.status).toBe('invalidPassword');
  });
});
