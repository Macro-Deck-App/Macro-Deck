import { isOperationAllowed, operationOptionsFor, operationsFor } from './set-variable-operations.util';

describe('set-variable-operations.util', () => {
  it('offers only what a boolean can do', () => {
    expect(operationsFor('boolean')).toEqual(['set', 'toggle']);
  });

  it('offers only what a number can do', () => {
    expect(operationsFor('numeric')).toEqual(['set', 'add']);
  });

  it('offers only what text can do', () => {
    expect(operationsFor('text')).toEqual(['set', 'append']);
  });

  it('offers everything while the type is unknown', () => {
    expect(operationsFor(undefined)).toEqual(['set', 'add', 'toggle', 'append']);
  });

  it('labels every option it offers', () => {
    const options = operationOptionsFor('numeric');

    expect(options).toEqual([
      { value: 'set', label: 'Set to' },
      { value: 'add', label: 'Add' },
    ]);
  });

  it('rejects an operation the type cannot perform', () => {
    expect(isOperationAllowed('append', 'numeric')).toBeFalse();
    expect(isOperationAllowed('toggle', 'text')).toBeFalse();
    expect(isOperationAllowed('set', 'boolean')).toBeTrue();
  });

  it('accepts anything while the type is unknown', () => {
    expect(isOperationAllowed('append', undefined)).toBeTrue();
  });
});
