export function containsLiquid(value: string | null | undefined): boolean {
  return !!value && (value.includes('{{') || value.includes('{%'));
}
