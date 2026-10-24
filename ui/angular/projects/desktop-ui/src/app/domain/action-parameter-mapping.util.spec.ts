import { ActionParameterType } from '@macro-deck/runtime';
import { mapActionParamType, mapActionParameterDef } from './action-parameter-mapping.util';

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

describe('mapActionParameterDef', () => {
  const localization = { translate: (_scope: string, key: string) => key };

  it('carries the language and variable insert format of a code parameter to the editor row', () => {
    const mapped = mapActionParameterDef({
      name: 'code',
      type: ActionParameterType.Code,
      description: 'Code',
      language: 'autohotkey',
      variableInsertFormat: 'MacroDeck.GetVar("{name}")',
    }, localization);

    expect(mapped.language).toBe('autohotkey');
    expect(mapped.variableInsertFormat).toBe('MacroDeck.GetVar("{name}")');
  });

  it('leaves the insert format unset for a parameter from an older plugin that does not send it', () => {
    const mapped = mapActionParameterDef({
      name: 'code',
      type: ActionParameterType.Code,
      description: 'Code',
      language: 'lua',
    }, localization);

    expect(mapped.variableInsertFormat).toBeUndefined();
  });
});
