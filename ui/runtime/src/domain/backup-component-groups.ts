import { AppStrings } from '../localization/generated/app-strings';

export type BackupComponentGroup =
  | 'Profiles'
  | 'Scripts'
  | 'Automations'
  | 'Variables'
  | 'Icons'
  | 'Plugins'
  | 'Integrations'
  | 'AppSettings'
  | 'Accounts';

export interface BackupComponentGroupLabel {
  readonly id: BackupComponentGroup;
  readonly label: string;
  readonly description: string;
}

export interface BackupDependencyWarning {
  readonly group: BackupComponentGroup;
  readonly missingDependency: BackupComponentGroup;
  readonly message: string;
}

export type BackupTranslator = (key: string, args?: Record<string, unknown>) => string;

export const BACKUP_COMPONENT_GROUP_IDS: readonly BackupComponentGroup[] = [
  'Profiles',
  'Scripts',
  'Automations',
  'Variables',
  'Icons',
  'Plugins',
  'Integrations',
  'AppSettings',
  'Accounts',
];

export function backupComponentGroupLabels(t: BackupTranslator): readonly BackupComponentGroupLabel[] {
  return BACKUP_COMPONENT_GROUP_IDS.map(id => ({
    id,
    label: backupComponentGroupLabel(id, t),
    description: t(AppStrings.Backup.Group[id].Description),
  }));
}

export function backupComponentGroupLabel(id: BackupComponentGroup, t: BackupTranslator): string {
  // The host owns the group list, so it can report a group this client has no wording for yet. Such a
  // group has to read as its raw id rather than crash the restore dialog on a missing key.
  const keys: { Label: string } | undefined = AppStrings.Backup.Group[id];
  return keys ? t(keys.Label) : id;
}

export function backupDependencyWarningMessage(
  group: BackupComponentGroup,
  missingDependency: BackupComponentGroup,
  t: BackupTranslator,
): string {
  const specific = WARNING_KEYS[`${group}>${missingDependency}`];

  return specific
    ? t(specific)
    : t(AppStrings.Backup.Warning.Generic, {
        group: backupComponentGroupLabel(group, t),
        dependency: backupComponentGroupLabel(missingDependency, t).toLocaleLowerCase(),
      });
}

const WARNING_KEYS: Readonly<Record<string, string>> = {
  'Profiles>Icons': AppStrings.Backup.Warning.ProfilesWithoutIcons,
  'Profiles>Integrations': AppStrings.Backup.Warning.ProfilesWithoutIntegrations,
  'Profiles>Plugins': AppStrings.Backup.Warning.ProfilesWithoutPlugins,
  'Scripts>Integrations': AppStrings.Backup.Warning.ScriptsWithoutIntegrations,
  'Automations>Scripts': AppStrings.Backup.Warning.AutomationsWithoutScripts,
  'Automations>Variables': AppStrings.Backup.Warning.AutomationsWithoutVariables,
  'Automations>Integrations': AppStrings.Backup.Warning.AutomationsWithoutIntegrations,
  'Plugins>Integrations': AppStrings.Backup.Warning.PluginsWithoutIntegrations,
  'Accounts>Profiles': AppStrings.Backup.Warning.AccountsWithoutProfiles,
};
