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

const VARIANT_PREFIX = 'variant=';
const VARIANT_TOKEN = /^[a-z][a-zA-Z0-9]{0,31}$/;
const LETTER_FOLDS: Record<string, string> = { ß: 'ss', æ: 'ae', œ: 'oe', ø: 'o', đ: 'd', ð: 'd', ł: 'l', þ: 'th' };

export function iconAppearanceVariantKey(name: string): string | null {
  const folded = name.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/[ßæœøđðłþ]/g, letter => LETTER_FOLDS[letter]);
  const words = folded.split(/[^a-z0-9]+/).filter(word => word.length > 0);
  if (words.length === 0) return null;
  const token = words[0] + words.slice(1).map(word => word[0].toUpperCase() + word.slice(1)).join('');
  return VARIANT_TOKEN.test(token) ? VARIANT_PREFIX + token : null;
}

export function humanizeIconAppearanceVariant(token: string): string {
  const text = token.split(/(?<=[a-z0-9])(?=[A-Z])/)
    .map((word, index) => (index > 0 && word.length > 1 && word === word.toUpperCase() ? word : word.toLowerCase()))
    .join(' ');
  return text.length === 0 ? text : text[0].toUpperCase() + text.slice(1);
}

export function iconAppearanceVariantName(key: string): string | null {
  return key.startsWith(VARIANT_PREFIX) && VARIANT_TOKEN.test(key.slice(VARIANT_PREFIX.length))
    ? humanizeIconAppearanceVariant(key.slice(VARIANT_PREFIX.length))
    : null;
}

export function iconAppearanceKindLabel(key: string): IconAppearanceKindLabel {
  for (let index = 0; index < ICON_APPEARANCE_KINDS.length; index++) {
    if (ICON_APPEARANCE_KINDS[index].key === key) return { label: ICON_APPEARANCE_KINDS[index].label };
  }
  const variant = iconAppearanceVariantName(key);
  if (variant !== null) return { label: AppStrings.IconPacks.Appearances.Kind.Named, args: { name: variant } };
  return { label: AppStrings.IconPacks.Appearances.Kind.Custom, args: { traits: key } };
}
