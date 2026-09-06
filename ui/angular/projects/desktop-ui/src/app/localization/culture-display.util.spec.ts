import { cultureDisplayName, sortCulturesForReader } from './culture-display.util';

describe('cultureDisplayName', () => {
  it('names a language in its own language', () => {
    expect(cultureDisplayName('de')).toBe('Deutsch');
    expect(cultureDisplayName('ja')).toBe('日本語');
  });

  it('falls back to the tag when the platform has no name for it', () => {
    expect(cultureDisplayName('qya')).toBe('qya');
  });
});

describe('sortCulturesForReader', () => {
  it("puts the reader's language first, English next, then the rest by displayed name", () => {
    expect(sortCulturesForReader(['ja', 'en', 'nl', 'de'], 'de'))
      .toEqual(['de', 'en', 'nl', 'ja']);
  });

  it("matches the reader's language by its language subtag, not the whole tag", () => {
    // A reader on de-AT still wants German at the top, and the exact tag wins among several German ones.
    expect(sortCulturesForReader(['en', 'de', 'de-AT'], 'de-AT'))
      .toEqual(['de-AT', 'de', 'en']);
  });

  it('collapses the two leading rules into one for an English reader', () => {
    expect(sortCulturesForReader(['nl', 'de', 'en'], 'en')).toEqual(['en', 'de', 'nl']);
  });

  it('never reorders the caller\'s array in place', () => {
    const original = ['nl', 'de', 'en'];
    sortCulturesForReader(original, 'de');
    expect(original).toEqual(['nl', 'de', 'en']);
  });

  it('keeps a language the platform cannot name, ordered by its tag', () => {
    expect(sortCulturesForReader(['qya', 'en'], 'en')).toEqual(['en', 'qya']);
  });
});
