import { ActionBlockParameter, ActionParameterDef } from '@macro-deck/runtime';
import { isFieldVisible, isParameterVisible } from './parameter-visibility.util';

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

describe('isFieldVisible', () => {
  const brand: Pick<ActionParameterDef, 'name' | 'visibleWhen'> = { name: 'brand' };
  const model: Pick<ActionParameterDef, 'name' | 'visibleWhen'> = {
    name: 'model',
    visibleWhen: { parameterName: 'brand', values: ['option1'] },
  };
  const fields = [brand, model];

  it('shows a field with no condition', () => {
    expect(isFieldVisible(brand, fields, {})).toBeTrue();
  });

  it('shows a field whose referenced field holds a listed value', () => {
    expect(isFieldVisible(model, fields, { brand: 'option1' })).toBeTrue();
  });

  it('hides a field whose referenced field holds another value', () => {
    expect(isFieldVisible(model, fields, { brand: 'option2' })).toBeFalse();
  });

  it('hides a field whose referenced field has no value yet', () => {
    expect(isFieldVisible(model, fields, {})).toBeFalse();
  });

  it('compares values case-insensitively', () => {
    expect(isFieldVisible(model, fields, { brand: 'OPTION1' })).toBeTrue();
  });

  it('shows a field whose condition names no field of the step', () => {
    expect(isFieldVisible(model, [model], { brand: 'option2' })).toBeTrue();
  });
});
