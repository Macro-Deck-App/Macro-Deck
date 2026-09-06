import { ActionBlockParameter } from '@macro-deck/runtime';
import { isParameterVisible } from './parameter-visibility.util';

function param(
  name: string,
  value: ActionBlockParameter['value'],
  visibleWhen?: ActionBlockParameter['visibleWhen'],
): ActionBlockParameter {
  return { name, type: 'string', label: name, value, visibleWhen };
}

describe('isParameterVisible', () => {
  it('shows a parameter with no condition', () => {
    const siblings = [param('authType', 'none')];
    expect(isParameterVisible(param('other', ''), siblings)).toBeTrue();
  });

  it('shows a parameter whose sibling holds a listed value', () => {
    const target = param('headerName', '', { parameterName: 'authType', values: ['header'] });
    const siblings = [param('authType', 'header'), target];
    expect(isParameterVisible(target, siblings)).toBeTrue();
  });

  it('hides a parameter whose sibling holds an unlisted value', () => {
    const target = param('headerName', '', { parameterName: 'authType', values: ['header'] });
    const siblings = [param('authType', 'none'), target];
    expect(isParameterVisible(target, siblings)).toBeFalse();
  });

  it('matches any of several listed values', () => {
    const target = param('secret', '', { parameterName: 'authType', values: ['basic', 'bearer', 'header'] });
    for (const value of ['basic', 'bearer', 'header']) {
      expect(isParameterVisible(target, [param('authType', value), target])).toBeTrue();
    }
    expect(isParameterVisible(target, [param('authType', 'none'), target])).toBeFalse();
  });

  it('compares values case-insensitively', () => {
    const target = param('jsonBody', '', { parameterName: 'bodyType', values: ['json'] });
    expect(isParameterVisible(target, [param('bodyType', 'JSON'), target])).toBeTrue();
  });

  // A typo in an integration must not make a field permanently unreachable.
  it('shows a parameter whose condition names no sibling', () => {
    const target = param('orphan', '', { parameterName: 'doesNotExist', values: ['x'] });
    expect(isParameterVisible(target, [param('authType', 'none'), target])).toBeTrue();
  });

  it('shows a parameter whose sibling is bound to a variable', () => {
    const target = param('jsonBody', '', { parameterName: 'bodyType', values: ['json'] });
    const siblings = [param('bodyType', { $var: 'chosen_body' }), target];
    expect(isParameterVisible(target, siblings)).toBeTrue();
  });

  it('treats a missing sibling list as visible', () => {
    const target = param('jsonBody', '', { parameterName: 'bodyType', values: ['json'] });
    expect(isParameterVisible(target, undefined)).toBeTrue();
  });
});
