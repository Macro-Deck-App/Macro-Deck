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

  it('turns a bare http or https URL into an https link and leaves trailing punctuation outside it', () => {
    expect(parseMarkdown('See https://example.com/a_b, or http://example.com/x.')).toEqual([{
      kind: 'paragraph',
      inlines: [
        { kind: 'text', text: 'See ' },
        { kind: 'link', text: 'https://example.com/a_b', href: 'https://example.com/a_b', bare: true },
        { kind: 'text', text: ', or ' },
        { kind: 'link', text: 'http://example.com/x', href: 'https://example.com/x', bare: true },
        { kind: 'text', text: '.' },
      ],
    }]);
  });

  it('never autolinks a non-http scheme', () => {
    expect(JSON.stringify(parseMarkdown('javascript:alert(1) and ftp://example.com'))).not.toContain('"link"');
  });

  it('drops a block-level HTML comment, on one line or several, and keeps what follows', () => {
    const blocks = parseMarkdown('<!-- generated at abc -->\n\n## Title\n\n<!--\nhidden\n-->\nAfter.');

    expect(blocks.map(block => block.kind)).toEqual(['heading', 'paragraph']);
    expect(JSON.stringify(blocks)).not.toContain('hidden');
    expect(JSON.stringify(blocks)).not.toContain('<!--');
  });

  it('keeps text written after the closing --> on the same line', () => {
    expect(parseMarkdown('<!-- note --> visible')).toEqual([{ kind: 'paragraph', inlines: [{ kind: 'text', text: 'visible' }] }]);
  });

  it('ends a paragraph at a comment line', () => {
    expect(parseMarkdown('Line\n<!-- x -->\nNext').map(block => block.kind)).toEqual(['paragraph', 'paragraph']);
  });

  it('leaves a comment indented four spaces or opened mid-line as literal text', () => {
    expect(JSON.stringify(parseMarkdown('    <!-- x -->'))).toContain('<!-- x -->');
    expect(JSON.stringify(parseMarkdown('a <!-- x --> b'))).toContain('<!-- x -->');
  });

  it('keeps a comment inside a fenced code block as code', () => {
    expect(parseMarkdown('```\n<!-- x -->\n```')).toEqual([{ kind: 'code', text: '<!-- x -->', language: null }]);
  });

  it('hides everything after an unterminated comment, as GitHub does', () => {
    expect(parseMarkdown('Before\n\n<!-- open\nstill hidden')).toEqual([{ kind: 'paragraph', inlines: [{ kind: 'text', text: 'Before' }] }]);
  });
});
