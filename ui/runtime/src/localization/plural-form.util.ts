export function pluralForm(count: unknown): 'One' | 'Other' {
  return toNumber(count) === 1 ? 'One' : 'Other';
}

function toNumber(count: unknown): number | undefined {
  if (typeof count === 'number') return count;
  if (typeof count === 'string') {
    const parsed = Number(count);
    return Number.isNaN(parsed) ? undefined : parsed;
  }
  return undefined;
}
