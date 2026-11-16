import { UiConfigProperties, UiNode } from '@macro-deck/runtime';
import { findLabelFields, labelAt, pickedLabelIndex, positionPercent, previewLabels } from './preview-label-drag.util';

function node(id: string, value?: unknown, children: UiNode[] = []): UiNode {
  return { id, type: 'x', properties: value === undefined ? {} : { [UiConfigProperties.Value]: value }, children };
}

describe('preview label drag', () => {
  it('finds the position fields and the picker of a single-state button and of a state', () => {
    const root = findLabelFields(node('root', undefined, [node('labelPicker', '0'), node('labelX', 50), node('labelY', 50)]));
    const state = findLabelFields(node('root', undefined, [
      node('states.on.appearance.labelPicker', '2'),
      node('states.on.appearance.extraLabels.b.labelX', 10),
      node('states.on.appearance.extraLabels.b.labelY', 90),
    ]));

    expect([root.picker?.id, root.x?.id, root.y?.id]).toEqual(['labelPicker', 'labelX', 'labelY']);
    expect(pickedLabelIndex(root)).toBe(0);
    expect(state.x?.id).toBe('states.on.appearance.extraLabels.b.labelX');
    expect(pickedLabelIndex(state)).toBe(2);
    expect(findLabelFields(node('root', undefined, [node('labelXPreset', 'left')])).x).toBeNull();
  });

  it('turns where the label is dropped into the percentage the position fields use', () => {
    expect(positionPercent(20, 20, 200, 50)).toBe(0);
    expect(positionPercent(95, 20, 200, 50)).toBe(50);
    expect(positionPercent(170, 20, 200, 50)).toBe(100);
    expect(positionPercent(400, 20, 200, 50)).toBe(100);
    expect(positionPercent(-30, 20, 200, 50)).toBe(0);
    expect(positionPercent(20, 20, 200, 200)).toBeNull();
  });

  it('picks the label drawn last where two overlap and nothing where the pointer is off every label', () => {
    const labels = [
      { index: 0, box: { left: 10, top: 10, width: 60, height: 20 } },
      { index: 2, box: { left: 40, top: 10, width: 60, height: 20 } },
    ];

    expect(labelAt(labels, 50, 20)?.index).toBe(2);
    expect(labelAt(labels, 15, 20)?.index).toBe(0);
    expect(labelAt(labels, 150, 150)).toBeNull();
  });

  it('reads the labels of a layered preview by their slot and skips an empty one', () => {
    const frame = document.createElement('div');
    frame.innerHTML = `
      <div data-node-id="actionButton.labels.labelSlotPrimary.labelAnchorPrimary.labelRowPrimary.labelPinPrimary" style="position:absolute;left:10px;top:10px;width:40px;height:12px"></div>
      <div data-node-id="actionButton.labels.labelSlot0.labelAnchor0.labelRow0.labelPin0" style="position:absolute;left:0;top:0;width:0;height:0"></div>
      <div data-node-id="actionButton.labels.labelSlot1.labelAnchor1.labelRow1.labelPin1" style="position:absolute;left:60px;top:40px;width:30px;height:12px"></div>`;
    document.body.appendChild(frame);

    const labels = previewLabels(frame);
    frame.remove();

    expect(labels.map(label => label.index)).toEqual([0, 2]);
  });
});
