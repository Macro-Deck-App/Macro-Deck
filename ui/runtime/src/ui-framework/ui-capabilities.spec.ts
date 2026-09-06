import { UI_COMPONENTS_WELL_KNOWN } from '../ui-components/ui-component-types';
import { UI_MACRODECK_COMPONENTS_WELL_KNOWN } from '../macrodeck-components/macrodeck-component-types';
import { UI_CONFIG_PRIMITIVES_WELL_KNOWN } from '../ui-config/config-primitives';
import { isComponentProfileType } from './ui-capabilities';

describe('isComponentProfileType', () => {
  it('claims every component the framework and Macro Deck ship', () => {
    for (const type of [...UI_COMPONENTS_WELL_KNOWN, ...UI_MACRODECK_COMPONENTS_WELL_KNOWN]) {
      expect(isComponentProfileType(type)).withContext(type).toBeTrue();
    }
  });

  it('claims a component nobody has registered, which is the point of asking', () => {
    // The caller is deciding how to draw a type it could not resolve. A registry lookup cannot answer
    // that question, because an unsupported type is by construction absent from the registry.
    expect(isComponentProfileType('ui.hologram')).toBeTrue();
    expect(isComponentProfileType('macrodeck.sparkline')).toBeTrue();
  });

  it('disclaims the configuration vocabulary, which carries no namespace', () => {
    for (const type of UI_CONFIG_PRIMITIVES_WELL_KNOWN) {
      expect(isComponentProfileType(type)).withContext(type).toBeFalse();
    }
  });

  it('disclaims a namespace that merely contains one of ours', () => {
    expect(isComponentProfileType('plugin.ui.gauge')).toBeFalse();
    expect(isComponentProfileType('acme.macrodeck.dial')).toBeFalse();
    expect(isComponentProfileType('uiplugin.gauge')).toBeFalse();
  });
});
