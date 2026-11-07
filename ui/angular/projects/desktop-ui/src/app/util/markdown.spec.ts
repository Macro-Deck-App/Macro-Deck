import { MarkdownBlock, MarkdownInline, parseMarkdown } from './markdown';

function inlineText(runs: MarkdownInline[]): string {
  return runs.map(run => {
    switch (run.kind) {
      case 'text':
      case 'code':
        return run.text;
      case 'image':
      case 'video':
        return run.alt;
      case 'br':
        return '\n';
      default:
        return inlineText(run.children);
    }
  }).join('');
}

function blockText(blocks: MarkdownBlock[]): string {
  return blocks.map(block => {
    switch (block.kind) {
      case 'heading':
      case 'paragraph':
        return inlineText(block.inlines);
      case 'code':
        return block.text;
      case 'quote':
        return blockText(block.blocks);
      case 'list':
        return block.items.map(item => blockText(item.blocks)).join('\n');
      case 'table':
        return [block.header, ...block.rows].map(row => row.map(inlineText).join(' | ')).join('\n');
      case 'rule':
        return '';
    }
  }).join('\n');
}

function links(blocks: MarkdownBlock[]): { text: string; href: string }[] {
  const found: { text: string; href: string }[] = [];
  const visitInlines = (runs: MarkdownInline[]): void => runs.forEach(run => {
    if (run.kind === 'link') {
      found.push({ text: inlineText(run.children), href: run.href });
    } else if (run.kind === 'strong' || run.kind === 'em' || run.kind === 'del') {
      visitInlines(run.children);
    }
  });
  const visit = (items: MarkdownBlock[]): void => items.forEach(block => {
    switch (block.kind) {
      case 'heading':
      case 'paragraph':
        visitInlines(block.inlines);
        break;
      case 'quote':
        visit(block.blocks);
        break;
      case 'list':
        block.items.forEach(item => visit(item.blocks));
        break;
      case 'table':
        [block.header, ...block.rows].forEach(row => row.forEach(visitInlines));
        break;
    }
  });
  visit(blocks);
  return found;
}

describe('parseMarkdown', () => {
  it('never renders a javascript: URL as a link, not even around an image, and keeps its label readable', () => {
    const blocks = parseMarkdown('[click me](javascript:alert(1)) [![badge](https://example.com/b.png)](javascript:alert(1))');

    expect(links(blocks)).toEqual([]);
    expect(blockText(blocks)).toBe('click me badge');
  });

  it('never turns raw HTML into its own block, element or attribute', () => {
    const blocks = parseMarkdown('<script>alert(1)</script>\n\n<div onclick="alert(1)">hi</div>');

    for (const block of blocks) {
      expect(block.kind === 'code' || block.kind === 'rule').toBeFalse();
    }

    const text = blockText(blocks);

    expect(text).toContain('<script>alert(1)</script>');
    expect(text).toContain('hi');
    expect(text).not.toContain('onclick');
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
      expect(blocks[1].items.map(item => blockText(item.blocks))).toEqual(['one', 'two']);
    }

    expect(blocks[2]).toEqual({ kind: 'code', text: 'const x = 1;', language: 'ts' });

    expect(blocks[3].kind).toBe('paragraph');
    if (blocks[3].kind === 'paragraph') {
      expect(blocks[3].inlines).toEqual([
        { kind: 'text', text: 'This is ' },
        { kind: 'strong', children: [{ kind: 'text', text: 'bold' }] },
        { kind: 'text', text: ' text.' },
      ]);
    }
  });

  it('accepts https links', () => {
    const blocks = parseMarkdown('[docs](https://example.com/path)');

    expect(blocks[0].kind).toBe('paragraph');
    const inlines = blocks[0].kind === 'paragraph' ? blocks[0].inlines : [];
    expect(inlines).toEqual([{ kind: 'link', href: 'https://example.com/path', title: null, children: [{ kind: 'text', text: 'docs' }] }]);
  });

  it('turns a bare http or https URL into an https link and leaves trailing punctuation outside it', () => {
    const blocks = parseMarkdown('See https://example.com/a_b, or http://example.com/x. and www.example.com');

    expect(links(blocks)).toEqual([
      { text: 'https://example.com/a_b', href: 'https://example.com/a_b' },
      { text: 'http://example.com/x', href: 'https://example.com/x' },
      { text: 'www.example.com', href: 'https://www.example.com/' },
    ]);
    expect(blockText(blocks)).toBe('See https://example.com/a_b, or http://example.com/x. and www.example.com');
  });

  it('never links an email address or a mailto link', () => {
    const blocks = parseMarkdown('Mail a@b.example or [us](mailto:us@b.example).');

    expect(links(blocks)).toEqual([]);
    expect(blocks).toEqual([{ kind: 'paragraph', inlines: [{ kind: 'text', text: 'Mail a@b.example or us.' }] }]);
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
    expect(parseMarkdown('<!-- note --> visible **bold**')).toEqual([{
      kind: 'paragraph',
      inlines: [{ kind: 'text', text: 'visible ' }, { kind: 'strong', children: [{ kind: 'text', text: 'bold' }] }],
    }]);
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

  it('keeps an indented continuation line inside its list item and the numbering intact', () => {
    const blocks = parseMarkdown([
      '1. In Macro Deck, open **Integrations**.',
      '2. Pick your bridge. If it is not listed, type the',
      '   bridge\'s IP address.',
      '3. Press the link button.',
    ].join('\n'));

    expect(blocks.length).toBe(1);
    const list = blocks[0];
    expect(list.kind === 'list' && list.ordered && list.start).toBe(1);
    expect(list.kind === 'list' ? list.items.map(item => blockText(item.blocks)) : []).toEqual([
      'In Macro Deck, open Integrations.',
      'Pick your bridge. If it is not listed, type the\nbridge\'s IP address.',
      'Press the link button.',
    ]);
  });

  it('keeps the start number of an ordered list', () => {
    const [list] = parseMarkdown('3. third\n4. fourth');

    expect(list.kind === 'list' ? list.start : null).toBe(3);
  });

  it('nests an indented list inside the item above it', () => {
    const [list] = parseMarkdown('- To find bridges, it uses:\n  1. mDNS, and\n  2. a subnet scan\n- Next');

    expect(list.kind === 'list' ? list.items.length : 0).toBe(2);
    const first = list.kind === 'list' ? list.items[0].blocks : [];
    expect(first.map(block => block.kind)).toEqual(['paragraph', 'list']);
    const nested = first[1];
    expect(nested.kind === 'list' && nested.ordered).toBeTrue();
    expect(nested.kind === 'list' ? nested.items.map(item => blockText(item.blocks)) : []).toEqual(['mDNS, and', 'a subnet scan']);
  });

  it('parses a GitHub table into a header and rows', () => {
    const [table] = parseMarkdown('| Action | What it does |\n| --- | --- |\n| Set scene | Recalls a **scene**. |\n| Update light | Sets lights. |');

    expect(table.kind).toBe('table');
    if (table.kind === 'table') {
      expect(table.header.map(inlineText)).toEqual(['Action', 'What it does']);
      expect(table.rows.map(row => row.map(inlineText))).toEqual([['Set scene', 'Recalls a scene.'], ['Update light', 'Sets lights.']]);
    }
  });

  it('shows an https image, also inside a link, and only the description of any other image', () => {
    const blocks = parseMarkdown([
      '[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/S5O622WBIQ)',
      '',
      '![Build](https://img.shields.io/badge/build-passing-green) ![Insecure](http://example.com/a.png) ![Local](file:///etc/a.png)',
    ].join('\n'));

    expect(blocks).toEqual([
      {
        kind: 'paragraph',
        inlines: [{
          kind: 'link',
          href: 'https://ko-fi.com/S5O622WBIQ',
          title: null,
          children: [{ kind: 'image', src: 'https://ko-fi.com/img/githubbutton_sm.svg', alt: 'ko-fi' }],
        }],
      },
      {
        kind: 'paragraph',
        inlines: [
          { kind: 'image', src: 'https://img.shields.io/badge/build-passing-green', alt: 'Build' },
          { kind: 'text', text: ' Insecure Local' },
        ],
      },
    ]);
  });

  it('never embeds a video in creator text', () => {
    expect(JSON.stringify(parseMarkdown('![Clip](https://example.com/a.mp4)'))).not.toContain('"video"');
  });

  it('names a link after its address when nothing inside it describes it', () => {
    const [badge, empty] = parseMarkdown('[![](https://img.shields.io/x.svg)](https://example.com/build)\n\n[](https://example.com/empty)');

    expect(badge).toEqual({
      kind: 'paragraph',
      inlines: [{
        kind: 'link',
        href: 'https://example.com/build',
        title: null,
        children: [{ kind: 'image', src: 'https://img.shields.io/x.svg', alt: 'https://example.com/build' }],
      }],
    });
    expect(links([empty])).toEqual([{ text: 'https://example.com/empty', href: 'https://example.com/empty' }]);
  });

  describe('img tags', () => {
    const images = (source: string) => parseMarkdown(source).flatMap(block => (block.kind === 'paragraph' ? block.inlines : []));

    it('shows a GitHub upload tag as an image with numeric size, in a block and inline', () => {
      const tag = '<img width="1457" height="886" alt="image" src="https://github.com/user-attachments/assets/abc?x=1&amp;y=2" />';

      const expected: MarkdownInline = { kind: 'image', src: 'https://github.com/user-attachments/assets/abc?x=1&y=2', alt: 'image', width: 1457, height: 886 };
      expect(images(tag)).toEqual([expected]);
      expect(images(`Before ${tag} after`)).toEqual([
        { kind: 'text', text: 'Before ' },
        expected,
        { kind: 'text', text: ' after' },
      ]);
    });

    it('falls back to the description for an http, relative or missing source', () => {
      expect(images('<img alt="Plain" src="http://example.com/a.png">')).toEqual([{ kind: 'text', text: 'Plain' }]);
      expect(images('<img alt="Rel" src="/a.png">')).toEqual([{ kind: 'text', text: 'Rel' }]);
      expect(images('<img alt="None">')).toEqual([{ kind: 'text', text: 'None' }]);
    });

    it('drops every attribute but src, alt, width and height, and non-numeric sizes', () => {
      const [image] = images('<img src="https://example.com/a.png" alt="A" onerror="alert(1)" style="x" width="50%" height="12px">');

      expect(image).toEqual({ kind: 'image', src: 'https://example.com/a.png', alt: 'A' });
      expect(JSON.stringify(image)).not.toContain('alert');
    });

    it('drops other tags and keeps their text, but leaves text that only looks like a tag', () => {
      expect(images('<p align="center">\n<img src="https://example.com/a.png" alt="A">\n</p>')).toEqual([
        { kind: 'image', src: 'https://example.com/a.png', alt: 'A' },
      ]);
      expect(images('<div>hi &amp; bye</div>')).toEqual([{ kind: 'text', text: 'hi & bye' }]);
      expect(images('<imgx src="https://example.com/a.png">x')).toEqual([{ kind: 'text', text: 'x' }]);
      expect(images('<div>\nsee <https://example.com/x> or <user@example.com>\n</div>')).toEqual([
        { kind: 'text', text: 'see <https://example.com/x> or <user@example.com>' },
      ]);
    });

    it('drops a block whose only content is an unusable image without a description', () => {
      expect(parseMarkdown('<img src="http://example.com/a.png">')).toEqual([]);
      expect(images('<img src="https://example.com/a.png" width="0" height="99999" ALT=\'Up\'>')).toEqual([
        { kind: 'image', src: 'https://example.com/a.png', alt: 'Up' },
      ]);
    });

    it('never shows a hidden comment around an image', () => {
      expect(images('<!-- note --><img src="https://example.com/a.png" alt="A">')).toEqual([
        { kind: 'image', src: 'https://example.com/a.png', alt: 'A' },
      ]);
    });
  });

  describe('raw HTML around images', () => {
    const inlines = (source: string) => parseMarkdown(source).flatMap(block => (block.kind === 'paragraph' ? block.inlines : []));
    const store = 'https://macro-deck.app/store/com.example.plugin';
    const light = 'https://assets.macro-deck.app/branding/Badge%20Light.png';
    const dark = 'https://assets.macro-deck.app/branding/Badge%20Dark.png';

    it('shows a store badge in a picture as one linked image with its dark variant', () => {
      const readme = [
        `<a href="${store}">`,
        '  <picture>',
        `    <source media="(prefers-color-scheme: dark)" srcset="${dark}">`,
        `    <img src="${light}" alt="Open in the Macro Deck Store">`,
        '  </picture>',
        '</a>',
      ].join('\n');

      expect(parseMarkdown(readme)).toEqual([{
        kind: 'paragraph',
        inlines: [{
          kind: 'link',
          href: store,
          title: null,
          children: [{ kind: 'image', src: light, alt: 'Open in the Macro Deck Store', darkSrc: dark }],
        }],
      }]);
    });

    it('links a badge whose tags share one line inside a paragraph', () => {
      expect(inlines(`Get it: <a href="${store}"><img src="${dark}" alt=""></a> now`)).toEqual([
        { kind: 'text', text: 'Get it: ' },
        { kind: 'link', href: store, title: null, children: [{ kind: 'image', src: dark, alt: store }] },
        { kind: 'text', text: ' now' },
      ]);
      expect(inlines(`<a href="${store}" title="Store">the store</a>`)).toEqual([
        { kind: 'link', href: store, title: 'Store', children: [{ kind: 'text', text: 'the store' }] },
      ]);
    });

    it('never links an unsafe address and never shows an address the author did not write', () => {
      expect(inlines(`<a href="javascript:alert(1)"><img src="${light}" alt="A"></a>`)).toEqual([{ kind: 'image', src: light, alt: 'A' }]);
      expect(inlines(`<a href="/relative">text</a>`)).toEqual([{ kind: 'text', text: 'text' }]);
      expect(parseMarkdown(`<a href="${store}"></a>`)).toEqual([]);
      expect(parseMarkdown(`<a href="${store}">\n\n<img src="${light}" alt="A">\n\n</a>`).flatMap(block => (block.kind === 'paragraph' ? block.inlines : []))).toEqual([
        { kind: 'image', src: light, alt: 'A' },
      ]);
    });

    it('picks the light and dark sources by colour scheme and ignores unusable ones', () => {
      const picture = (sources: string, img = light) => inlines(`<picture>${sources}<img src="${img}" alt="A"></picture>`);
      const other = 'https://example.com/light.png';

      expect(picture(`<source media="(PREFERS-color-scheme:dark)" srcset="${dark} 2x, https://example.com/x.png 1x">`)).toEqual([
        { kind: 'image', src: light, alt: 'A', darkSrc: dark },
      ]);
      expect(picture(`<source media="(prefers-color-scheme: light)" srcset="${other}"><source media="(prefers-color-scheme: dark)" srcset="${dark}">`)).toEqual([
        { kind: 'image', src: other, alt: 'A', darkSrc: dark },
      ]);
      expect(picture('<source media="(prefers-color-scheme: dark)" srcset="http://example.com/dark.png">')).toEqual([
        { kind: 'image', src: light, alt: 'A' },
      ]);
      expect(picture(`<source media="(min-width: 600px)" srcset="${dark}">`)).toEqual([{ kind: 'image', src: light, alt: 'A' }]);
      expect(picture(`<source media="(prefers-color-scheme: dark)" srcset="${dark}">`, 'http://example.com/a.png')).toEqual([
        { kind: 'text', text: 'A' },
      ]);
    });

    it('shows script, style and similar tags as written and never swallows the text after them', () => {
      expect(inlines('<style>\n.badge { color: red; }\n</style>')).toEqual([{ kind: 'text', text: '<style>\n.badge { color: red; }\n</style>' }]);
      expect(inlines('Set the <title> of the window, then restart')).toEqual([{ kind: 'text', text: 'Set the <title> of the window, then restart' }]);
      expect(inlines('Before <script>alert("<b>x</b>")</script>after')).toEqual([{ kind: 'text', text: 'Before <script>alert("x")</script>after' }]);
    });

    it('keeps line breaks', () => {
      expect(inlines('one<br>two')).toEqual([{ kind: 'text', text: 'one' }, { kind: 'br' }, { kind: 'text', text: 'two' }]);
    });
  });
});
