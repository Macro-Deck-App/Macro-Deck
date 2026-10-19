import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree } from './ui-render-test-support';

function prose(properties: Record<string, unknown>): UiNode {
  return { id: 'p', type: 'prose', properties };
}

describe('shared-ui-node styled prose and accented stacks', () => {
  afterEach(() => {
    document.documentElement.classList.remove('light');
    TestBed.resetTestingModule();
  });

  it('draws plain prose as its text and keeps collapsing whitespace', async () => {
    const paragraph = el(await renderTree(prose({ text: 'one\ntwo' }))).querySelector<HTMLElement>('.config-chrome-prose')!;

    expect(paragraph.textContent).toBe('one\ntwo');
    expect(paragraph.querySelector('span')).toBeNull();
    expect(getComputedStyle(paragraph).whiteSpace).toBe('normal');
  });

  it('draws spans in place of the text with their styling and keeps line breaks', async () => {
    const paragraph = el(await renderTree(prose({
      text: 'Hello world\nbye',
      spans: [
        { text: 'Hello ', italic: true },
        { text: 'world', underline: true, strikethrough: true, monospace: true, background: '#1e1f22' },
        { text: '\nbye' },
      ],
    }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    const [hello, world, bye] = Array.from(paragraph.querySelectorAll('span'));

    expect(paragraph.textContent).toBe('Hello world\nbye');
    expect(getComputedStyle(paragraph).whiteSpace).toBe('pre-wrap');
    expect(getComputedStyle(hello).fontStyle).toBe('italic');
    expect(getComputedStyle(world).textDecorationLine).toContain('underline');
    expect(getComputedStyle(world).textDecorationLine).toContain('line-through');
    expect(getComputedStyle(world).fontFamily).toMatch(/mono|Menlo|Consolas/i);
    expect(getComputedStyle(world).backgroundColor).toBe('rgb(30, 31, 34)');
    expect(bye.getAttribute('style') ?? '').toBe('');
  });

  it('sets a quote, a code block and a heading apart from a plain paragraph', async () => {
    const plain = el(await renderTree(prose({ text: 'x' }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    const plainStyle = getComputedStyle(plain);
    const plainSize = parseFloat(plainStyle.fontSize);
    const plainFont = plainStyle.fontFamily;
    const plainBorder = plainStyle.borderLeftWidth;

    const quote = el(await renderTree(prose({ text: 'x', variant: 'quote' }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    expect(getComputedStyle(quote).borderLeftWidth).not.toBe(plainBorder);

    const code = el(await renderTree(prose({ text: 'a\n  b', variant: 'code-block' }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    expect(getComputedStyle(code).fontFamily).not.toBe(plainFont);
    expect(getComputedStyle(code).whiteSpace).toBe('pre-wrap');

    const heading = el(await renderTree(prose({ text: 'x', variant: 'heading-1' }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    expect(parseFloat(getComputedStyle(heading).fontSize)).toBeGreaterThan(plainSize);
    expect(heading.getAttribute('role')).toBe('heading');
    expect(heading.getAttribute('aria-level')).toBe('1');

    const unknown = el(await renderTree(prose({ text: 'x', variant: 'marquee' }))).querySelector<HTMLElement>('.config-chrome-prose')!;
    expect(parseFloat(getComputedStyle(unknown).fontSize)).toBeGreaterThan(0);
    expect(unknown.getAttribute('role')).toBeNull();
  });

  it('draws an accented stack as a dark card with a leading bar even in the light theme', async () => {
    document.documentElement.classList.add('light');
    const host = el(await renderTree({
      id: 's',
      type: 'stack',
      properties: { accent: '#5865f2' },
      children: [prose({ text: 'Message' })],
    }));
    const card = host.querySelector<HTMLElement>('.config-chrome-stack')!;
    const style = getComputedStyle(card);
    const paragraph = card.querySelector<HTMLElement>('.config-chrome-prose')!;

    expect(style.backgroundColor).toBe('rgb(43, 45, 49)');
    expect(style.borderLeftWidth).toBe('4px');
    expect(style.borderLeftColor).toBe('rgb(88, 101, 242)');
    expect(luminance(getComputedStyle(paragraph).color)).toBeGreaterThan(0.5);
  });

  it('leaves a stack without an accent, or with a malformed one, unchanged', async () => {
    for (const properties of [{}, { accent: 'blue' }]) {
      const stack = el(await renderTree({ id: 's', type: 'stack', properties }))
        .querySelector<HTMLElement>('.config-chrome-stack')!;

      expect(getComputedStyle(stack).backgroundColor).toBe('rgba(0, 0, 0, 0)');
      expect(getComputedStyle(stack).borderLeftWidth).toBe('0px');
    }
  });
});

function luminance(rgb: string): number {
  const [r, g, b] = (rgb.match(/\d+/g) ?? ['0', '0', '0']).slice(0, 3).map(Number);
  return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
}
