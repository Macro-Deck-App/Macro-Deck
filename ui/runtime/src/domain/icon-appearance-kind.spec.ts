import { AppStrings } from '../localization/generated/app-strings';
import { ICON_APPEARANCE_KINDS, iconAppearanceKindLabel } from './icon-appearance-kind';

describe('icon appearance kinds', () => {
  it('names every known kind by its own label', () => {
    expect(iconAppearanceKindLabel('colorScheme=dark')).toEqual({ label: AppStrings.IconPacks.Appearances.Kind.Dark });
    expect(iconAppearanceKindLabel('motion=static')).toEqual({ label: AppStrings.IconPacks.Appearances.Kind.Static });
    expect(iconAppearanceKindLabel('colorScheme=dark;motion=static'))
      .toEqual({ label: AppStrings.IconPacks.Appearances.Kind.DarkStatic });
    expect(new Set(ICON_APPEARANCE_KINDS.map(kind => kind.label)).size).toBe(8);
  });

  it('shows an unknown kind as custom with its traits', () => {
    expect(iconAppearanceKindLabel('contrast=high')).toEqual({
      label: AppStrings.IconPacks.Appearances.Kind.Custom,
      args: { traits: 'contrast=high' },
    });
  });
});
