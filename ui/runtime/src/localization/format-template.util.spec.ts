import { formatTemplate } from './format-template.util';

describe('formatTemplate', () => {
  it('substitutes a named placeholder from the arguments', () => {
    expect(formatTemplate('Connected as {userName}', { userName: 'ada' })).toBe('Connected as ada');
  });

  it('unescapes doubled braces to a literal brace without treating them as a placeholder', () => {
    expect(formatTemplate('{{userName}}', { userName: 'ada' })).toBe('{userName}');
  });

  it('leaves a placeholder with no supplied argument as its literal text', () => {
    expect(formatTemplate('Connected as {userName}', {})).toBe('Connected as {userName}');
    expect(formatTemplate('Connected as {userName}')).toBe('Connected as {userName}');
  });

  it('formats a boolean argument as lowercase true/false, never the JS default', () => {
    expect(formatTemplate('Enabled: {value}', { value: true })).toBe('Enabled: true');
    expect(formatTemplate('Enabled: {value}', { value: false })).toBe('Enabled: false');
  });

  it('formats a number via JS number-to-string, with no locale thousands separator or decimal mark', () => {
    expect(formatTemplate('Limit: {value}', { value: 1234.5 })).toBe('Limit: 1234.5');
    expect(formatTemplate('Limit: {value}', { value: 1000000 })).toBe('Limit: 1000000');
  });

  it('substitutes a string argument verbatim', () => {
    expect(formatTemplate('{field} is required', { field: 'Name' })).toBe('Name is required');
  });

  it('substitutes multiple distinct placeholders independently', () => {
    expect(formatTemplate('{a} and {b}', { a: 'x', b: 'y' })).toBe('x and y');
  });
});
