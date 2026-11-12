import { Lexer, type Token, type Tokens } from 'marked';

// Markdown here is third-party (registry descriptions, changelogs, release notes): it becomes a typed
// model that templates render with Angular control flow, so there is never a string of HTML to trust.
export type MarkdownInline =
  | { kind: 'text'; text: string }
  | { kind: 'strong' | 'em' | 'del'; children: MarkdownInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'br' }
  | { kind: 'link'; href: string; title: string | null; children: MarkdownInline[]; bare?: true }
  | { kind: 'image' | 'video'; src: string; alt: string; width?: number; height?: number; darkSrc?: string };

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
  interpretHtml: boolean;
}

const COMMENT = /<!--[\s\S]*?(?:-->|$)/g;
const HTML_TAG = /<(\/?)([a-z][a-z0-9-]*)(?=[\s/>])((?:"[^"]*"|'[^']*'|[^>"'])*)>/gi;
const TAG_ATTRIBUTE = /([^\s=/"'<>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g;
const MAX_DIMENSION = 10000;
const VOID_ELEMENTS = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr']);
const LITERAL_ELEMENTS = new Set(['script', 'style', 'textarea', 'title', 'noscript', 'iframe']);

interface HtmlTag {
  tag: string;
  closing: boolean;
  attributes: Map<string, string>;
}

type HtmlPiece = MarkdownInline | HtmlTag;

interface HtmlFrame {
  tag: string;
  attributes: Map<string, string>;
  children: MarkdownInline[];
  darkSrc?: string;
  lightSrc?: string;
}

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
const CREATOR_TEXT: MarkdownOptions = { media: () => 'image', href: httpsHref, hideComments: true, interpretHtml: true };

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
      case 'paragraph': {
        const inlines = this.inlines((token as Tokens.Paragraph).tokens);
        return inlines.length === 0 ? [] : [{ kind: 'paragraph', inlines }];
      }
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
    const inlines = trimEdges(this.fold(this.htmlPieces(text.trim())));
    return inlines.length === 0 ? [] : [{ kind: 'paragraph', inlines }];
  }

  private inlines(tokens: Token[] | undefined): MarkdownInline[] {
    return this.fold((tokens ?? []).flatMap(token => this.inline(token)));
  }

  private fold(pieces: HtmlPiece[]): MarkdownInline[] {
    const stack: HtmlFrame[] = [{ tag: '', attributes: new Map(), children: [] }];
    for (const piece of pieces) {
      const top = stack[stack.length - 1];
      if (!('tag' in piece)) {
        append(top.children, piece);
      } else if (piece.closing) {
        const index = stack.map(frame => frame.tag).lastIndexOf(piece.tag);
        while (index > 0 && stack.length > index) {
          this.close(stack, stack.length - 1 === index);
        }
      } else if (piece.tag === 'img') {
        append(top.children, this.imgTag(piece.attributes));
      } else if (piece.tag === 'br') {
        top.children.push({ kind: 'br' });
      } else if (piece.tag === 'source') {
        this.source(top, piece.attributes);
      } else if (!VOID_ELEMENTS.has(piece.tag)) {
        stack.push({ tag: piece.tag, attributes: piece.attributes, children: [] });
      }
    }
    while (stack.length > 1) {
      this.close(stack, false);
    }
    return stack[0].children;
  }

  private close(stack: HtmlFrame[], closed: boolean): void {
    const frame = stack.pop()!;
    const parent = stack[stack.length - 1];
    for (const run of this.element(frame, closed)) {
      append(parent.children, run);
    }
  }

  private element(frame: HtmlFrame, closed: boolean): MarkdownInline[] {
    if (frame.tag === 'picture') {
      return frame.children.map(run => (run.kind === 'image' ? withSources(run, frame) : run));
    }
    if (frame.tag !== 'a') {
      return frame.children;
    }
    const children = trimEdges(frame.children);
    const raw = frame.attributes.get('href');
    const href = raw === undefined ? null : this.options.href(raw);
    if (!closed || href === null || children.length === 0) {
      return children;
    }
    return [{ kind: 'link', href, title: frame.attributes.get('title') ?? null, children: describeImages(children, href) }];
  }

  private source(frame: HtmlFrame, attributes: Map<string, string>): void {
    if (frame.tag !== 'picture') {
      return;
    }
    const scheme = /^\(prefers-color-scheme:(dark|light)\)$/.exec((attributes.get('media') ?? '').toLowerCase().replace(/\s+/g, ''));
    const candidate = (attributes.get('srcset') ?? '').split(',')[0].trim().split(/\s+/)[0];
    const image = scheme && candidate ? this.media(candidate, '') : null;
    if (image?.kind !== 'image') {
      return;
    }
    if (scheme![1] === 'dark') {
      frame.darkSrc ??= image.src;
    } else {
      frame.lightSrc ??= image.src;
    }
  }

  private htmlPieces(text: string): HtmlPiece[] {
    const interpret = this.options.interpretHtml;
    const textRun = (raw: string): MarkdownInline[] => (interpret ? plain(raw) : [{ kind: 'text', text: raw }]);
    const pieces: HtmlPiece[] = [];
    let last = 0;
    for (const match of text.matchAll(HTML_TAG)) {
      const tag = match[2].toLowerCase();
      if (interpret ? LITERAL_ELEMENTS.has(tag) : tag !== 'img' || match[1] === '/') {
        continue;
      }
      if (match.index > last) {
        pieces.push(...textRun(text.slice(last, match.index)));
      }
      pieces.push({ tag, closing: match[1] === '/', attributes: tagAttributes(match[3]) });
      last = match.index + match[0].length;
    }
    if (last < text.length) {
      pieces.push(...textRun(text.slice(last)));
    }
    return pieces;
  }

  private imgTag(values: Map<string, string>): MarkdownInline {
    return this.media(values.get('src') ?? '', values.get('alt') ?? '', dimension(values.get('width')), dimension(values.get('height')));
  }

  private inline(token: Token): HtmlPiece[] {
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
        children = describeImages(children, href);
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
        return this.htmlPieces((token as Tokens.Tag).text);
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

function tagAttributes(attributes: string): Map<string, string> {
  const values = new Map<string, string>();
  for (const match of attributes.matchAll(TAG_ATTRIBUTE)) {
    const name = match[1].toLowerCase();
    if (!values.has(name)) {
      values.set(name, decodeEntities(match[2] ?? match[3] ?? match[4] ?? ''));
    }
  }
  return values;
}

function append(runs: MarkdownInline[], run: MarkdownInline): void {
  const previous = runs[runs.length - 1];
  if (run.kind === 'text' && run.text === '') {
    return;
  }
  if (run.kind === 'text' && previous?.kind === 'text') {
    runs[runs.length - 1] = { kind: 'text', text: previous.text + run.text };
  } else {
    runs.push(run);
  }
}

function trimEdges(runs: MarkdownInline[]): MarkdownInline[] {
  const trimmed = [...runs];
  const first = trimmed[0];
  if (first?.kind === 'text') {
    trimmed[0] = { kind: 'text', text: first.text.trimStart() };
  }
  const last = trimmed[trimmed.length - 1];
  if (last?.kind === 'text') {
    trimmed[trimmed.length - 1] = { kind: 'text', text: last.text.trimEnd() };
  }
  return trimmed.filter(run => run.kind !== 'text' || run.text !== '');
}

function describeImages(children: MarkdownInline[], href: string): MarkdownInline[] {
  return children.map(child => (child.kind === 'image' && !child.alt.trim() ? { ...child, alt: href } : child));
}

function withSources(image: Extract<MarkdownInline, { src: string }>, frame: HtmlFrame): MarkdownInline {
  const src = frame.lightSrc ?? image.src;
  return frame.darkSrc && frame.darkSrc !== src ? { ...image, src, darkSrc: frame.darkSrc } : { ...image, src };
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
