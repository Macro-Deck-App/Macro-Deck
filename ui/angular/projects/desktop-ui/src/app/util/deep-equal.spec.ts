import { deepEqual } from './deep-equal';

describe('deepEqual', () => {
  it('compares primitives by value', () => {
    expect(deepEqual(1, 1)).toBeTrue();
    expect(deepEqual('a', 'a')).toBeTrue();
    expect(deepEqual(null, null)).toBeTrue();
    expect(deepEqual(1, '1')).toBeFalse();
    expect(deepEqual(null, undefined)).toBeFalse();
    expect(deepEqual(Number.NaN, Number.NaN)).toBeTrue();
  });

  it('compares nested objects and arrays structurally', () => {
    const flows = [{ triggerType: 'onPress', children: [{ id: 'a', parameters: { x: 1 } }] }];
    expect(deepEqual(flows, structuredClone(flows))).toBeTrue();
    expect(deepEqual(flows, [{ triggerType: 'onPress', children: [{ id: 'a', parameters: { x: 2 } }] }]))
      .toBeFalse();
  });

  it('ignores key order', () => {
    expect(deepEqual({ label: 'a', color: '#fff' }, { color: '#fff', label: 'a' })).toBeTrue();
  });

  it('treats a property set to undefined as absent', () => {
    expect(deepEqual({ label: 'a', instanceId: undefined }, { label: 'a' })).toBeTrue();
    expect(deepEqual({ label: 'a' }, { label: 'a', instanceId: '1' })).toBeFalse();
  });

  it('does not treat an array as equal to an object', () => {
    expect(deepEqual([], {})).toBeFalse();
    expect(deepEqual([1, 2], [1, 2, 3])).toBeFalse();
  });

  it('compares dates by their instant', () => {
    expect(deepEqual(new Date(5), new Date(5))).toBeTrue();
    expect(deepEqual(new Date(5), new Date(6))).toBeFalse();
    expect(deepEqual(new Date(5), 5)).toBeFalse();
  });
});
