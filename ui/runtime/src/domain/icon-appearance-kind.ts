import { AppStrings } from '../localization/generated/app-strings';

export interface IconAppearanceKind {
  readonly key: string;
  readonly label: string;
}

export interface IconAppearanceKindLabel {
  readonly label: string;
  readonly args?: Record<string, string>;
}

export const ICON_APPEARANCE_KINDS: readonly IconAppearanceKind[] = [
  { key: 'colorScheme=light', label: AppStrings.IconPacks.Appearances.Kind.Light },
  { key: 'colorScheme=dark', label: AppStrings.IconPacks.Appearances.Kind.Dark },
  { key: 'motion=static', label: AppStrings.IconPacks.Appearances.Kind.Static },
  { key: 'motion=animated', label: AppStrings.IconPacks.Appearances.Kind.Animated },
  { key: 'colorScheme=light;motion=static', label: AppStrings.IconPacks.Appearances.Kind.LightStatic },
  { key: 'colorScheme=light;motion=animated', label: AppStrings.IconPacks.Appearances.Kind.LightAnimated },
  { key: 'colorScheme=dark;motion=static', label: AppStrings.IconPacks.Appearances.Kind.DarkStatic },
  { key: 'colorScheme=dark;motion=animated', label: AppStrings.IconPacks.Appearances.Kind.DarkAnimated },
];

export function iconAppearanceKindLabel(key: string): IconAppearanceKindLabel {
  for (let index = 0; index < ICON_APPEARANCE_KINDS.length; index++) {
    if (ICON_APPEARANCE_KINDS[index].key === key) return { label: ICON_APPEARANCE_KINDS[index].label };
  }
  return { label: AppStrings.IconPacks.Appearances.Kind.Custom, args: { traits: key } };
}
