import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeTextSpans, paintTextSpans } from './text-spans';

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

  it('reads span styling and paints italic, combined lines, a monospace face and a background', () => {
    const runs = nodeTextSpans(withSpans([
      { text: 'plain' },
      { text: 'styled', italic: true, underline: true, strikethrough: true, monospace: true, background: '#1e1f22' },
    ]))!;
    const element = document.createElement('span');

    paintTextSpans(element, runs, () => null);

    const [plain, styled] = Array.from(element.children) as HTMLElement[];
    expect(plain.getAttribute('style')).toBeNull();
    expect(styled.textContent).toBe('styled');
    expect(styled.style.fontStyle).toBe('italic');
    expect(styled.style.textDecoration).toContain('underline');
    expect(styled.style.textDecoration).toContain('line-through');
    expect(styled.style.fontFamily).toContain('monospace');
    expect(styled.style.backgroundColor).not.toBe('');
  });

  it('draws a span with unknown or malformed styling as a plain span', () => {
    const runs = nodeTextSpans(withSpans([{ text: 'a', italic: 'yes', background: 'red', blink: true }]))!;
    const element = document.createElement('span');

    paintTextSpans(element, runs, () => null);

    expect(runs).toEqual([{ kind: 'text', text: 'a', color: undefined, weight: undefined }]);
    expect(element.children[0].getAttribute('style')).toBeNull();
  });
});
