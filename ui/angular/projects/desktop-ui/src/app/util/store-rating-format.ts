export function formatStoreRating(value: number, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value);
  } catch {
    return value.toFixed(1);
  }
}
