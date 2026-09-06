import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { MigrationImportResponse, MigrationPreviewResponse } from '@macro-deck/runtime';
import { ApiService, ProfileService } from '@shared';
import { MigrationService } from './migration.service';
import { provideLocalizationTesting } from '../../testing/localization-test-support';

describe('MigrationService', () => {
  let service: MigrationService;
  let api: jasmine.SpyObj<ApiService>;

  const successPreview: MigrationPreviewResponse = {
    success: true,
    summary: {
      sourceId: 'macrodeck2',
      sourceName: 'Macro Deck 2',
      profileCount: 1,
      folderCount: 0,
      widgetCount: 0,
      iconCount: 0,
      variableCount: 0,
      migratedActionCount: 0,
      unsupportedActionCount: 0,
      credentialStatus: 'NotPresent',
      integrations: [],
      unsupportedActions: [],
      warnings: [],
    },
  };

  const successImport: MigrationImportResponse = { ...successPreview, profileIds: ['p1'] };

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'previewMigration',
      'importMigration',
      'previewMigrationUpload',
      'importMigrationUpload',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api },
        { provide: ProfileService, useValue: { loadProfiles: () => Promise.resolve() } },
      ],
    });
    service = TestBed.inject(MigrationService);
  });

  it('sends a picked path to the path endpoint', async () => {
    api.previewMigration.and.resolveTo(successPreview);

    await service.preview({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      skipDecryption: false,
    });

    expect(api.previewMigration).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      path: '/data/macrodeck2',
      decryptionKey: undefined,
      skipDecryption: false,
    });
    expect(api.previewMigrationUpload).not.toHaveBeenCalled();
  });

  it('sends an uploaded file to the upload endpoint', async () => {
    const file = new File(['zip-bytes'], 'backup.zip');
    api.previewMigrationUpload.and.resolveTo(successPreview);

    await service.preview({
      sourceId: 'macrodeck2',
      input: { kind: 'file', file },
      decryptionKey: 'the-key',
      skipDecryption: true,
    });

    expect(api.previewMigrationUpload).toHaveBeenCalledWith(file, 'macrodeck2', 'the-key', true);
    expect(api.previewMigration).not.toHaveBeenCalled();
  });

  it('imports a picked path through the path endpoint', async () => {
    api.importMigration.and.resolveTo(successImport);

    await service.import({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      skipDecryption: false,
    });

    expect(api.importMigration).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      path: '/data/macrodeck2',
      decryptionKey: undefined,
      skipDecryption: false,
    });
    expect(api.importMigrationUpload).not.toHaveBeenCalled();
  });

  it('imports an uploaded file through the upload endpoint', async () => {
    const file = new File(['zip-bytes'], 'backup.zip');
    api.importMigrationUpload.and.resolveTo(successImport);

    await service.import({
      sourceId: 'macrodeck2',
      input: { kind: 'file', file },
      skipDecryption: false,
    });

    expect(api.importMigrationUpload).toHaveBeenCalledWith(file, 'macrodeck2', undefined, false);
    expect(api.importMigration).not.toHaveBeenCalled();
  });
});
