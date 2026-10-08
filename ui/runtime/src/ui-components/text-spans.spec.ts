import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeTextSpans } from './text-spans';

describe('nodeTextSpans', () => {
  const withSpans = (spans: unknown): UiNode =>
    ({ id: 'n1', type: 'ui.text', properties: { spans }, children: [] }) as UiNode;

  it('keeps a span colour written as six or eight hex digits', () => {
    const runs = nodeTextSpans(withSpans([
      { text: 'a', color: '#9146ff' },
      { text: 'b', color: '#9146ff80' },
    ]));

    expect(runs).toEqual([
      { kind: 'text', text: 'a', color: '#9146ff', weight: undefined },
      { kind: 'text', text: 'b', color: '#9146ff80', weight: undefined },
    ]);
  });

  it('drops a span colour in any other spelling and keeps the text', () => {
    const runs = nodeTextSpans(withSpans([
      { text: 'a', color: '#fff' },
      { text: 'b', color: 'purple' },
    ]));

    expect(runs).toEqual([
      { kind: 'text', text: 'a', color: undefined, weight: undefined },
      { kind: 'text', text: 'b', color: undefined, weight: undefined },
    ]);
  });
});
