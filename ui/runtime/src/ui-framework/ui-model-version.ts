export const UiModelVersions = {
  Minimum: 3,
  Current: 4,
} as const;

export function supportsUiModelVersion(version: number): boolean {
  return version >= UiModelVersions.Minimum && version <= UiModelVersions.Current;
}
