import { applyUiPatch, UiPatch, UiPatchOperations } from './ui-patch';
import { UiNode } from './ui-node.interface';

// A patch that cannot fully apply must leave the caller's tree exactly as it was. The failure worth
// guarding is not "it reported a problem" but "it reported a problem after already having applied the
// operations that came before the bad one" - so every case here puts a valid operation FIRST and
// asserts the node it would have changed is untouched.
describe('applyUiPatch', () => {
  const tree = (): UiNode => ({
    id: 'root',
    type: 'stack',
    children: [
      { id: 'field.a', type: 'string-input', properties: { label: 'A' } },
      { id: 'field.b', type: 'string-input', properties: { label: 'B' } },
    ],
  });

  const labelOf = (node: UiNode | null, id: string): unknown =>
    node?.children?.find((child) => child.id === id)?.properties?.['label'];

  it('applies every operation in order when the whole patch is applicable', () => {
    const root = tree();

    const result = applyUiPatch(root, 1, {
      fromRevision: 1,
      toRevision: 2,
      operations: [
        { op: UiPatchOperations.SetProperties, nodeId: 'field.a', properties: { label: 'A2' } },
        { op: UiPatchOperations.SetProperties, nodeId: 'field.b', properties: { label: 'B2' } },
      ],
    });

    expect(labelOf(result, 'field.a')).toBe('A2');
    expect(labelOf(result, 'field.b')).toBe('B2');
  });

  const rejected: { name: string; patch: UiPatch }[] = [
    {
      name: 'a later operation targets a node that does not exist',
      patch: {
        fromRevision: 1,
        toRevision: 2,
        operations: [
          { op: UiPatchOperations.SetProperties, nodeId: 'field.a', properties: { label: 'CHANGED' } },
          { op: UiPatchOperations.SetProperties, nodeId: 'field.MISSING', properties: { label: 'x' } },
        ],
      },
    },
    {
      name: 'a later operation names an unknown op',
      patch: {
        fromRevision: 1,
        toRevision: 2,
        operations: [
          { op: UiPatchOperations.SetProperties, nodeId: 'field.a', properties: { label: 'CHANGED' } },
          { op: 'set-everything', nodeId: 'field.b', properties: { label: 'x' } },
        ],
      },
    },
    {
      name: 'the patch starts from a revision the tree is not at',
      patch: {
        fromRevision: 5,
        toRevision: 6,
        operations: [
          { op: UiPatchOperations.SetProperties, nodeId: 'field.a', properties: { label: 'CHANGED' } },
        ],
      },
    },
    {
      name: 'the patch carries no operations',
      patch: { fromRevision: 1, toRevision: 2, operations: [] },
    },
  ];

  for (const { name, patch } of rejected) {
    it(`discards the whole patch and leaves the tree untouched when ${name}`, () => {
      const root = tree();

      const result = applyUiPatch(root, 1, patch);

      expect(result).toBeNull();
      // The assertion that matters: the first, individually-valid operation must not have stuck.
      expect(labelOf(root, 'field.a')).toBe('A');
      expect(labelOf(root, 'field.b')).toBe('B');
    });
  }
});
