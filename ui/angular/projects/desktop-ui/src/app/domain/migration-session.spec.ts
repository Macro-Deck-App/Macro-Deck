import { MigrationSummary } from '@macro-deck/runtime';
import { MigrationSession } from './migration-session';
import { MigrationImportOutcome, MigrationPreviewOutcome, MigrationService } from '../services/migration.service';

describe('MigrationSession', () => {
  function summaryWith(overrides: Partial<MigrationSummary>): MigrationSummary {
    return {
      sourceId: 'macrodeck2',
      sourceName: 'Macro Deck 2',
      profileCount: 2,
      folderCount: 5,
      widgetCount: 30,
      iconCount: 4,
      variableCount: 1,
      migratedActionCount: 40,
      unsupportedActionCount: 0,
      credentialStatus: 'NotPresent',
      integrations: [],
      unsupportedActions: [],
      warnings: [],
      ...overrides,
    };
  }

  let migration: jasmine.SpyObj<MigrationService>;
  let session: MigrationSession;
  let imported: string[][];

  beforeEach(() => {
    migration = jasmine.createSpyObj<MigrationService>('MigrationService', ['getSources', 'preview', 'import']);
    imported = [];
    session = new MigrationSession(migration, profileIds => imported.push(profileIds));
  });

  it('goes straight to the preview when nothing needs deciding', async () => {
    const summary = summaryWith({ credentialStatus: 'NotPresent' });
    migration.preview.and.resolveTo({ status: 'success', summary });

    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    expect(session.step()).toBe('preview');
    expect(session.summary()).toBe(summary);
    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: undefined,
      skipDecryption: false,
    });
  });

  it('keeps showing the key prompt, marked as submitting, while a typed key is being checked', async () => {
    migration.preview.and.resolveTo({ status: 'success', summary: summaryWith({ credentialStatus: 'KeyUnavailable' }) });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });
    session.enterKey();

    let resolvePreview: (outcome: MigrationPreviewOutcome) => void = () => undefined;
    migration.preview.and.returnValue(new Promise<MigrationPreviewOutcome>(resolve => (resolvePreview = resolve)));
    const pending = session.submitKey('a-key');

    // While the host is still answering: the prompt is the screen, not a gap in the wizard.
    expect(session.step()).toBe('keyPrompt');
    expect(session.submitting()).toBeTrue();

    resolvePreview({ status: 'success', summary: summaryWith({ credentialStatus: 'Decrypted' }) });
    await pending;

    expect(session.step()).toBe('preview');
    expect(session.submitting()).toBeFalse();
  });

  it('shows the wizard loading screen while the skip-the-encrypted-data choice is being re-read', async () => {
    migration.preview.and.resolveTo({ status: 'success', summary: summaryWith({ credentialStatus: 'KeyUnavailable' }) });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });
    expect(session.step()).toBe('credentialChoice');

    let resolvePreview: (outcome: MigrationPreviewOutcome) => void = () => undefined;
    migration.preview.and.returnValue(new Promise<MigrationPreviewOutcome>(resolve => (resolvePreview = resolve)));
    const pending = session.chooseSkip();

    // The confirmation modal closed on the click, so something else has to be on screen meanwhile.
    expect(session.step()).toBe('source');
    expect(session.submitting()).toBeTrue();

    resolvePreview({ status: 'success', summary: summaryWith({ credentialStatus: 'Skipped' }) });
    await pending;

    expect(session.step()).toBe('preview');
  });

  it('sends an uploaded file to the same door a path goes through, and re-sends it on import', async () => {
    const file = new File(['zip-bytes'], 'backup.zip');
    const summary = summaryWith({ credentialStatus: 'NotPresent' });
    migration.preview.and.resolveTo({ status: 'success', summary });

    await session.preview('macrodeck2', { kind: 'file', file });

    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'file', file },
      decryptionKey: undefined,
      skipDecryption: false,
    });

    migration.import.and.resolveTo({ status: 'success', summary, profileIds: ['p1'] });
    await session.confirmImport();

    expect(migration.import).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'file', file },
      decryptionKey: undefined,
      skipDecryption: false,
    });
  });

  it('preview -> key unavailable -> user chooses skip -> import runs with skipDecryption true', async () => {
    const unavailable = summaryWith({ credentialStatus: 'KeyUnavailable' });
    const skipped = summaryWith({ credentialStatus: 'Skipped' });
    migration.preview.and.resolveTo({ status: 'success', summary: unavailable } as MigrationPreviewOutcome);
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    expect(session.step()).toBe('credentialChoice');
    expect(session.credentialSummary()).toBe(unavailable);

    migration.preview.and.resolveTo({ status: 'success', summary: skipped } as MigrationPreviewOutcome);
    await session.chooseSkip();

    expect(session.step()).toBe('preview');
    expect(session.summary()).toBe(skipped);
    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: undefined,
      skipDecryption: true,
    });

    migration.import.and.resolveTo({
      status: 'success',
      summary: skipped,
      profileIds: ['p1', 'p2'],
    } as MigrationImportOutcome);
    await session.confirmImport();

    expect(migration.import).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: undefined,
      skipDecryption: true,
    });
    expect(session.step()).toBe('result');
    expect(session.result()).toEqual({ summary: skipped, profileIds: ['p1', 'p2'] });
    expect(imported).toEqual([['p1', 'p2']]);
  });

  it('preview -> key unavailable -> enter key -> rejected -> re-prompted -> accepted -> import', async () => {
    const unavailable = summaryWith({ credentialStatus: 'KeyUnavailable' });
    migration.preview.and.resolveTo({ status: 'success', summary: unavailable } as MigrationPreviewOutcome);
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });
    expect(session.step()).toBe('credentialChoice');

    session.enterKey();
    expect(session.step()).toBe('keyPrompt');
    expect(session.keyInvalid()).toBeFalse();

    migration.preview.and.resolveTo({ status: 'invalidKey' } as MigrationPreviewOutcome);
    await session.submitKey('wrong-key');

    expect(session.step()).toBe('keyPrompt');
    expect(session.keyInvalid()).toBeTrue();

    // Falling back to skip is still reachable from the rejected-key screen.
    session.backToCredentialChoice();
    expect(session.step()).toBe('credentialChoice');
    expect(session.keyInvalid()).toBeFalse();

    session.enterKey();
    const decrypted = summaryWith({ credentialStatus: 'Decrypted' });
    migration.preview.and.resolveTo({ status: 'success', summary: decrypted } as MigrationPreviewOutcome);
    await session.submitKey('right-key');

    expect(session.step()).toBe('preview');
    expect(session.summary()).toBe(decrypted);
    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: 'right-key',
      skipDecryption: false,
    });

    migration.import.and.resolveTo({
      status: 'success',
      summary: decrypted,
      profileIds: ['p1'],
    } as MigrationImportOutcome);
    await session.confirmImport();

    expect(migration.import).toHaveBeenCalledWith({
      sourceId: 'macrodeck2',
      input: { kind: 'path', path: '/data/macrodeck2' },
      decryptionKey: 'right-key',
      skipDecryption: false,
    });
    expect(session.step()).toBe('result');
  });

  it('an InvalidDecryptionKey error at preview time is treated the same as a rejected key', async () => {
    migration.preview.and.resolveTo({ status: 'invalidKey' } as MigrationPreviewOutcome);

    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    expect(session.step()).toBe('keyPrompt');
    expect(session.keyInvalid()).toBeTrue();
  });

  it('surfaces an infrastructure error and returns to the source step', async () => {
    migration.preview.and.resolveTo({ status: 'error', message: 'The folder could not be read' });

    await session.preview('macrodeck2', { kind: 'path', path: '/not/there' });

    expect(session.step()).toBe('source');
    expect(session.error()).toBe('The folder could not be read');
  });

  it('surfaces an import failure without discarding the accepted preview', async () => {
    const summary = summaryWith({ credentialStatus: 'NotPresent' });
    migration.preview.and.resolveTo({ status: 'success', summary });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    migration.import.and.resolveTo({ status: 'error', message: 'Storage failure' });
    await session.confirmImport();

    expect(session.step()).toBe('preview');
    expect(session.summary()).toBe(summary);
    expect(session.error()).toBe('Storage failure');
    expect(imported).toEqual([]);
  });

  it('a key rejected between preview and import is routed back through the key prompt', async () => {
    const summary = summaryWith({ credentialStatus: 'Decrypted' });
    migration.preview.and.resolveTo({ status: 'success', summary });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    migration.import.and.resolveTo({ status: 'invalidKey' } as MigrationImportOutcome);
    await session.confirmImport();

    expect(session.step()).toBe('keyPrompt');
    expect(session.keyInvalid()).toBeTrue();
  });

  it('drops every choice made so far when the wizard is cancelled', async () => {
    const summary = summaryWith({ credentialStatus: 'NotPresent' });
    migration.preview.and.resolveTo({ status: 'success', summary });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });

    session.cancel();

    expect(session.step()).toBe('source');
    expect(session.summary()).toBeNull();

    migration.preview.calls.reset();
    await session.chooseSkip();
    expect(migration.preview).toHaveBeenCalledWith({
      sourceId: '',
      input: { kind: 'path', path: '' },
      decryptionKey: undefined,
      skipDecryption: true,
    });
  });

  it('dismisses the infrastructure error independently of the step', async () => {
    migration.preview.and.resolveTo({ status: 'error', message: 'nope' });
    await session.preview('macrodeck2', { kind: 'path', path: '/data/macrodeck2' });
    expect(session.error()).toBe('nope');

    session.dismissError();

    expect(session.error()).toBeNull();
  });
});
