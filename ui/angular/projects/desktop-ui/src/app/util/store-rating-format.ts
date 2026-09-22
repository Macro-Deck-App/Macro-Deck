export function formatStoreRating(value: number, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value);
  } catch {
    return value.toFixed(1);
  }
}

export function formatStoreCount(value: number, locale: string): string {
  try {
    return new Intl.NumberFormat(locale).format(value);
  } catch {
    return String(value);
  }
}

export function formatStoreInstallCount(value: number, locale: string): string {
  try {
    const compact = new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
    const grouped = formatStoreCount(value, locale);
    return compact.replace(/\D/gu, '') === String(value) ? grouped : compact;
  } catch {
    return String(value);
  }
}
