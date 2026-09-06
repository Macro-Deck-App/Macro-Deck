import { ArchiveSummary } from '@macro-deck/runtime';
import { ArchiveImportSession } from './archive-import-session';
import { ArchiveSource, PortabilityService, PortableImportOutcome } from '../services/portability.service';

describe('ArchiveImportSession', () => {
  const file = new File(['x'], 'profile.macroDeckProfile');

  const summary: ArchiveSummary = {
    kind: 'Profile',
    name: 'Streaming',
    appVersion: '3.0.0',
    createdAt: '2026-07-30T10:00:00Z',
    encrypted: false,
    includesSecrets: false,
    folderCount: 1,
    widgetCount: 1,
    iconCount: 0,
    scriptCount: 0,
    variableCount: 0,
    secretCount: 0,
    integrations: [],
  };

  let portability: jasmine.SpyObj<PortabilityService>;
  let outcomes: PortableImportOutcome[];
  let session: ArchiveImportSession;
  let succeeded: PortableImportOutcome[];
  let imported: ArchiveSource[];

  function newSession(): ArchiveImportSession {
    return new ArchiveImportSession(
      portability,
      source => {
        imported.push(source);
        return Promise.resolve(outcomes.shift() ?? { status: 'error', message: 'no outcome queued' });
      },
      outcome => succeeded.push(outcome)
    );
  }

  beforeEach(() => {
    portability = jasmine.createSpyObj<PortabilityService>('PortabilityService', ['inspectArchive']);
    portability.inspectArchive.and.resolveTo({ status: 'success', archive: summary });
    outcomes = [];
    succeeded = [];
    imported = [];
    session = newSession();
  });

  it('previews the archive before importing anything', async () => {
    await session.beginFile(file);

    expect(session.summary()).toBe(summary);
    expect(succeeded).toEqual([]);
  });

  it('imports only once the preview is confirmed', async () => {
    outcomes = [{ status: 'success' }];
    await session.beginFile(file);
    await session.confirm();

    expect(session.summary()).toBeNull();
    expect(succeeded.length).toBe(1);
  });

  it('reports an unreadable file instead of opening a preview', async () => {
    portability.inspectArchive.and.resolveTo({ status: 'error', message: 'not a valid archive' });

    await session.beginFile(file);

    expect(session.summary()).toBeNull();
    expect(session.error()).toBe('not a valid archive');
  });

  it('asks for the password only after the preview, and retries on a wrong one', async () => {
    outcomes = [{ status: 'passwordRequired' }, { status: 'invalidPassword' }, { status: 'success' }];
    await session.beginFile(file);

    await session.confirm();
    expect(session.passwordVisible()).toBeTrue();
    expect(session.passwordInvalid()).toBeFalse();

    await session.submitPassword('nope');
    expect(session.passwordVisible()).toBeTrue();
    expect(session.passwordInvalid()).toBeTrue();

    await session.submitPassword('right');
    expect(session.passwordVisible()).toBeFalse();
    expect(succeeded.length).toBe(1);
  });

  it('drops the picked file when the flow is abandoned', async () => {
    outcomes = [{ status: 'success' }];
    await session.beginFile(file);
    session.cancel();

    await session.confirm();

    expect(session.summary()).toBeNull();
    expect(succeeded).toEqual([]);
  });

  it('surfaces a failed import as an error', async () => {
    outcomes = [{ status: 'error', message: 'Not enough free space in the folder' }];
    await session.beginFile(file);
    await session.confirm();

    expect(session.error()).toBe('Not enough free space in the folder');
    expect(succeeded).toEqual([]);
  });

  it('imports a path the same way, password prompt included', async () => {
    outcomes = [{ status: 'passwordRequired' }, { status: 'success' }];

    await session.beginPath('/tmp/Streaming.macroDeckProfile');
    expect(portability.inspectArchive).toHaveBeenCalledWith({
      kind: 'path',
      path: '/tmp/Streaming.macroDeckProfile',
    });

    await session.confirm();
    expect(session.passwordVisible()).toBeTrue();

    await session.submitPassword('hunter2');

    expect(succeeded.length).toBe(1);
    expect(imported).toEqual([
      { kind: 'path', path: '/tmp/Streaming.macroDeckProfile' },
      { kind: 'path', path: '/tmp/Streaming.macroDeckProfile' },
    ]);
  });
});
