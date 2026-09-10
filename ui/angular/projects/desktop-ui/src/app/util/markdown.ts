// A deliberately incomplete Markdown parser for third-party registry content (issue #517): extension
// descriptions and changelogs come off the network, so they never go through innerHTML or a
// DomSanitizer escape hatch. This parses into a typed model a template renders with plain Angular
// control flow, so there is never a string of HTML to trust. Raw HTML degrades to literal text.
export type MarkdownInline =
  | { kind: 'text'; text: string }
  | { kind: 'strong'; text: string }
  | { kind: 'em'; text: string }
  | { kind: 'code'; text: string }
  | { kind: 'link'; text: string; href: string; bare?: true };

export type MarkdownBlock =
  | { kind: 'heading'; level: 1 | 2 | 3; inlines: MarkdownInline[] }
  | { kind: 'paragraph'; inlines: MarkdownInline[] }
  | { kind: 'list'; ordered: boolean; items: MarkdownInline[][] }
  | { kind: 'code'; text: string; language: string | null }
  | { kind: 'quote'; inlines: MarkdownInline[] }
  | { kind: 'rule' };

const ATX_HEADING = /^(#{1,3})\s+(.*)$/;
const UNORDERED_ITEM = /^[-*]\s+(.*)$/;
const ORDERED_ITEM = /^\d+\.\s+(.*)$/;
const QUOTE_LINE = /^>\s?(.*)$/;
const FENCE = /^```\s*(\S*)\s*$/;
const RULE = /^([-*_])(?:\s*\1){2,}$/;
const COMMENT_OPEN = /^ {0,3}<!--/;
const TRAILING_PUNCTUATION = /[.,:;!?'"*_~]+$/;

function safeHref(rawHref: string): string | null {
  const href = rawHref.trim();
  try {
    const url = new URL(href);
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

const INLINE_TOKEN = /(\*\*([^*]+)\*\*)|(\*([^*]+)\*)|(`([^`]+)`)|(\[([^\]]*)\]\(([^)\s]*)\))|(https?:\/\/[^\s<>()[\]]+)/;

function parseInline(source: string): MarkdownInline[] {
  const inlines: MarkdownInline[] = [];
  let remaining = source;

  while (remaining.length > 0) {
    const match = INLINE_TOKEN.exec(remaining);
    if (!match) {
      inlines.push({ kind: 'text', text: remaining });
      break;
    }

    if (match.index > 0) {
      inlines.push({ kind: 'text', text: remaining.slice(0, match.index) });
    }

    let consumed = match[0].length;
    if (match[1] !== undefined) {
      inlines.push({ kind: 'strong', text: match[2] });
    } else if (match[3] !== undefined) {
      inlines.push({ kind: 'em', text: match[4] });
    } else if (match[5] !== undefined) {
      inlines.push({ kind: 'code', text: match[6] });
    } else if (match[7] !== undefined) {
      const href = safeHref(match[9]);
      if (href) {
        inlines.push({ kind: 'link', text: match[8], href });
      } else {
        inlines.push({ kind: 'text', text: match[0] });
      }
    } else {
      const url = match[10].replace(TRAILING_PUNCTUATION, '');
      const href = safeHref(url);
      inlines.push(href ? { kind: 'link', text: url, href, bare: true } : { kind: 'text', text: url });
      consumed = url.length;
    }

    remaining = remaining.slice(match.index + consumed);
  }

  return inlines;
}

export function parseMarkdown(source: string): MarkdownBlock[] {
  const blocks: MarkdownBlock[] = [];
  const lines = source.replace(/\r\n/g, '\n').split('\n');

  let i = 0;
  while (i < lines.length) {
    const line = lines[i];

    if (line.trim() === '') {
      i++;
      continue;
    }

    const fenceMatch = FENCE.exec(line);
    if (fenceMatch) {
      const language = fenceMatch[1] || null;
      const codeLines: string[] = [];
      i++;
      while (i < lines.length && !FENCE.test(lines[i])) {
        codeLines.push(lines[i]);
        i++;
      }
      i++; // skip closing fence (or EOF)
      blocks.push({ kind: 'code', text: codeLines.join('\n'), language });
      continue;
    }

    if (COMMENT_OPEN.test(line)) {
      let end = line.indexOf('-->', line.indexOf('<!--') + 4);
      while (end < 0 && ++i < lines.length) {
        end = lines[i].indexOf('-->');
      }
      if (i >= lines.length) {
        break;
      }
      const rest = lines[i].slice(end + 3).trimStart();
      if (rest === '') {
        i++;
      } else {
        lines[i] = rest;
      }
      continue;
    }

    if (RULE.test(line.trim())) {
      blocks.push({ kind: 'rule' });
      i++;
      continue;
    }

    const headingMatch = ATX_HEADING.exec(line);
    if (headingMatch) {
      const level = headingMatch[1].length as 1 | 2 | 3;
      blocks.push({ kind: 'heading', level, inlines: parseInline(headingMatch[2].trim()) });
      i++;
      continue;
    }

    const unorderedMatch = UNORDERED_ITEM.exec(line);
    const orderedMatch = ORDERED_ITEM.exec(line);
    if (unorderedMatch || orderedMatch) {
      const ordered = !!orderedMatch;
      const itemPattern = ordered ? ORDERED_ITEM : UNORDERED_ITEM;
      const items: MarkdownInline[][] = [];
      while (i < lines.length) {
        const itemMatch = itemPattern.exec(lines[i]);
        if (!itemMatch) {
          break;
        }
        items.push(parseInline(itemMatch[1]));
        i++;
      }
      blocks.push({ kind: 'list', ordered, items });
      continue;
    }

    const quoteMatch = QUOTE_LINE.exec(line);
    if (quoteMatch) {
      const quoteLines: string[] = [quoteMatch[1]];
      i++;
      while (i < lines.length) {
        const nextQuote = QUOTE_LINE.exec(lines[i]);
        if (!nextQuote) {
          break;
        }
        quoteLines.push(nextQuote[1]);
        i++;
      }
      blocks.push({ kind: 'quote', inlines: parseInline(quoteLines.join(' ')) });
      continue;
    }

    const paragraphLines: string[] = [line];
    i++;
    while (
      i < lines.length &&
      lines[i].trim() !== '' &&
      !ATX_HEADING.test(lines[i]) &&
      !FENCE.test(lines[i]) &&
      !UNORDERED_ITEM.test(lines[i]) &&
      !ORDERED_ITEM.test(lines[i]) &&
      !QUOTE_LINE.test(lines[i]) &&
      !COMMENT_OPEN.test(lines[i]) &&
      !RULE.test(lines[i].trim())
    ) {
      paragraphLines.push(lines[i]);
      i++;
    }
    blocks.push({ kind: 'paragraph', inlines: parseInline(paragraphLines.join(' ')) });
  }

  return blocks;
}
