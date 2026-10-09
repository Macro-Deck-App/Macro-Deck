import { AppStrings } from '../localization/generated/app-strings';
import {
  ICON_APPEARANCE_KINDS, humanizeIconAppearanceVariant, iconAppearanceKindLabel, iconAppearanceVariantKey,
} from './icon-appearance-kind';

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

  it('names a custom variant by its humanized token', () => {
    expect(iconAppearanceKindLabel('variant=duoTone')).toEqual({
      label: AppStrings.IconPacks.Appearances.Kind.Named,
      args: { name: 'Duo tone' },
    });
    expect(iconAppearanceKindLabel('colorScheme=dark;variant=red').label)
      .toBe(AppStrings.IconPacks.Appearances.Kind.Custom);
  });

  it('humanizes tokens the way the host does', () => {
    const pairs: Record<string, string> = {
      outlined: 'Outlined', duoTone: 'Duo tone', red: 'Red', style2: 'Style2', iOS: 'I OS',
    };
    for (const [token, text] of Object.entries(pairs)) expect(humanizeIconAppearanceVariant(token)).toBe(text);
  });

  it('derives a variant key from a typed name', () => {
    expect(iconAppearanceVariantKey('Outlined')).toBe('variant=outlined');
    expect(iconAppearanceVariantKey('Duo-tone')).toBe('variant=duoTone');
    expect(iconAppearanceVariantKey('Duo tone')).toBe('variant=duoTone');
    expect(iconAppearanceVariantKey('Gefüllt')).toBe('variant=gefullt');
    expect(iconAppearanceVariantKey('Straße')).toBe('variant=strasse');
    expect(iconAppearanceVariantKey('iOS Style')).toBe('variant=iosStyle');
  });

  it('refuses names that cannot become a token', () => {
    expect(iconAppearanceVariantKey('   ')).toBeNull();
    expect(iconAppearanceVariantKey('123')).toBeNull();
    expect(iconAppearanceVariantKey('填充')).toBeNull();
    expect(iconAppearanceVariantKey('a'.repeat(33))).toBeNull();
  });
});
