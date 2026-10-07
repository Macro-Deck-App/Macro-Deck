import type { Variable } from '@macro-deck/runtime';
import { liquidCompletions } from './liquid-completion';

function variable(name: string, value = '', origin?: 'event'): Variable {
  return { id: name, name, scope: 'global', type: 'text', classification: 'user', value, origin };
}

const variables = [
  variable('weather_temperature', '21'),
  variable('weather_condition', 'sunny'),
  variable('cpu', '12'),
  variable('button_id', '', 'event'),
];

function at(template: string): { doc: string; pos: number } {
  const pos = template.indexOf('|caret|');
  return { doc: template.replace('|caret|', ''), pos };
}

function complete(template: string) {
  const { doc, pos } = at(template);
  return liquidCompletions(doc, pos, variables);
}

describe('liquidCompletions', () => {
  it('offers matching variables after vars. and replaces what was typed', () => {
    const result = complete('CPU {{ vars.wea|caret| }}');

    expect(result!.items.map(item => item.label)).toEqual(['vars.weather_temperature', 'vars.weather_condition']);
    expect(result!.items[0].insert).toBe('vars.weather_temperature');
    expect(result!.items[0].detail).toBe('21');
    expect(result!.from).toBe('CPU {{ '.length);
  });

  it('offers every variable right after an opened output tag and closes the tag when it is still open', () => {
    const result = complete('{{ |caret|');

    expect(result!.items.map(item => item.label)).toContain('vars.cpu');
    expect(result!.items.find(item => item.label === 'vars.cpu')!.insert).toBe('vars.cpu }}');
  });

  it('offers only event parameters after event.', () => {
    const result = complete('{{ event.|caret| }}');

    expect(result!.items.map(item => item.label)).toEqual(['event.button_id']);
  });

  it('offers matching filters after a pipe without repeating the pipe', () => {
    const result = complete('{{ vars.cpu | ro|caret| }}');

    const round = result!.items.find(item => item.label === 'round')!;
    expect(round.kind).toBe('filter');
    expect(round.insert).toBe('round: 2');
    expect(round.hintKey).toBeTruthy();
    expect(result!.items.every(item => item.label.includes('ro'))).toBeTrue();
  });

  it('offers logic blocks after {% and replaces the opened tag with the whole block', () => {
    const result = complete('{% if|caret|');

    const ifElse = result!.items.find(item => item.label === 'if / else')!;
    expect(ifElse.kind).toBe('control');
    expect(result!.from).toBe(0);
    expect(ifElse.insert.startsWith('{% if ')).toBeTrue();
    expect(ifElse.insert.slice(0, ifElse.caret)).toBe('{% if ');
  });

  it('does not close a logic tag with output braces when completing a variable inside it', () => {
    const result = complete('{% if vars.cp|caret|');

    expect(result!.items.map(item => item.insert)).toEqual(['vars.cpu']);
  });

  it('stays quiet in plain text outside any tag', () => {
    expect(complete('Temperature vars.cpu|caret|')).toBeNull();
  });

  it('keeps suggesting while vars is typed without the dot yet', () => {
    for (const typed of ['{{ v|caret|', '{{ vars|caret|', '{{ vars.|caret|']) {
      const result = complete(typed);
      expect(result!.items.map(item => item.label)).withContext(typed).toContain('vars.cpu');
    }
    const replaced = complete('{{ vars|caret|')!;
    expect(replaced.from).toBe('{{ '.length);
    expect(replaced.items.find(item => item.label === 'vars.cpu')!.insert).toBe('vars.cpu }}');
  });

  it('suggests variables by name inside a logic tag', () => {
    const result = complete('{% if vars|caret|');

    expect(result!.items.map(item => item.insert)).toContain('vars.cpu');
    expect(result!.from).toBe('{% if '.length);
  });

  it('matches a variable by its name alone', () => {
    const result = complete('{{ condi|caret| }}');

    expect(result!.items.map(item => item.insert)).toEqual(['vars.weather_condition']);
  });

  it('does not open the variable list for Liquid keywords', () => {
    expect(complete('{% if vars.cpu > 1 and|caret|')).toBeNull();
    expect(complete('{% for item in|caret|')).toBeNull();
  });
});
