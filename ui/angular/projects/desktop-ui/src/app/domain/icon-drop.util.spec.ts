import { ICON_DROP_EXTENSIONS, acceptsIconDropPath, droppedPathExtension } from './icon-drop.util';

describe('icon-drop.util', () => {
  describe('droppedPathExtension', () => {
    it('reads the extension from a posix and a windows path', () => {
      expect(droppedPathExtension('/Users/me/logo.PNG')).toBe('png');
      expect(droppedPathExtension('C:\\Program Files\\App\\app.EXE')).toBe('exe');
    });

    it('ignores a trailing separator so a dropped directory keeps its extension', () => {
      expect(droppedPathExtension('/Applications/Spotify.app/')).toBe('app');
    });

    it('returns empty for a name without an extension or a leading dot only', () => {
      expect(droppedPathExtension('/usr/bin/code')).toBe('');
      expect(droppedPathExtension('/home/me/.bashrc')).toBe('');
    });
  });

  describe('acceptsIconDropPath', () => {
    it('accepts every declared source extension', () => {
      for (const extension of ICON_DROP_EXTENSIONS) {
        expect(acceptsIconDropPath({ path: `/tmp/thing.${extension}`, directory: false }))
          .withContext(extension)
          .toBeTrue();
      }
    });

    it('rejects a file it cannot read an icon from', () => {
      expect(acceptsIconDropPath({ path: '/tmp/notes.txt', directory: false })).toBeFalse();
      expect(acceptsIconDropPath({ path: '/tmp/archive.zip', directory: false })).toBeFalse();
    });

    it('accepts a .lottie animation but not a bare .json', () => {
      expect(acceptsIconDropPath({ path: '/tmp/spinner.lottie', directory: false })).toBeTrue();
      expect(acceptsIconDropPath({ path: '/tmp/tsconfig.json', directory: false })).toBeFalse();
    });

    it('accepts a .app directory but no other directory', () => {
      expect(acceptsIconDropPath({ path: '/Applications/Spotify.app', directory: true })).toBeTrue();
      expect(acceptsIconDropPath({ path: '/Users/me/Pictures', directory: true })).toBeFalse();
      expect(acceptsIconDropPath({ path: '/Users/me/icons.png', directory: true })).toBeFalse();
    });
  });
});
