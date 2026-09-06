import { parseMarkdown } from './markdown';

describe('parseMarkdown', () => {
  it('never renders a javascript: URL as a link', () => {
    const blocks = parseMarkdown('[click me](javascript:alert(1))');

    expect(blocks.length).toBe(1);
    expect(blocks[0].kind).toBe('paragraph');
    const inlines = blocks[0].kind === 'paragraph' ? blocks[0].inlines : [];
    expect(inlines.some(inline => inline.kind === 'link')).toBeFalse();
    expect(inlines.map(inline => inline.text).join('')).toContain('javascript:alert(1)');
  });

  it('renders raw HTML in the source as literal text, never as its own block or element', () => {
    const blocks = parseMarkdown('<script>alert(1)</script>\n\n<img src=x onerror="alert(1)">');

    for (const block of blocks) {
      expect(block.kind === 'code' || block.kind === 'rule').toBeFalse();
    }

    const text = blocks
      .flatMap(block => (block.kind === 'paragraph' || block.kind === 'heading' || block.kind === 'quote' ? block.inlines : []))
      .map(inline => inline.text)
      .join(' ');

    expect(text).toContain('<script>alert(1)</script>');
    expect(text).toContain('onerror="alert(1)"');
  });

  it('parses ATX headings, a bulleted list, a fenced code block and bold text into the expected kinds', () => {
    const source = [
      '# Title',
      '',
      '- one',
      '- two',
      '',
      '```ts',
      'const x = 1;',
      '```',
      '',
      'This is **bold** text.',
    ].join('\n');

    const blocks = parseMarkdown(source);

    expect(blocks[0]).toEqual({ kind: 'heading', level: 1, inlines: [{ kind: 'text', text: 'Title' }] });

    expect(blocks[1].kind).toBe('list');
    if (blocks[1].kind === 'list') {
      expect(blocks[1].ordered).toBeFalse();
      expect(blocks[1].items).toEqual([
        [{ kind: 'text', text: 'one' }],
        [{ kind: 'text', text: 'two' }],
      ]);
    }

    expect(blocks[2]).toEqual({ kind: 'code', text: 'const x = 1;', language: 'ts' });

    expect(blocks[3].kind).toBe('paragraph');
    if (blocks[3].kind === 'paragraph') {
      expect(blocks[3].inlines).toEqual([
        { kind: 'text', text: 'This is ' },
        { kind: 'strong', text: 'bold' },
        { kind: 'text', text: ' text.' },
      ]);
    }
  });

  it('accepts https links', () => {
    const blocks = parseMarkdown('[docs](https://example.com/path)');

    expect(blocks[0].kind).toBe('paragraph');
    const inlines = blocks[0].kind === 'paragraph' ? blocks[0].inlines : [];
    expect(inlines).toEqual([{ kind: 'link', text: 'docs', href: 'https://example.com/path' }]);
  });
});
