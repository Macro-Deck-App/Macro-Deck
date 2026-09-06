const PLACEHOLDER_PATTERN = /\{\{|\}\}|\{([A-Za-z_][A-Za-z0-9_]*)\}/g;

export function formatTemplate(template: string, args?: Record<string, unknown>): string {
  return template.replace(PLACEHOLDER_PATTERN, (match, name?: string) => {
    if (match === '{{') return '{';
    if (match === '}}') return '}';

    const value = args ? args[name!] : undefined;
    return value === undefined || value === null ? match : formatArgumentValue(value);
  });
}

function formatArgumentValue(value: unknown): string {
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'string') return value;
  return String(value);
}
