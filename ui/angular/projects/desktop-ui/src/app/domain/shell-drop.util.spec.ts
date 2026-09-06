import { ShellDroppedPath } from '../util/shell-bridge';
import { acceptsShellDropPath, isApplicationDropPath, shellDropKind } from './shell-drop.util';

describe('shell-drop.util', () => {
  function file(path: string): ShellDroppedPath {
    return { path, directory: false };
  }

  function directory(path: string): ShellDroppedPath {
    return { path, directory: true };
  }

  describe('isApplicationDropPath', () => {
    it('takes the application and shortcut types of every platform', () => {
      expect(isApplicationDropPath(directory('/Applications/Calculator.app'))).toBeTrue();
      expect(isApplicationDropPath(file('C:\\Program Files\\App\\app.exe'))).toBeTrue();
      expect(isApplicationDropPath(file('C:\\Users\\me\\Desktop\\App.lnk'))).toBeTrue();
      expect(isApplicationDropPath(file('/home/me/.local/share/applications/app.desktop'))).toBeTrue();
      expect(isApplicationDropPath(file('/home/me/run.sh'))).toBeTrue();
    });

    it('sees through a trailing separator on a bundle', () => {
      expect(isApplicationDropPath(directory('/Applications/Calculator.app/'))).toBeTrue();
    });

    it('offers an extension-less file to the host', () => {
      expect(isApplicationDropPath(file('/usr/local/bin/tool'))).toBeTrue();
    });

    it('rejects a plain folder and anything that is not an application', () => {
      expect(isApplicationDropPath(directory('/Users/me/Documents'))).toBeFalse();
      expect(isApplicationDropPath(file('/Users/me/notes.md'))).toBeFalse();
      expect(isApplicationDropPath(file('/Users/me/logo.png'))).toBeFalse();
    });
  });

  describe('shellDropKind', () => {
    it('tells an application from an archive', () => {
      expect(shellDropKind(file('/tmp/Streaming.macroDeckProfile'))).toBe('profile');
      expect(shellDropKind(file('/tmp/Lights.macroDeckFolder'))).toBe('folder');
      expect(shellDropKind(directory('/Applications/Calculator.app'))).toBe('application');
      expect(shellDropKind(file('/tmp/notes.md'))).toBeNull();
    });
  });

  describe('acceptsShellDropPath', () => {
    it('accepts only the kinds the target asked for', () => {
      const bundle = directory('/Applications/Calculator.app');
      expect(acceptsShellDropPath(bundle, ['application'])).toBeTrue();
      expect(acceptsShellDropPath(bundle, ['widgets', 'iconPack'])).toBeFalse();

      const widgets = file('/tmp/Deck.macroDeckWidget');
      expect(acceptsShellDropPath(widgets, ['widgets', 'application'])).toBeTrue();
      expect(acceptsShellDropPath(widgets, ['application'])).toBeFalse();
    });
  });
});
