import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ArchiveSummary, FolderMoveDirection, FolderMoveRequest } from '@macro-deck/runtime';
import { FolderViewService, ProfileService, ToastService } from '@shared';
import { FolderFocusRuleService } from '../../../services/folder-focus-rule.service';
import { PortabilityService } from '../../../services/portability.service';
import { FileOpenService, FolderService } from '../../../services';
import { FolderNavigationComponent } from './folder-navigation.component';
import type { FolderContextMenuAction } from './folder-context-menu/folder-context-menu.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const archiveSummary: ArchiveSummary = {
  kind: 'Folder',
  name: 'Lights',
  appVersion: '3.0.0',
  createdAt: '2026-07-30T10:00:00Z',
  encrypted: false,
  includesSecrets: false,
  folderCount: 2,
  widgetCount: 3,
  iconCount: 1,
  scriptCount: 0,
  variableCount: 0,
  secretCount: 0,
  integrations: [],
};

describe('FolderNavigationComponent export/import', () => {
  let folderStub: Record<string, jasmine.Spy>;
  let portability: jasmine.SpyObj<PortabilityService>;
  let toasts: jasmine.SpyObj<ToastService>;
  let children: string[];

  function createComponent(): FolderNavigationComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'pid' } },
        { provide: PortabilityService, useValue: portability },
        { provide: ToastService, useValue: toasts },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: FolderFocusRuleService, useValue: { rulesForFolder: () => [] } },
      ],
    });
    return TestBed.createComponent(FolderNavigationComponent).componentInstance;
  }

  beforeEach(() => {
    children = [];
    folderStub = {
      rootFolders: jasmine.createSpy('rootFolders').and.returnValue([]),
      contextMenu: jasmine.createSpy('contextMenu').and.returnValue({ isOpen: true, folderId: 'ctx', x: 0, y: 0 }),
      selectedFolderId: jasmine.createSpy('selectedFolderId').and.returnValue('ctx'),
      getChildFolders: jasmine.createSpy('getChildFolders').and.callFake(() => children),
      getFolderById: jasmine.createSpy('getFolderById').and.returnValue({ id: 'ctx', parentId: null, isDefault: true }),
      canDeleteFolder: jasmine.createSpy('canDeleteFolder').and.returnValue(true),
      deletionScope: jasmine.createSpy('deletionScope').and.returnValue(null),
      deleteFolder: jasmine.createSpy('deleteFolder').and.resolveTo({ success: true }),
      closeContextMenu: jasmine.createSpy('closeContextMenu'),
      selectFolder: jasmine.createSpy('selectFolder'),
      expandFolder: jasmine.createSpy('expandFolder'),
      toggleFolderExpanded: jasmine.createSpy('toggleFolderExpanded'),
      resolveMove: jasmine.createSpy('resolveMove').and.returnValue(null),
      moveFolder: jasmine.createSpy('moveFolder').and.resolveTo({ success: true }),
    };
    portability = jasmine.createSpyObj<PortabilityService>('PortabilityService',
      ['exportFolder', 'importFolder', 'inspectArchive']);
    portability.inspectArchive.and.resolveTo({ status: 'success', archive: archiveSummary });
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
  });

  const moveActions: Array<[FolderContextMenuAction, FolderMoveDirection]> = [
    ['move-up', 'up'],
    ['move-down', 'down'],
    ['move-into', 'into'],
    ['move-to-parent', 'out'],
  ];

  for (const [action, direction] of moveActions) {
    it(`the ${action} context action resolves '${direction}' and forwards it to moveFolder`, async () => {
      const request: FolderMoveRequest = { folderId: 'ctx', targetId: 'sibling', position: 'before' };
      folderStub['resolveMove'].and.returnValue(request);
      const component = createComponent();

      await component.onContextMenuAction(action);

      expect(folderStub['resolveMove']).toHaveBeenCalledWith('ctx', direction);
      expect(folderStub['moveFolder']).toHaveBeenCalledWith(request);
    });
  }

  it('does nothing when resolveMove refuses the move (e.g. the list edge)', async () => {
    folderStub['resolveMove'].and.returnValue(null);
    const component = createComponent();

    await component.onContextMenuAction('move-up');

    expect(folderStub['moveFolder']).not.toHaveBeenCalled();
  });

  it('a failed move surfaces an error toast', async () => {
    const request: FolderMoveRequest = { folderId: 'ctx', targetId: 'sibling', position: 'after' };
    folderStub['resolveMove'].and.returnValue(request);
    folderStub['moveFolder'].and.resolveTo({
      success: false,
      error: { code: 'InvalidParent', message: 'Cannot move a folder into its own descendant' },
    });
    const component = createComponent();

    await component.onContextMenuAction('move-down');

    expect(toasts.show).toHaveBeenCalledWith('Cannot move a folder into its own descendant', { variant: 'error' });
  });

  it('the export context action targets the folder the menu was opened on', async () => {
    const component = createComponent();

    await component.onContextMenuAction('export');

    expect(component['exportFolderId']()).toBe('ctx');
  });

  it('offers the subfolder choice only for a folder that has subfolders', async () => {
    const component = createComponent();

    await component.onContextMenuAction('export');
    expect(component.exportToggles().map(toggle => toggle.key)).toEqual(['includeIcons']);

    children = ['child'];
    expect(component.exportToggles().map(toggle => toggle.key)).toEqual(['includeSubfolders', 'includeIcons']);
  });

  it('a confirmed export forwards the options and confirms with a toast', async () => {
    portability.exportFolder.and.resolveTo({ ok: true, fileName: 'Lights.macroDeckFolder' });
    const component = createComponent();
    await component.onContextMenuAction('export');

    await component.onExportConfirmed({ includeSecrets: false, includeIcons: false, includeSubfolders: true });

    expect(portability.exportFolder)
      .toHaveBeenCalledWith('ctx', { includeSecrets: false, includeIcons: false, includeSubfolders: true });
    expect(toasts.show).toHaveBeenCalledWith('Folder exported', { detail: 'Downloaded Lights.macroDeckFolder' });
    expect(component['exportFolderId']()).toBeNull();
  });

  it('a dismissed save dialog is not reported as a failure', async () => {
    portability.exportFolder.and.resolveTo({ ok: true, canceled: true, fileName: 'Lights.macroDeckFolder' });
    const component = createComponent();
    await component.onContextMenuAction('export');

    await component.onExportConfirmed({ includeSecrets: false });

    expect(toasts.show).not.toHaveBeenCalled();
    expect(component['portableError']()).toBeNull();
  });

  it('a failed export surfaces in the error banner', async () => {
    portability.exportFolder.and.resolveTo({ ok: false, error: 'disk full' });
    const component = createComponent();
    await component.onContextMenuAction('export');

    await component.onExportConfirmed({ includeSecrets: false });

    expect(component['portableError']()).toBe('disk full');
  });

  it('importing inside a folder puts the archive below it, then reveals and selects it', async () => {
    portability.importFolder.and.resolveTo({ status: 'success', count: 2, folderId: 'imported' });
    const component = createComponent();

    await component.onContextMenuAction('import-inside');
    await selectFile(component, 'tree.macroDeckFolder');
    await confirmPreview(component);

    expect(portability.importFolder).toHaveBeenCalledWith('pid', 'ctx', { kind: 'file', file: jasmine.any(File) }, undefined);
    expect(folderStub['expandFolder']).toHaveBeenCalledWith('ctx');
    expect(folderStub['selectFolder']).toHaveBeenCalledWith('imported');
    expect(toasts.show).toHaveBeenCalledWith('2 folders imported');
  });

  it('the footer import goes to the profile root, with no parent folder', async () => {
    portability.importFolder.and.resolveTo({ status: 'success', count: 1, folderId: 'imported' });
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');
    await confirmPreview(component);

    expect(portability.importFolder).toHaveBeenCalledWith('pid', null, { kind: 'file', file: jasmine.any(File) }, undefined);
    expect(folderStub['expandFolder']).not.toHaveBeenCalled();
    expect(toasts.show).toHaveBeenCalledWith('Folder imported');
  });

  it('an encrypted archive prompts for the password and retries with it', async () => {
    portability.importFolder.and.resolveTo({ status: 'passwordRequired' });
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');
    await confirmPreview(component);

    expect(component['archiveImport'].passwordVisible()).toBeTrue();
    expect(component['archiveImport'].passwordInvalid()).toBeFalse();

    portability.importFolder.and.resolveTo({ status: 'success', count: 1, folderId: 'imported' });
    await component['archiveImport'].submitPassword('pw');

    expect(portability.importFolder).toHaveBeenCalledWith('pid', null, { kind: 'file', file: jasmine.any(File) }, 'pw');
    expect(component['archiveImport'].passwordVisible()).toBeFalse();
  });

  it('a wrong password re-prompts and marks the field invalid', async () => {
    portability.importFolder.and.resolveTo({ status: 'invalidPassword' });
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');
    await confirmPreview(component);

    expect(component['archiveImport'].passwordVisible()).toBeTrue();
    expect(component['archiveImport'].passwordInvalid()).toBeTrue();
  });

  it('a failed import surfaces in the error banner', async () => {
    portability.importFolder.and.resolveTo({ status: 'error', message: 'not a valid archive' });
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');
    await confirmPreview(component);

    expect(component['portableError']()).toBe('not a valid archive');
  });

  it('previews the archive before importing, and imports nothing if the preview is dismissed', async () => {
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');

    expect(component['archiveImport'].summary()).toEqual(archiveSummary);
    expect(portability.importFolder).not.toHaveBeenCalled();

    component['archiveImport'].cancel();
    await confirmPreview(component);

    expect(portability.importFolder).not.toHaveBeenCalled();
  });

  it('an unreadable file is reported without opening the preview', async () => {
    portability.inspectArchive.and.resolveTo({ status: 'error', message: 'not a valid archive' });
    const component = createComponent();

    component.startImport(null);
    await selectFile(component, 'tree.macroDeckFolder');

    expect(component['archiveImport'].summary()).toBeNull();
    expect(component['portableError']()).toBe('not a valid archive');
  });

  function confirmPreview(component: FolderNavigationComponent): Promise<void> {
    return component['archiveImport'].confirm();
  }

  async function selectFile(component: FolderNavigationComponent, name: string): Promise<void> {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['zip'], name)] });
    await component.onImportFileSelected({ target: input } as unknown as Event);
  }
});

describe('FolderNavigationComponent delete confirmation', () => {
  let folderStub: Record<string, jasmine.Spy>;
  let toasts: jasmine.SpyObj<ToastService>;

  function createComponent(): FolderNavigationComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FolderService, useValue: folderStub },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'pid' } },
        { provide: PortabilityService, useValue: {} },
        { provide: ToastService, useValue: toasts },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: FolderFocusRuleService, useValue: { rulesForFolder: () => [] } },
      ],
    });
    return TestBed.createComponent(FolderNavigationComponent).componentInstance;
  }

  async function startDelete(component: FolderNavigationComponent): Promise<void> {
    await component.onContextMenuAction('delete');
  }

  beforeEach(() => {
    folderStub = {
      contextMenu: jasmine.createSpy('contextMenu').and.returnValue({ isOpen: true, folderId: 'ctx', x: 0, y: 0 }),
      deletionScope: jasmine.createSpy('deletionScope').and.returnValue(null),
      deleteFolder: jasmine.createSpy('deleteFolder').and.resolveTo({ success: true }),
    };
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
  });

  it('falls back to the static message when the folder is unknown to deletionScope', async () => {
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe(
      'Are you sure you want to delete this folder and all its contents? This action cannot be undone.'
    );
  });

  it('names the folder with no extra counts for an empty leaf', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 0, widgetCount: 0, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe('Delete "Games"? This action cannot be undone.');
  });

  it('pluralizes a widget-only subtree', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 0, widgetCount: 18, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe('Delete "Games" and 18 widgets? This action cannot be undone.');
  });

  it('singularizes a single widget', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 0, widgetCount: 1, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe('Delete "Games" and 1 widget? This action cannot be undone.');
  });

  it('pluralizes a subfolder-only subtree', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 4, widgetCount: 0, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe('Delete "Games" and 4 subfolders? This action cannot be undone.');
  });

  it('singularizes a single subfolder', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 1, widgetCount: 0, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe('Delete "Games" and 1 subfolder? This action cannot be undone.');
  });

  it('uses a serial comma for the full subfolders-and-widgets shape', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 4, widgetCount: 18, pinnedCount: 0 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']())
      .toBe('Delete "Games", 4 subfolders, and 18 widgets? This action cannot be undone.');
  });

  it('adds the pinned clause when the subtree contains pinned widgets', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 0, widgetCount: 3, pinnedCount: 2 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe(
      'Delete "Games" and 3 widgets? 2 of them are pinned and will be removed from all folders. ' +
      'This action cannot be undone.'
    );
  });

  it('singularizes the pinned clause for exactly one pinned widget', async () => {
    folderStub['deletionScope'].and.returnValue({ name: 'Games', subfolderCount: 0, widgetCount: 1, pinnedCount: 1 });
    const component = createComponent();
    await startDelete(component);

    expect(component['deleteMessage']()).toBe(
      'Delete "Games" and 1 widget? 1 of them is pinned and will be removed from all folders. ' +
      'This action cannot be undone.'
    );
  });

  it('shows an error toast when the host rejects the delete', async () => {
    folderStub['deleteFolder'].and.resolveTo({ success: false, error: { code: 'INTERNAL_ERROR', message: 'boom' } });
    const component = createComponent();
    await startDelete(component);

    await component.confirmFolderDelete();

    expect(toasts.show).toHaveBeenCalledWith('boom', { variant: 'error' });
  });

  it('shows no toast when the delete succeeds', async () => {
    folderStub['deleteFolder'].and.resolveTo({ success: true });
    const component = createComponent();
    await startDelete(component);

    await component.confirmFolderDelete();

    expect(toasts.show).not.toHaveBeenCalled();
    expect(component['folderToDelete']()).toBeNull();
  });
});

describe('FolderNavigationComponent footer', () => {
  function createFixture(): ComponentFixture<FolderNavigationComponent> {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: FolderService,
          useValue: {
            rootFolders: () => [],
            contextMenu: () => ({ isOpen: false, folderId: null, x: 0, y: 0 }),
            selectedFolderId: () => null,
            getChildFolders: () => [],
          },
        },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'pid' } },
        { provide: PortabilityService, useValue: {} },
        { provide: ToastService, useValue: jasmine.createSpyObj<ToastService>('ToastService', ['show']) },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: FolderFocusRuleService, useValue: { rulesForFolder: () => [] } },
      ],
    });
    const fixture = TestBed.createComponent(FolderNavigationComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('pairs a wide create button with a labelled icon-only import button', () => {
    const footer: HTMLElement = createFixture().nativeElement.querySelector('.folder-nav-footer');

    const buttons = footer.querySelectorAll('shared-button');
    expect(buttons.length).toBe(2);
    expect(buttons[0].textContent).toContain('New Folder');
    expect(buttons[1].textContent?.trim()).toBe('');
    expect(buttons[1].querySelector('button')?.getAttribute('aria-label'))
      .toBe('Import folder from a .macroDeckFolder file');
  });

  it('marks the import action with the download icon', () => {
    const footer: HTMLElement = createFixture().nativeElement.querySelector('.folder-nav-footer');

    expect(footer.querySelector('.icon-download')).not.toBeNull();
    expect(footer.querySelector('.icon-upload')).toBeNull();
  });
});

describe('FolderNavigationComponent change-view availability', () => {
  const grid = { id: 'macrodeck.widget-grid', providerId: '', navigation: 'default', hasConfiguration: false, isBuiltIn: true };
  const dashboard = { id: 'com.example.home::dashboard', providerId: 'com.example.home', navigation: 'default', hasConfiguration: false, isBuiltIn: false };

  function createComponent(views: unknown[], customFolderViewsSupported: boolean): FolderNavigationComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: FolderService,
          useValue: {
            rootFolders: () => [],
            contextMenu: () => ({ isOpen: false, folderId: null, x: 0, y: 0 }),
            selectedFolderId: () => null,
            getChildFolders: () => [],
            getFolderById: () => null,
            canDeleteFolder: () => true,
            deletionScope: () => null,
            resolveMove: () => null,
            closeContextMenu: () => undefined,
            customFolderViewsSupported: () => customFolderViewsSupported,
          },
        },
        { provide: ProfileService, useValue: { isCurrentProfileLocked: () => false, selectedProfileId: () => 'pid' } },
        { provide: PortabilityService, useValue: jasmine.createSpyObj('PortabilityService', ['exportFolder']) },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
        { provide: FolderFocusRuleService, useValue: { rulesForFolder: () => [] } },
        { provide: FolderViewService, useValue: { folderViews: () => views, load: () => Promise.resolve() } },
      ],
    });
    return TestBed.createComponent(FolderNavigationComponent).componentInstance;
  }

  function canChangeView(component: FolderNavigationComponent): boolean {
    return (component as unknown as { canChangeView: () => boolean }).canChangeView();
  }

  it('is offered when a second view exists and the device can render one', () => {
    expect(canChangeView(createComponent([grid, dashboard], true))).toBeTrue();
  });

  it('is withheld when the widget grid is the only view on offer', () => {
    expect(canChangeView(createComponent([grid], true))).toBeFalse();
  });

  it("is withheld when the profile's device cannot render a folder view", () => {
    expect(canChangeView(createComponent([grid, dashboard], false))).toBeFalse();
  });
});
