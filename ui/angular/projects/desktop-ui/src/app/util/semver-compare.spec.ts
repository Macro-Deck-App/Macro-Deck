import { compareVersions, sameVersion } from './semver-compare';

describe('semver-compare', () => {
  it('orders by core version', () => {
    expect(compareVersions('1.4.0', '1.3.9')).toBe(1);
    expect(compareVersions('1.3.0', '1.10.0')).toBe(-1);
    expect(compareVersions('2.0.0', '2.0.0')).toBe(0);
  });

  it('ranks a pre-release below its release and compares numeric identifiers numerically', () => {
    expect(compareVersions('3.0.0-beta.11', '3.0.0')).toBe(-1);
    expect(compareVersions('3.0.0-beta.11', '3.0.0-beta.12')).toBe(-1);
    expect(compareVersions('3.0.0-beta.9', '3.0.0-beta.10')).toBe(-1);
    expect(compareVersions('1.0.0-alpha', '1.0.0-alpha.1')).toBe(-1);
    expect(compareVersions('1.0.0-alpha.beta', '1.0.0-alpha.1')).toBe(1);
  });

  it('ignores build metadata when ordering', () => {
    expect(compareVersions('1.0.0+abc', '1.0.0+def')).toBe(0);
  });

  it('refuses to order versions it cannot parse', () => {
    expect(compareVersions('1.0', '1.0.0')).toBeNull();
    expect(compareVersions('1.0.0-01', '1.0.0')).toBeNull();
    expect(compareVersions('1.0.0-a..b', '1.0.0')).toBeNull();
    expect(compareVersions('latest', '1.0.0')).toBeNull();
  });

  it('treats unparseable versions as the same only when the text matches ignoring case', () => {
    expect(sameVersion('1.0', '1.0.0')).toBeFalse();
    expect(sameVersion('Nightly', 'nightly')).toBeTrue();
    expect(sameVersion('1.2.3', '1.2.3+build')).toBeTrue();
  });
});
