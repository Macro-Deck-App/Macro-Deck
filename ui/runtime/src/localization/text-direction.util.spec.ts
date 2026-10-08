import { cultureChain, effectiveCulture, textDirection } from './text-direction.util';

describe('effectiveCulture', () => {
  const shipped = ['en', 'de', 'zh', 'zh-TW', 'ar', 'pt-BR'];

  it('serves a regional request from its neutral catalog', () => {
    expect(effectiveCulture('de-AT', shipped)).toBe('de');
  });

  it('serves Traditional Chinese readers from the Taiwan catalog', () => {
    expect(effectiveCulture('zh-HK', shipped)).toBe('zh-TW');
    expect(effectiveCulture('zh-Hant-TW', shipped)).toBe('zh-TW');
    expect(effectiveCulture('zh-CN', shipped)).toBe('zh');
  });

  it('falls back to English for a language no catalog carries', () => {
    expect(effectiveCulture('he-IL', shipped)).toBe('en');
    expect(effectiveCulture('pt-PT', shipped)).toBe('en');
  });

  it('returns the catalog spelling of the culture', () => {
    expect(effectiveCulture('pt-br', shipped)).toBe('pt-BR');
  });

  it('matches the host chain order', () => {
    expect(cultureChain('zh-HK')).toEqual(['zh-HK', 'zh-Hant', 'zh-TW', 'zh', 'en']);
    expect(cultureChain(undefined)).toEqual(['en']);
  });
});

describe('textDirection', () => {
  it('is right to left for Arabic in any region', () => {
    expect(textDirection('ar')).toBe('rtl');
    expect(textDirection('ar-EG')).toBe('rtl');
  });

  it('is left to right for other languages and a missing culture', () => {
    expect(textDirection('de')).toBe('ltr');
    expect(textDirection('zh-TW')).toBe('ltr');
    expect(textDirection(undefined)).toBe('ltr');
  });

  it('follows a right-to-left script subtag', () => {
    expect(textDirection('pa-Arab')).toBe('rtl');
  });
});
