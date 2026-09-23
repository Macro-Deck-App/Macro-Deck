interface ParsedVersion {
  core: [number, number, number];
  prerelease: string[];
}

const IDENTIFIER = '(?:0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*)';
const SEMVER = new RegExp(
  `^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-(${IDENTIFIER}(?:\\.${IDENTIFIER})*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$`);

function parse(value: string | null | undefined): ParsedVersion | null {
  const match = SEMVER.exec(value ?? '');
  if (!match) {
    return null;
  }
  return {
    core: [Number(match[1]), Number(match[2]), Number(match[3])],
    prerelease: match[4] ? match[4].split('.') : [],
  };
}

function compareIdentifiers(left: string, right: string): number {
  const leftNumeric = /^\d+$/.test(left);
  const rightNumeric = /^\d+$/.test(right);
  if (leftNumeric && rightNumeric) {
    return Math.sign(Number(left) - Number(right));
  }
  if (leftNumeric !== rightNumeric) {
    return leftNumeric ? -1 : 1;
  }
  return left < right ? -1 : left > right ? 1 : 0;
}

// Null when either side does not parse: an ordering between unparseable versions would be a guess.
export function compareVersions(left: string | null | undefined, right: string | null | undefined): number | null {
  const a = parse(left);
  const b = parse(right);
  if (!a || !b) {
    return null;
  }

  for (let index = 0; index < 3; index++) {
    const difference = a.core[index] - b.core[index];
    if (difference !== 0) {
      return Math.sign(difference);
    }
  }

  if (a.prerelease.length === 0 || b.prerelease.length === 0) {
    return a.prerelease.length === b.prerelease.length ? 0 : a.prerelease.length === 0 ? 1 : -1;
  }

  const length = Math.max(a.prerelease.length, b.prerelease.length);
  for (let index = 0; index < length; index++) {
    if (index >= a.prerelease.length) {
      return -1;
    }
    if (index >= b.prerelease.length) {
      return 1;
    }
    const result = compareIdentifiers(a.prerelease[index], b.prerelease[index]);
    if (result !== 0) {
      return result;
    }
  }
  return 0;
}

// Mirrors the host's StoreVersions.Same, so the UI and the installer agree on "same".
export function sameVersion(left: string | null | undefined, right: string | null | undefined): boolean {
  const order = compareVersions(left, right);
  if (order !== null) {
    return order === 0;
  }
  return (left ?? '').toLowerCase() === (right ?? '').toLowerCase();
}
