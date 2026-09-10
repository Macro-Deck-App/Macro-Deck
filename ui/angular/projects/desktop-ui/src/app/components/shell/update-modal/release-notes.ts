import { MarkdownBlock, MarkdownInline, parseMarkdown } from '../../../util/markdown';

const RELEASE_REPO = 'Macro-Deck-App/Macro-Deck';
const WHATS_CHANGED = /^what['’]s changed$/i;
const GITHUB_REFERENCE = /^https:\/\/github\.com\/([\w.-]+\/[\w.-]+)\/(?:pull|issues)\/(\d+)\/?$/;
const GITHUB_COMPARE = /^https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/compare\/([^/?#]+)$/;
const MENTION = /(^|[^\w@/.])@([A-Za-z0-9][A-Za-z0-9-]{0,38})/g;

function plainText(inlines: MarkdownInline[]): string {
  return inlines.map(run => run.text).join('').trim();
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
    runs.push({ kind: 'link', text: `@${match[2]}`, href: `https://github.com/${match[2]}` });
    last = start + match[2].length + 1;
  }
  if (last < text.length) {
    runs.push({ kind: 'text', text: text.slice(last) });
  }
  return runs;
}

function githubInlines(inlines: MarkdownInline[]): MarkdownInline[] {
  return inlines.flatMap(run => {
    if (run.kind === 'link' && run.bare) {
      return [{ ...run, text: shortLabel(run.text, run.href) }];
    }
    return run.kind === 'text' ? linkMentions(run.text) : [run];
  });
}

function githubBlock(block: MarkdownBlock): MarkdownBlock {
  switch (block.kind) {
    case 'heading':
    case 'paragraph':
    case 'quote':
      return { ...block, inlines: githubInlines(block.inlines) };
    case 'list':
      return { ...block, items: block.items.map(githubInlines) };
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
