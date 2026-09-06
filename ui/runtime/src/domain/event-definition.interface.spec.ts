import { EventDefinition, resolveEventPayloadParameter } from './event-definition.interface';

const folderChanged: EventDefinition = {
  id: 'macro-deck::folder-changed',
  providerId: 'macro-deck',
  providerName: 'Macro Deck',
  isIntegration: false,
  name: 'Folder Changed',
  deliveryKind: 'push',
  configurationParameters: [],
  payloadParameters: [
    { name: 'deviceId', type: 'dynamic-choice', label: 'Device', dynamicOptions: true, optionsSourceId: 'macrodeck.devices' },
    { name: 'note', type: 'string', label: 'Note' },
  ],
};

describe('resolveEventPayloadParameter', () => {
  it('resolves the stored object reference to its matching payload parameter', () => {
    const resolved = resolveEventPayloadParameter(folderChanged, { $event: 'deviceId' });

    expect(resolved).toEqual(folderChanged.payloadParameters[0]);
  });

  it('resolves a lone typed token the same way as the stored object form', () => {
    const resolved = resolveEventPayloadParameter(folderChanged, '{{ event.deviceId }}');

    expect(resolved).toEqual(folderChanged.payloadParameters[0]);
  });

  it('does not resolve a $var reference, even one named the same as a payload parameter', () => {
    expect(resolveEventPayloadParameter(folderChanged, { $var: 'deviceId' })).toBeUndefined();
  });

  it('does not resolve a variable token, only an event token', () => {
    expect(resolveEventPayloadParameter(folderChanged, '{{ vars.deviceId }}')).toBeUndefined();
  });

  it('does not resolve a token mixed with other text', () => {
    expect(resolveEventPayloadParameter(folderChanged, 'prefix {{ event.deviceId }} suffix')).toBeUndefined();
  });

  it('does not resolve a plain literal', () => {
    expect(resolveEventPayloadParameter(folderChanged, 'deviceId')).toBeUndefined();
    expect(resolveEventPayloadParameter(folderChanged, 42)).toBeUndefined();
    expect(resolveEventPayloadParameter(folderChanged, true)).toBeUndefined();
    expect(resolveEventPayloadParameter(folderChanged, null)).toBeUndefined();
    expect(resolveEventPayloadParameter(folderChanged, undefined)).toBeUndefined();
  });

  it('does not resolve a name the event does not declare in its payload', () => {
    expect(resolveEventPayloadParameter(folderChanged, { $event: 'unknownField' })).toBeUndefined();
  });

  it('does not resolve without a definition at all', () => {
    expect(resolveEventPayloadParameter(undefined, { $event: 'deviceId' })).toBeUndefined();
  });

  it('resolves a name only from the payload list, never the configuration list', () => {
    const definition: EventDefinition = {
      ...folderChanged,
      configurationParameters: [{ name: 'configOnly', type: 'string', label: 'Config only' }],
    };

    expect(resolveEventPayloadParameter(definition, { $event: 'configOnly' })).toBeUndefined();
  });
});
