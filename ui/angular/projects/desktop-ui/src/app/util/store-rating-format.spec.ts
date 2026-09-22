import { formatStoreInstallCount } from './store-rating-format';

describe('formatStoreInstallCount', () => {
  it('abbreviates large counts in the reader\'s language', () => {
    expect(formatStoreInstallCount(1234, 'en')).toBe('1.2K');
    expect(formatStoreInstallCount(342000, 'en')).toBe('342K');
  });

  it('groups a count the language does not abbreviate instead of running the digits together', () => {
    expect(formatStoreInstallCount(1234, 'de')).toBe('1.234');
    expect(formatStoreInstallCount(5600, 'de')).toBe('5.600');
  });

  it('leaves small counts alone', () => {
    expect(formatStoreInstallCount(12, 'en')).toBe('12');
    expect(formatStoreInstallCount(1, 'de')).toBe('1');
  });
});
