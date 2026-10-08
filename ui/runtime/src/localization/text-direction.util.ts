const RIGHT_TO_LEFT_LANGUAGES = ['ar', 'he', 'fa', 'ur', 'ps', 'sd', 'ug', 'yi', 'dv', 'ckb'];
const TRADITIONAL_CHINESE_REGIONS = ['tw', 'hk', 'mo'];
const DEFAULT_CULTURE = 'en';

export type TextDirection = 'ltr' | 'rtl';

export function cultureChain(culture: string | null | undefined): string[] {
  const chain: string[] = [];
  const add = (candidate: string | null | undefined): void => {
    if (!candidate) return;
    if (chain.some(existing => existing.toLowerCase() === candidate.toLowerCase())) return;
    chain.push(candidate);
  };

  add(culture);
  if (isTraditionalChinese(culture)) {
    add('zh-Hant');
    add('zh-TW');
  }
  const separator = culture ? culture.indexOf('-') : -1;
  if (culture && separator > 0) add(culture.slice(0, separator));
  add(DEFAULT_CULTURE);
  return chain;
}

export function effectiveCulture(culture: string | null | undefined, available: readonly string[]): string {
  const chain = cultureChain(culture);
  for (let rung = 0; rung < chain.length; rung++) {
    for (let index = 0; index < available.length; index++) {
      if (available[index].toLowerCase() === chain[rung].toLowerCase()) return available[index];
    }
  }
  return DEFAULT_CULTURE;
}

export function textDirection(culture: string | null | undefined): TextDirection {
  if (!culture) return 'ltr';
  const parts = culture.toLowerCase().split('-');
  if (RIGHT_TO_LEFT_LANGUAGES.indexOf(parts[0]) >= 0) return 'rtl';
  return parts.slice(1).some(part => part === 'arab' || part === 'hebr') ? 'rtl' : 'ltr';
}

function isTraditionalChinese(culture: string | null | undefined): boolean {
  if (!culture) return false;
  const parts = culture.toLowerCase().split('-');
  if (parts[0] !== 'zh' || parts.length < 2) return false;
  if (parts[1].length === 4) return parts[1] === 'hant';
  return TRADITIONAL_CHINESE_REGIONS.indexOf(parts[1]) >= 0;
}
