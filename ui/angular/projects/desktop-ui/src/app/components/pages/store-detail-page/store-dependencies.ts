import { StoreDependenciesBody, StoreDependencyPackageBody } from '@macro-deck/runtime';

export function dependencyPackageCount(dependencies: StoreDependenciesBody | null | undefined): number | null {
  if (!dependencies) {
    return null;
  }
  const ids = new Set<string>();
  for (const framework of dependencies.frameworks) {
    for (const entry of framework.packages) {
      ids.add(entry.id.toLowerCase());
    }
  }
  return ids.size;
}

export function splitPackages(packages: readonly StoreDependencyPackageBody[]): {
  direct: StoreDependencyPackageBody[];
  transitive: StoreDependencyPackageBody[];
} {
  return {
    direct: packages.filter(entry => entry.direct),
    transitive: packages.filter(entry => !entry.direct),
  };
}

export function packageUrl(
  dependencies: StoreDependenciesBody,
  entry: StoreDependencyPackageBody,
): string | null {
  const ecosystem = dependencies.ecosystem ?? 'nuget';
  return ecosystem === 'nuget'
    ? `https://www.nuget.org/packages/${encodeURIComponent(entry.id)}/${encodeURIComponent(entry.version)}`
    : null;
}
