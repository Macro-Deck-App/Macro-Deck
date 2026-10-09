import { MarkdownBlock, MarkdownInline, parseMarkdown } from '../../../util/markdown';

const RELEASE_REPO = 'Macro-Deck-App/Macro-Deck';
const WHATS_CHANGED = /^what['’]s changed$/i;
const GITHUB_REFERENCE = /^https:\/\/github\.com\/([\w.-]+\/[\w.-]+)\/(?:pull|issues)\/(\d+)\/?$/;
const GITHUB_COMPARE = /^https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/compare\/([^/?#]+)$/;
const MENTION = /(^|[^\w@/.])@([A-Za-z0-9][A-Za-z0-9-]{0,38})/g;

function plainText(inlines: MarkdownInline[]): string {
  return inlines.map(runText).join('').trim();
}

function runText(run: MarkdownInline): string {
  switch (run.kind) {
    case 'text':
    case 'code':
      return run.text;
    case 'strong':
    case 'em':
    case 'del':
    case 'link':
      return run.children.map(runText).join('');
    case 'image':
    case 'video':
      return run.alt;
    case 'br':
      return ' ';
  }
}

function shortLabel(text: string, href: string): string {
  const reference = GITHUB_REFERENCE.exec(href);
  if (reference) {
    return reference[1] === RELEASE_REPO ? `#${reference[2]}` : `${reference[1]}#${reference[2]}`;
  }
  return GITHUB_COMPARE.exec(href)?.[1] ?? text;
}

function linkMentions(text: string): MarkdownInline[] {
  const runs: MarkdownInline[] = [];
  let last = 0;
  for (const match of text.matchAll(MENTION)) {
    const start = match.index + match[1].length;
    if (start > last) {
      runs.push({ kind: 'text', text: text.slice(last, start) });
    }
    runs.push({ kind: 'link', href: `https://github.com/${match[2]}`, title: null, children: [{ kind: 'text', text: `@${match[2]}` }] });
    last = start + match[2].length + 1;
  }
  if (last < text.length) {
    runs.push({ kind: 'text', text: text.slice(last) });
  }
  return runs;
}

function githubInlines(inlines: MarkdownInline[]): MarkdownInline[] {
  return inlines.flatMap((run): MarkdownInline[] => {
    switch (run.kind) {
      case 'link':
        return run.bare ? [{ ...run, children: [{ kind: 'text', text: shortLabel(plainText(run.children), run.href) }] }] : [run];
      case 'text':
        return linkMentions(run.text);
      case 'strong':
      case 'em':
      case 'del':
        return [{ ...run, children: githubInlines(run.children) }];
      default:
        return [run];
    }
  });
}

function githubBlock(block: MarkdownBlock): MarkdownBlock {
  switch (block.kind) {
    case 'heading':
    case 'paragraph':
      return { ...block, inlines: githubInlines(block.inlines) };
    case 'quote':
      return { ...block, blocks: block.blocks.map(githubBlock) };
    case 'list':
      return { ...block, items: block.items.map(item => ({ ...item, blocks: item.blocks.map(githubBlock) })) };
    default:
      return block;
  }
}

export function releaseNoteSections(text: string): MarkdownBlock[][] {
  const blocks = parseMarkdown(text);
  const first = blocks[0];
  if (first?.kind === 'heading' && WHATS_CHANGED.test(plainText(first.inlines))) {
    blocks.shift();
  }

  const sections: MarkdownBlock[][] = [];
  let current: MarkdownBlock[] | null = null;
  for (const block of blocks) {
    const closesCategory = current !== null
      && current[0].kind === 'heading'
      && current[current.length - 1].kind === 'list'
      && block.kind !== 'list';
    if (current === null || block.kind === 'heading' || closesCategory) {
      current = [];
      sections.push(current);
    }
    current.push(githubBlock(block));
  }
  return sections;
}
