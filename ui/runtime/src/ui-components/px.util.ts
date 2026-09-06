export function px(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : `${value}px`;
}
