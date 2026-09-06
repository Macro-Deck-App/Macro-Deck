import { ARCHIVE_DROP_EXTENSIONS, acceptsArchiveDropPath, archiveDropKind } from './archive-drop.util';

describe('archive-drop.util', () => {
  it('recognises every exported archive kind', () => {
    expect(archiveDropKind('/tmp/Streaming.macroDeckProfile')).toBe('profile');
    expect(archiveDropKind('/tmp/Lights.macroDeckFolder')).toBe('folder');
    expect(archiveDropKind('/tmp/widgets.macroDeckWidget')).toBe('widgets');
    expect(archiveDropKind('/tmp/Neon.macroDeckIconPack')).toBe('iconPack');
    expect(archiveDropKind('/tmp/Sample.macroDeckPlugin')).toBe('plugin');
  });

  it('ignores extension case', () => {
    expect(archiveDropKind('/tmp/A.MACRODECKPROFILE')).toBe('profile');
    expect(archiveDropKind('C:\\Decks\\A.MacroDeckFolder')).toBe('folder');
  });

  it('is not fooled by other archives', () => {
    expect(archiveDropKind('/tmp/pack.zip')).toBeNull();
    expect(archiveDropKind('/tmp/pack.streamDeckIconPack')).toBeNull();
    expect(archiveDropKind('/tmp/no-extension')).toBeNull();
  });

  it('accepts only the kinds a target asked for', () => {
    const profile = { path: '/tmp/a.macroDeckProfile', directory: false };

    expect(acceptsArchiveDropPath(profile, ['profile'])).toBeTrue();
    expect(acceptsArchiveDropPath(profile, ['folder', 'widgets'])).toBeFalse();
    expect(acceptsArchiveDropPath(profile, [])).toBeFalse();
  });

  // A macOS bundle is a directory whose name ends in an extension, so a directory that happens to
  // be named like an archive must not be offered as one - unlike icon drops, which want .app.
  it('never accepts a directory', () => {
    expect(acceptsArchiveDropPath({ path: '/tmp/a.macroDeckProfile', directory: true }, ['profile'])).toBeFalse();
  });

  it('keeps the extension table in step with the kinds', () => {
    for (const [kind, extension] of Object.entries(ARCHIVE_DROP_EXTENSIONS)) {
      expect(archiveDropKind(`/tmp/file.${extension}`)).toBe(kind as never);
    }
  });
});
