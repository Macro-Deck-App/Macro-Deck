import { bundledTranslator as t } from '../localization/bundled-translator';
import {
  backupComponentGroupLabel,
  backupComponentGroupLabels,
  backupDependencyWarningMessage,
} from './backup-component-groups';

describe('backup component groups', () => {
  it('labels all nine groups the host restores', () => {
    expect(backupComponentGroupLabels(t).map(group => group.id)).toEqual([
      'Profiles',
      'Scripts',
      'Automations',
      'Variables',
      'Icons',
      'Plugins',
      'Integrations',
      'AppSettings',
      'Accounts',
    ]);
  });

  it('names the concrete consequence of dropping a dependency', () => {
    const message = backupDependencyWarningMessage('Profiles', 'Integrations', t);

    expect(message).toContain('password');
    expect(message).not.toContain('Integrations>');
  });

  // A pair the host reports but this catalogue has no wording for must still read as a sentence rather
  // than leaking an identifier into the dialog.
  it('falls back to a readable sentence for an unlisted pair', () => {
    const message = backupDependencyWarningMessage('Variables', 'Icons', t);

    expect(message).toContain('Variables');
    expect(message).toContain('icons and icon packs');
  });

  it('falls back to the raw id only when the group is unknown', () => {
    expect(backupComponentGroupLabel('Plugins', t)).toBe('Plugins');
    expect(backupComponentGroupLabel('Nope' as never, t)).toBe('Nope');
  });
});
