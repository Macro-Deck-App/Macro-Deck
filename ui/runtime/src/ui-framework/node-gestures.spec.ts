import { activationClaim, treeClaimsGesture } from './node-gestures';
import { UiNode } from './ui-node.interface';

function node(id: string, type: string, properties: Record<string, unknown> = {}, children: UiNode[] = []): UiNode {
  return { id, type, properties, children };
}

const disabledRegion = (children: UiNode[] = []) => node('region', 'ui.stack', { modifiers: { disabled: true } }, children);

describe('node gesture walks', () => {
  describe('the tile pointer claim', () => {
    it('leaves a tree that declares nothing to the tile', () => {
      expect(treeClaimsGesture(node('root', 'ui.stack', {}, [node('text', 'ui.text')]))).toBeFalse();
    });

    it('is taken by a tree that only drags, swipes or pinches', () => {
      for (const name of ['drag', 'drag-end', 'swipe', 'pinch', 'pinch-end']) {
        const tree = node('root', 'ui.stack', {}, [node('surface', 'ui.modifier', { events: [name] })]);
        expect(treeClaimsGesture(tree)).withContext(name).toBeTrue();
      }
    });

    it('is absorbed by a disabled region even when nothing enabled claims it', () => {
      expect(treeClaimsGesture(node('root', 'ui.stack', {}, [disabledRegion()]))).toBeTrue();
    });
  });

  describe('activation', () => {
    it('reaches an enabled press node after a disabled region', () => {
      const button = node('button', 'ui.button', { events: ['press'] });
      const tree = node('root', 'ui.stack', {}, [disabledRegion([node('inner', 'ui.button', { events: ['press'] })]), button]);

      expect(activationClaim(tree)).toEqual({ node: button });
    });

    it('is absorbed when the only claimant sits in a disabled region', () => {
      const tree = node('root', 'ui.stack', {}, [disabledRegion([node('inner', 'ui.button', { events: ['press'] })])]);

      expect(activationClaim(tree)).toBe('absorbed');
    });

    it('is not taken by a node that only drags', () => {
      expect(activationClaim(node('surface', 'ui.modifier', { events: ['drag'] }))).toBe('none');
    });

    it('reaches a value claimant', () => {
      const slider = node('slider', 'ui.slider', { events: ['change'] });

      expect(activationClaim(node('root', 'ui.stack', {}, [slider]))).toEqual({ node: slider });
    });
  });
});
