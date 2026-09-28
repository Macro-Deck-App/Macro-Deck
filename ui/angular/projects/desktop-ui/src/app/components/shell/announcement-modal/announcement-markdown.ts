import { Lexer, type Token, type Tokens } from 'marked';

export type AnnouncementInline =
  | { kind: 'text'; text: string }
  | { kind: 'strong' | 'em' | 'del'; children: AnnouncementInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'br' }
  | { kind: 'link'; href: string; title: string | null; children: AnnouncementInline[] }
  | { kind: 'image' | 'video'; src: string; alt: string };

export interface AnnouncementListItem {
  task: boolean;
  checked: boolean;
  blocks: AnnouncementBlock[];
}

export type AnnouncementBlock =
  | { kind: 'heading'; level: 1 | 2 | 3 | 4 | 5 | 6; inlines: AnnouncementInline[] }
  | { kind: 'paragraph'; inlines: AnnouncementInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'quote'; blocks: AnnouncementBlock[] }
  | { kind: 'list'; ordered: boolean; start: number; items: AnnouncementListItem[] }
  | {
    kind: 'table';
    align: ('left' | 'center' | 'right' | null)[];
    header: AnnouncementInline[][];
    rows: AnnouncementInline[][][];
  }
  | { kind: 'rule' };

const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp']);
const VIDEO_EXTENSIONS = new Set(['mp4', 'webm']);
const LINK_SCHEME = /^(https?:|mailto:)/i;

// Same options as the Creator Portal preview, which is the reference for how an announcement looks.
export function parseAnnouncement(markdown: string): AnnouncementBlock[] {
  return blocks(new Lexer({ gfm: true, breaks: false }).lex(markdown));
}

function blocks(tokens: Token[]): AnnouncementBlock[] {
  return tokens.flatMap(block);
}

function block(token: Token): AnnouncementBlock[] {
  switch (token.type) {
    case 'heading': {
      const heading = token as Tokens.Heading;
      const level = Math.min(Math.max(heading.depth, 1), 6) as 1 | 2 | 3 | 4 | 5 | 6;
      return [{ kind: 'heading', level, inlines: inlines(heading.tokens) }];
    }
    case 'paragraph':
      return [{ kind: 'paragraph', inlines: inlines((token as Tokens.Paragraph).tokens) }];
    case 'text': {
      const text = token as Tokens.Text;
      return [{ kind: 'paragraph', inlines: text.tokens ? inlines(text.tokens) : plain(text.text) }];
    }
    case 'code':
      return [{ kind: 'code', text: (token as Tokens.Code).text }];
    case 'blockquote':
      return [{ kind: 'quote', blocks: blocks((token as Tokens.Blockquote).tokens) }];
    case 'list': {
      const list = token as Tokens.List;
      return [{
        kind: 'list',
        ordered: list.ordered,
        start: typeof list.start === 'number' ? list.start : 1,
        items: list.items.map(item => ({
          task: item.task,
          checked: !!item.checked,
          blocks: blocks(item.tokens.filter(child => child.type !== 'checkbox')),
        })),
      }];
    }
    case 'table': {
      const table = token as Tokens.Table;
      return [{
        kind: 'table',
        align: table.align,
        header: table.header.map(cell => inlines(cell.tokens)),
        rows: table.rows.map(row => row.map(cell => inlines(cell.tokens))),
      }];
    }
    case 'hr':
      return [{ kind: 'rule' }];
    case 'html':
      return [{ kind: 'paragraph', inlines: [{ kind: 'text', text: (token as Tokens.HTML).text.trim() }] }];
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

function inlines(tokens: Token[] | undefined): AnnouncementInline[] {
  return (tokens ?? []).flatMap(inline);
}

function inline(token: Token): AnnouncementInline[] {
  switch (token.type) {
    case 'text': {
      const text = token as Tokens.Text;
      return text.tokens ? inlines(text.tokens) : plain(text.text);
    }
    case 'escape':
      return [{ kind: 'text', text: (token as Tokens.Escape).text }];
    case 'strong':
    case 'em':
    case 'del':
      return [{ kind: token.type, children: inlines((token as Tokens.Strong).tokens) }];
    case 'codespan':
      return [{ kind: 'code', text: (token as Tokens.Codespan).text }];
    case 'br':
      return [{ kind: 'br' }];
    case 'link': {
      const link = token as Tokens.Link;
      const href = link.href.trim();
      const children = inlines(link.tokens);
      return LINK_SCHEME.test(href)
        ? [{ kind: 'link', href, title: link.title ? decodeEntities(link.title) : null, children }]
        : children;
    }
    case 'image':
      return [media(token as Tokens.Image)];
    case 'html':
    case 'tag':
      return [{ kind: 'text', text: (token as Tokens.Tag).text }];
    case 'checkbox':
      return [];
    default:
      return 'raw' in token && typeof token.raw === 'string' ? [{ kind: 'text', text: token.raw }] : [];
  }
}

function media(image: Tokens.Image): AnnouncementInline {
  const alt = decodeEntities(image.text);
  let url: URL;
  try {
    url = new URL(image.href.trim());
  } catch {
    return { kind: 'text', text: alt };
  }
  if (url.protocol !== 'https:') {
    return { kind: 'text', text: alt };
  }
  const extension = url.pathname.split('.').pop()?.toLowerCase() ?? '';
  if (IMAGE_EXTENSIONS.has(extension)) {
    return { kind: 'image', src: url.href, alt };
  }
  if (VIDEO_EXTENSIONS.has(extension)) {
    return { kind: 'video', src: url.href, alt };
  }
  return { kind: 'text', text: alt };
}

function plain(text: string): AnnouncementInline[] {
  return [{ kind: 'text', text: decodeEntities(text) }];
}

let decoder: HTMLTextAreaElement | null = null;

export function decodeEntities(text: string): string {
  if (!text.includes('&')) {
    return text;
  }
  // A textarea parses its content as RCDATA: character references are decoded, markup never becomes
  // elements, and the detached element never loads anything. The result is only ever interpolated.
  decoder ??= document.createElement('textarea');
  decoder.innerHTML = text;
  return decoder.value;
}
