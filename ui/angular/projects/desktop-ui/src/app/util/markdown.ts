import { Lexer, type Token, type Tokens } from 'marked';

// Markdown here is third-party (registry descriptions, changelogs, release notes): it becomes a typed
// model that templates render with Angular control flow, so there is never a string of HTML to trust.
export type MarkdownInline =
  | { kind: 'text'; text: string }
  | { kind: 'strong' | 'em' | 'del'; children: MarkdownInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'br' }
  | { kind: 'link'; href: string; title: string | null; children: MarkdownInline[]; bare?: true }
  | { kind: 'image' | 'video'; src: string; alt: string; width?: number; height?: number };

export interface MarkdownListItem {
  task: boolean;
  checked: boolean;
  blocks: MarkdownBlock[];
}

export type MarkdownBlock =
  | { kind: 'heading'; level: 1 | 2 | 3 | 4 | 5 | 6; inlines: MarkdownInline[] }
  | { kind: 'paragraph'; inlines: MarkdownInline[] }
  | { kind: 'code'; text: string; language: string | null }
  | { kind: 'quote'; blocks: MarkdownBlock[] }
  | { kind: 'list'; ordered: boolean; start: number; items: MarkdownListItem[] }
  | {
    kind: 'table';
    align: ('left' | 'center' | 'right' | null)[];
    header: MarkdownInline[][];
    rows: MarkdownInline[][][];
  }
  | { kind: 'rule' };

export interface MarkdownOptions {
  media: (url: URL) => 'image' | 'video' | null;
  href: (raw: string) => string | null;
  hideComments: boolean;
}

const COMMENT = /<!--[\s\S]*?(?:-->|$)/g;
const IMG_TAG = /<img(?=[\s/>])((?:"[^"]*"|'[^']*'|[^>"'])*)>/gi;
const TAG_ATTRIBUTE = /([^\s=/"'<>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g;
const MAX_DIMENSION = 10000;

function httpsHref(raw: string): string | null {
  try {
    const url = new URL(raw.trim());
    if (url.protocol === 'https:') {
      return url.toString();
    }
    if (url.protocol === 'http:') {
      return `https:${url.toString().slice('http:'.length)}`;
    }
    return null;
  } catch {
    return null;
  }
}

// Creator text shows any https image, as GitHub does, so opening a page contacts that image's host.
// Video stays reserved for text Macro Deck itself publishes.
const CREATOR_TEXT: MarkdownOptions = { media: () => 'image', href: httpsHref, hideComments: true };

export function parseMarkdown(source: string, options: MarkdownOptions = CREATOR_TEXT): MarkdownBlock[] {
  return new MarkdownWalker(options).blocks(new Lexer({ gfm: true, breaks: false }).lex(source));
}

class MarkdownWalker {
  constructor(private readonly options: MarkdownOptions) {}

  blocks(tokens: Token[]): MarkdownBlock[] {
    return tokens.flatMap(token => this.block(token));
  }

  private block(token: Token): MarkdownBlock[] {
    switch (token.type) {
      case 'heading': {
        const heading = token as Tokens.Heading;
        const level = Math.min(Math.max(heading.depth, 1), 6) as 1 | 2 | 3 | 4 | 5 | 6;
        return [{ kind: 'heading', level, inlines: this.inlines(heading.tokens) }];
      }
      case 'paragraph':
        return [{ kind: 'paragraph', inlines: this.inlines((token as Tokens.Paragraph).tokens) }];
      case 'text': {
        const text = token as Tokens.Text;
        return [{ kind: 'paragraph', inlines: text.tokens ? this.inlines(text.tokens) : plain(text.text) }];
      }
      case 'code': {
        const code = token as Tokens.Code;
        return [{ kind: 'code', text: code.text, language: (code.lang ?? '').trim().split(/\s+/)[0] || null }];
      }
      case 'blockquote':
        return [{ kind: 'quote', blocks: this.blocks((token as Tokens.Blockquote).tokens) }];
      case 'list': {
        const list = token as Tokens.List;
        return [{
          kind: 'list',
          ordered: list.ordered,
          start: typeof list.start === 'number' ? list.start : 1,
          items: list.items.map(item => ({
            task: item.task,
            checked: !!item.checked,
            blocks: this.blocks(item.tokens.filter(child => child.type !== 'checkbox')),
          })),
        }];
      }
      case 'table': {
        const table = token as Tokens.Table;
        return [{
          kind: 'table',
          align: table.align,
          header: table.header.map(cell => this.inlines(cell.tokens)),
          rows: table.rows.map(row => row.map(cell => this.inlines(cell.tokens))),
        }];
      }
      case 'hr':
        return [{ kind: 'rule' }];
      case 'html':
        return this.html((token as Tokens.HTML).text);
      case 'space':
      case 'def':
      case 'checkbox':
        return [];
      default:
        return 'raw' in token && typeof token.raw === 'string' && token.raw.trim()
          ? [{ kind: 'paragraph', inlines: [{ kind: 'text', text: token.raw }] }]
          : [];
    }
  }

  private html(text: string): MarkdownBlock[] {
    const rest = this.options.hideComments ? text.replace(COMMENT, '') : text;
    if (rest !== text) {
      return rest.trim() ? parseMarkdown(rest.trim(), this.options) : [];
    }
    const inlines = this.rawHtml(text.trim());
    return inlines.every(run => run.kind === 'text' && run.text === '') ? [] : [{ kind: 'paragraph', inlines }];
  }

  private rawHtml(text: string): MarkdownInline[] {
    const runs: MarkdownInline[] = [];
    let last = 0;
    for (const match of text.matchAll(IMG_TAG)) {
      if (match.index > last) {
        runs.push({ kind: 'text', text: text.slice(last, match.index) });
      }
      runs.push(this.imgTag(match[1]));
      last = match.index + match[0].length;
    }
    if (runs.length === 0) {
      return [{ kind: 'text', text }];
    }
    if (last < text.length) {
      runs.push({ kind: 'text', text: text.slice(last) });
    }
    return runs.filter(run => run.kind !== 'text' || run.text !== '');
  }

  private imgTag(attributes: string): MarkdownInline {
    const values = new Map<string, string>();
    for (const match of attributes.matchAll(TAG_ATTRIBUTE)) {
      const name = match[1].toLowerCase();
      if (!values.has(name)) {
        values.set(name, decodeEntities(match[2] ?? match[3] ?? match[4] ?? ''));
      }
    }
    return this.media(values.get('src') ?? '', values.get('alt') ?? '', dimension(values.get('width')), dimension(values.get('height')));
  }

  private inlines(tokens: Token[] | undefined): MarkdownInline[] {
    const runs: MarkdownInline[] = [];
    for (const run of (tokens ?? []).flatMap(token => this.inline(token))) {
      const previous = runs[runs.length - 1];
      if (run.kind === 'text' && previous?.kind === 'text') {
        runs[runs.length - 1] = { kind: 'text', text: previous.text + run.text };
      } else {
        runs.push(run);
      }
    }
    return runs;
  }

  private inline(token: Token): MarkdownInline[] {
    switch (token.type) {
      case 'text': {
        const text = token as Tokens.Text;
        return text.tokens ? this.inlines(text.tokens) : plain(text.text);
      }
      case 'escape':
        return [{ kind: 'text', text: (token as Tokens.Escape).text }];
      case 'strong':
      case 'em':
      case 'del':
        return [{ kind: token.type, children: this.inlines((token as Tokens.Strong).tokens) }];
      case 'codespan':
        return [{ kind: 'code', text: (token as Tokens.Codespan).text }];
      case 'br':
        return [{ kind: 'br' }];
      case 'link': {
        const link = token as Tokens.Link;
        const href = this.options.href(link.href);
        let children = this.inlines(link.tokens);
        if (href === null) {
          return children;
        }
        children = children.map(child => (child.kind === 'image' && !child.alt.trim() ? { ...child, alt: href } : child));
        if (children.every(child => child.kind === 'text' && child.text.trim() === '')) {
          children = [{ kind: 'text', text: href }];
        }
        const title = link.title ? decodeEntities(link.title) : null;
        return [link.autolink ? { kind: 'link', href, title, children, bare: true } : { kind: 'link', href, title, children }];
      }
      case 'image':
        return [this.media((token as Tokens.Image).href, decodeEntities((token as Tokens.Image).text))];
      case 'html':
      case 'tag':
        return this.rawHtml((token as Tokens.Tag).text);
      case 'checkbox':
        return [];
      default:
        return 'raw' in token && typeof token.raw === 'string' ? [{ kind: 'text', text: token.raw }] : [];
    }
  }

  private media(src: string, alt: string, width?: number, height?: number): MarkdownInline {
    let url: URL;
    try {
      url = new URL(src.trim());
    } catch {
      return { kind: 'text', text: alt };
    }
    const kind = url.protocol === 'https:' ? this.options.media(url) : null;
    if (!kind) {
      return { kind: 'text', text: alt };
    }
    return {
      kind,
      src: url.href,
      alt,
      ...(width === undefined ? {} : { width }),
      ...(height === undefined ? {} : { height }),
    };
  }
}

function dimension(value: string | undefined): number | undefined {
  if (value === undefined || !/^\d+$/.test(value.trim())) {
    return undefined;
  }
  const size = Number(value.trim());
  return size > 0 && size <= MAX_DIMENSION ? size : undefined;
}

function plain(text: string): MarkdownInline[] {
  return [{ kind: 'text', text: decodeEntities(text) }];
}

let decoder: HTMLTextAreaElement | null = null;

function decodeEntities(text: string): string {
  if (!text.includes('&')) {
    return text;
  }
  // A textarea parses its content as RCDATA: character references are decoded, markup never becomes
  // elements, and the detached element never loads anything. The result is only ever interpolated.
  decoder ??= document.createElement('textarea');
  decoder.innerHTML = text;
  return decoder.value;
}
