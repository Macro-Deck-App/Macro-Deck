import { ActionParameterType } from '@macro-deck/runtime';
import { mapActionParamType } from './action-parameter-mapping.util';

describe('mapActionParamType', () => {
  it('translates every wire type to a distinct control', () => {
    const wireTypes = Object.values(ActionParameterType);
    const mapped = wireTypes.map(type => mapActionParamType(type));

    expect(new Set(mapped).size).toBe(wireTypes.length);
  });

  it('maps the widget target to its own picker', () => {
    expect(mapActionParamType(ActionParameterType.WidgetTarget)).toBe('widget-target');
  });

  it('falls back to text for a type this client does not know yet', () => {
    expect(mapActionParamType('SomethingNewerThanThisBuild')).toBe('string');
  });
});
