import { UiNode } from './ui-node.interface';
import {
  LocalizationTranslator,
  nodeBoolean,
  nodeNumber,
  nodeOptions,
  nodeRaw,
  nodeString,
  nodeText,
} from './node-properties.util';

function fakeLocalization(): LocalizationTranslator {
  return {
    translate: (scope, key, args) =>
      args ? `${scope}:${key}(${JSON.stringify(args)})` : `${scope}:${key}`,
  };
}

describe('ui-node-properties.util', () => {
  it('never coerces a wrongly-typed property: a string where a number is expected yields undefined', () => {
    const node: UiNode = { id: 'n', type: 'number', properties: { rotation: '37.5' } };

    expect(nodeNumber(node, 'rotation')).toBeUndefined();
  });

  it('ignores an unknown property key without affecting reads of known ones', () => {
    const node: UiNode = {
      id: 'n',
      type: 'string',
      properties: { label: 'Name', someFutureKey: 'y', value: 'hello' },
    };

    expect(nodeString(node, 'label')).toBe('Name');
    expect(nodeString(node, 'value')).toBe('hello');
    expect(nodeRaw(node, 'someFutureKey')).toBe('y');
  });

  it('never throws on a missing properties map', () => {
    const node: UiNode = { id: 'n', type: 'string' };

    expect(() => nodeString(node, 'label')).not.toThrow();
    expect(nodeString(node, 'label')).toBeUndefined();
    expect(nodeBoolean(node, 'required')).toBeUndefined();
  });

  it('never throws on a null or undefined node', () => {
    expect(nodeString(null, 'label')).toBeUndefined();
    expect(nodeString(undefined, 'label')).toBeUndefined();
  });
});

describe('nodeText', () => {
  it('renders a plain-string property unchanged', () => {
    const node: UiNode = { id: 'n', type: 'string', properties: { label: 'Save' } };

    expect(nodeText(node, 'label', fakeLocalization())).toBe('Save');
  });

  it('resolves a $localized reference through the given translator, arguments included', () => {
    const node: UiNode = {
      id: 'n',
      type: 'string',
      properties: {
        label: { $localized: { scope: 'plugin:com.example.spotify', key: 'Configuration.Title', arguments: { userName: 'ada' } } },
      },
    };

    expect(nodeText(node, 'label', fakeLocalization())).toBe(
      'plugin:com.example.spotify:Configuration.Title({"userName":"ada"})',
    );
  });

  it('yields undefined for neither a string nor a $localized shape', () => {
    const node: UiNode = { id: 'n', type: 'string', properties: { label: 42 } };

    expect(nodeText(node, 'label', fakeLocalization())).toBeUndefined();
  });

  it('never throws on a missing properties map or a malformed $localized reference', () => {
    const node: UiNode = { id: 'n', type: 'string', properties: { label: { $localized: { scope: 'macrodeck' } } } };

    expect(() => nodeText(node, 'label', fakeLocalization())).not.toThrow();
    expect(nodeText(node, 'label', fakeLocalization())).toBeUndefined();
    expect(nodeText(undefined, 'label', fakeLocalization())).toBeUndefined();
  });
});

describe('nodeOptions', () => {
  it('resolves a $localized option label through the translator', () => {
    const node: UiNode = {
      id: 'n',
      type: 'choice',
      properties: {
        options: [{ value: 'red', label: { $localized: { scope: 'macrodeck', key: 'Color.Red' } } }],
      },
    };

    expect(nodeOptions(node, fakeLocalization())).toEqual([{ value: 'red', label: 'macrodeck:Color.Red' }]);
  });

  it('keeps a plain-string label unchanged', () => {
    const node: UiNode = { id: 'n', type: 'choice', properties: { options: [{ value: 'red', label: 'Red' }] } };

    expect(nodeOptions(node, fakeLocalization())).toEqual([{ value: 'red', label: 'Red' }]);
  });

  it('omits the label (falling back to value at the call site) when neither shape applies', () => {
    const node: UiNode = { id: 'n', type: 'choice', properties: { options: [{ value: 'red' }] } };

    const options = nodeOptions(node, fakeLocalization());

    expect(options).toEqual([{ value: 'red' }]);
    expect(options![0].label).toBeUndefined();
  });
});
