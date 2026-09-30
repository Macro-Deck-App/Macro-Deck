const PACKAGE_ID = /^[a-z][a-z0-9]*(-[a-z0-9]+)*(\.[a-z][a-z0-9]*(-[a-z0-9]+)*)+$/;

const MAX_PACKAGE_ID_LENGTH = 128;

export function isStorePackageId(value: unknown): value is string {
  return typeof value === 'string' && value.length <= MAX_PACKAGE_ID_LENGTH && PACKAGE_ID.test(value);
}
