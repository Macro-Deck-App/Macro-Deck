import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { MigrationSummary } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { MigrationWizardComponent } from './migration-wizard.component';
import { MigrationPreviewOutcome, MigrationService } from '../../../services/migration.service';
import { MigrationWizardService } from '../../../services/migration-wizard.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MigrationWizardComponent', () => {
  let fixture: ComponentFixture<MigrationWizardComponent>;
  let migration: jasmine.SpyObj<MigrationService>;
  let wizardService: MigrationWizardService;

  function summary(overrides: Partial<MigrationSummary> = {}): MigrationSummary {
    return {
      sourceId: 'macrodeck2',
      sourceName: 'Macro Deck 2',
      profileCount: 1,
      folderCount: 1,
      widgetCount: 1,
      iconCount: 0,
      variableCount: 0,
      migratedActionCount: 1,
      unsupportedActionCount: 0,
      credentialStatus: 'NotPresent',
      integrations: [],
      unsupportedActions: [],
      warnings: [],
      ...overrides,
    };
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function buttonNamed(root: Element, label: string): HTMLButtonElement {
    const match = Array.from(root.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes(label));
    if (!match) {
      throw new Error(`No button labelled "${label}" in ${root.outerHTML}`);
    }
    return match;
  }

  async function clickAndAwaitClose(button: HTMLButtonElement): Promise<void> {
    jasmine.clock().install();
    try {
      button.click();
      jasmine.clock().tick(150);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [MigrationWizardComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: MigrationService, useValue: migration },
        {
          provide: ApiService,
          useValue: { getFilesystemEntries: () => Promise.resolve({ path: '', entries: [] }), onNotification: () => EMPTY },
        },
      ],
    }).compileComponents();

    wizardService = TestBed.inject(MigrationWizardService);
  }

  beforeEach(() => {
    migration = jasmine.createSpyObj<MigrationService>('MigrationService', ['getSources', 'preview', 'import']);
  });

  it('kicks off the preview as soon as it mounts, with the choice it was opened with', async () => {
    await create();
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'path', path: '/data/macrodeck2' } });
    migration.preview.and.resolveTo({ status: 'success', summary: summary() });

    fixture = TestBed.createComponent(MigrationWizardComponent);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: undefined,
      skipDecryption: false,
    });
    expect(fixture.nativeElement.querySelector('app-migration-preview-modal')).toBeTruthy();
  });

  it('sends the uploaded file it was opened with, not a path', async () => {
    await create();
    const file = new File(['zip-bytes'], 'backup.zip');
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'file', file } });
    migration.preview.and.resolveTo({ status: 'success', summary: summary() });

    fixture = TestBed.createComponent(MigrationWizardComponent);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(migration.preview).toHaveBeenCalledWith(jasmine.objectContaining({ input: { kind: 'file', file } }));
  });

  it('offers the skip-or-key choice when the source key cannot be read, and imports on skip', async () => {
    await create();
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'path', path: '/data/macrodeck2' } });
    migration.preview.and.resolveTo({ status: 'success', summary: summary({ credentialStatus: 'KeyUnavailable' }) });

    fixture = TestBed.createComponent(MigrationWizardComponent);
    await fixture.whenStable();
    fixture.detectChanges();

    const choiceModal = fixture.nativeElement.querySelector('shared-confirmation-modal');
    expect(choiceModal).toBeTruthy();
    expect(choiceModal.textContent).toContain('Macro Deck 2');

    migration.preview.and.resolveTo({ status: 'success', summary: summary({ credentialStatus: 'Skipped' }) });
    await clickAndAwaitClose(buttonNamed(choiceModal, 'Migrate without'));

    expect(migration.preview).toHaveBeenCalledWith(jasmine.objectContaining({ skipDecryption: true }));
    const previewModal = fixture.nativeElement.querySelector('app-migration-preview-modal');
    expect(previewModal).toBeTruthy();

    migration.import.and.resolveTo({
      status: 'success',
      summary: summary({ credentialStatus: 'Skipped' }),
      profileIds: ['p1'],
    });
    buttonNamed(previewModal, 'Import').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(migration.import).toHaveBeenCalledWith(jasmine.objectContaining({ skipDecryption: true }));
    expect(fixture.nativeElement.querySelector('app-migration-result-modal')).toBeTruthy();
  });

  it('keeps the key prompt on screen while the typed key is being checked, and again when it is rejected', async () => {
    await create();
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'path', path: '/data/macrodeck2' } });
    migration.preview.and.resolveTo({ status: 'success', summary: summary({ credentialStatus: 'KeyUnavailable' }) });

    fixture = TestBed.createComponent(MigrationWizardComponent);
    await fixture.whenStable();
    fixture.detectChanges();

    await clickAndAwaitClose(buttonNamed(fixture.nativeElement.querySelector('shared-confirmation-modal'), 'Enter the'));

    const prompt = fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal');
    expect(prompt).toBeTruthy();

    const input = prompt.querySelector('input') as HTMLInputElement;
    input.value = 'a-key';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    let resolvePreview: (outcome: MigrationPreviewOutcome) => void = () => undefined;
    migration.preview.and.returnValue(new Promise<MigrationPreviewOutcome>(resolve => (resolvePreview = resolve)));

    buttonNamed(prompt, 'Continue').click();
    await fixture.whenStable();
    fixture.detectChanges();

    // The request is in flight: the prompt has to still be the screen, not a closed modal over nothing.
    expect(fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-modal')).toBeTruthy();

    resolvePreview({ status: 'invalidKey' });
    await fixture.whenStable();
    fixture.detectChanges();

    // And a rejected key re-shows that same prompt with its retry message rather than a blank wizard.
    expect(fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal')).toBeTruthy();
    expect(text()).toContain("doesn't decrypt the stored data");
  });

  it('shows the source name before the preview resolves, from the choice it was opened with', async () => {
    await create();
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'path', path: '/data/macrodeck2' } });
    migration.preview.and.resolveTo({ status: 'invalidKey' });

    fixture = TestBed.createComponent(MigrationWizardComponent);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal')).toBeTruthy();
    expect(text()).toContain('Macro Deck 2');
  });

  it('closes the wizard from the loading/error screen', async () => {
    await create();
    wizardService.open({ sourceId: 'macrodeck2', sourceName: 'Macro Deck 2', input: { kind: 'path', path: '/data/macrodeck2' } });
    migration.preview.and.returnValue(new Promise(() => {}));

    fixture = TestBed.createComponent(MigrationWizardComponent);
    fixture.detectChanges();

    buttonNamed(fixture.nativeElement, 'Cancel').click();

    expect(wizardService.isOpen()).toBeFalse();
  });
});
