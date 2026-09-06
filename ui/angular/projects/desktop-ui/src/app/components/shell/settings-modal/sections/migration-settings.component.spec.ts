import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MigrationSettingsComponent } from './migration-settings.component';
import { MigrationService, MigrationSourcesOutcome } from '../../../../services/migration.service';
import { MigrationWizardService } from '../../../../services/migration-wizard.service';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

describe('MigrationSettingsComponent', () => {
  let fixture: ComponentFixture<MigrationSettingsComponent>;
  let migration: jasmine.SpyObj<MigrationService>;
  let wizard: MigrationWizardService;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  async function create(outcome: MigrationSourcesOutcome): Promise<void> {
    migration = jasmine.createSpyObj<MigrationService>('MigrationService', ['getSources']);
    migration.getSources.and.resolveTo(outcome);

    await TestBed.configureTestingModule({
      imports: [MigrationSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: MigrationService, useValue: migration },
      ],
    }).compileComponents();

    wizard = TestBed.inject(MigrationWizardService);
    fixture = TestBed.createComponent(MigrationSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => useShell(null));

  it('shows the detected path for a source that was found', async () => {
    await create({
      status: 'success',
      sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }],
    });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Macro Deck 2');
    expect(text).toContain('/data/macrodeck2');
  });

  it('says nothing was found for a source with no detected path', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2' }]});

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No installation found on this device');
  });

  it('shows an empty state when there are no sources', async () => {
    await create({ status: 'success', sources: []});

    expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeTruthy();
  });

  it('shows an error banner when the sources fail to load', async () => {
    await create({ status: 'error', message: 'The host could not be reached' });

    const banner = fixture.nativeElement.querySelector('shared-error-banner');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('The host could not be reached');
  });

  it('offers a native folder and archive picker inside the desktop shell, and starts the wizard with the resolved path', async () => {
    const showOpenDialog = jasmine.createSpy('showOpenDialog').and.resolveTo('/picked/path');
    useShell({ showOpenDialog });
    await create({
      status: 'success',
      sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }],
    });

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>);
    const folderButton = buttons.find(b => b.textContent?.includes('Choose folder'));
    const archiveButton = buttons.find(b => b.textContent?.includes('Choose backup file'));
    expect(folderButton).toBeTruthy();
    expect(archiveButton).toBeTruthy();
    expect(buttons.find(b => b.textContent?.includes('Upload backup file'))).toBeFalsy();

    folderButton!.click();
    await fixture.whenStable();

    expect(showOpenDialog).toHaveBeenCalledWith({ directory: true });
    expect(wizard.isOpen()).toBeTrue();
  });

  it('offers only an upload action in a plain browser', async () => {
    useShell(null);
    await create({
      status: 'success',
      sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }],
    });

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>);
    const uploadButton = buttons.find(b => b.textContent?.includes('Upload backup file'));
    expect(uploadButton).toBeTruthy();
    expect(buttons.find(b => b.textContent?.includes('Choose folder'))).toBeFalsy();
    expect(buttons.find(b => b.textContent?.includes('Choose backup file'))).toBeFalsy();

    // Picks the row's source (sets the upload target) the way clicking the button does, then drives
    // the hidden file input the way its change event does - without touching the DOM's FileList (real
    // `<input>` elements refuse to have `files` assigned directly).
    uploadButton!.click();
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File(['zip-bytes'], 'backup.zip')] });
    fixture.componentInstance.onUploadFileSelected({ target: input } as unknown as Event);
    await fixture.whenStable();

    expect(wizard.isOpen()).toBeTrue();
  });
});
