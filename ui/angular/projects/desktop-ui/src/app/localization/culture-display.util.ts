export function cultureDisplayName(culture: string): string {
  try {
    const name = new Intl.DisplayNames([culture], { type: 'language' }).of(culture);
    if (!name || name.toLowerCase() === culture.toLowerCase()) {
      return culture;
    }

    return name.charAt(0).toUpperCase() + name.slice(1);
  } catch {
    return culture;
  }
}

function languageOf(culture: string): string {
  return culture.split('-')[0].toLowerCase();
}

export function sortCulturesForReader(cultures: readonly string[], culture: string): string[] {
  const readerLanguage = languageOf(culture);

  const rank = (candidate: string): number => {
    if (candidate.toLowerCase() === culture.toLowerCase()) return 0;
    if (languageOf(candidate) === readerLanguage) return 1;
    if (languageOf(candidate) === 'en') return 2;
    return 3;
  };

  return [...cultures].sort((left, right) => {
    const difference = rank(left) - rank(right);
    if (difference !== 0) return difference;

    return cultureDisplayName(left).localeCompare(cultureDisplayName(right), culture);
  });
}
