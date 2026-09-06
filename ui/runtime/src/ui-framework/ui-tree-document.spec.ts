import { rootNodeOf } from './ui-tree-document';

describe('rootNodeOf', () => {
  const node = { id: 'n1', type: 'ui.stack' };

  it('takes the root out of a tree document', () => {
    expect(rootNodeOf({ revision: 3, surface: { kind: 'widget' }, root: node })).toEqual(node as never);
  });

  it('accepts a bare node, which some exchanges send instead', () => {
    expect(rootNodeOf(node)).toEqual(node as never);
  });

  it('answers null for a document whose root is not a node', () => {
    expect(rootNodeOf({ revision: 1, root: 'nope' })).toBeNull();
    expect(rootNodeOf({ revision: 1, root: { id: 'n1' } })).toBeNull();
    expect(rootNodeOf({ revision: 1, root: { type: 'ui.stack' } })).toBeNull();
  });

  it('answers null rather than handing back something unrenderable', () => {
    expect(rootNodeOf(undefined)).toBeNull();
    expect(rootNodeOf(null)).toBeNull();
    expect(rootNodeOf('tree')).toBeNull();
    expect(rootNodeOf({ revision: 1, surface: {} })).toBeNull();
  });
});
