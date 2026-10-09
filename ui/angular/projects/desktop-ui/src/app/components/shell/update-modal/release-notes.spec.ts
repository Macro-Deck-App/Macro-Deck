import { MarkdownInline } from '../../../util/markdown';
import { releaseNoteSections } from './release-notes';

function written(runs: MarkdownInline[]): string {
  return runs.map(run => {
    switch (run.kind) {
      case 'text':
        return run.text;
      case 'link':
        return `[${written(run.children)}](${run.href})`;
      case 'strong':
      case 'em':
      case 'del':
        return written(run.children);
      default:
        return '';
    }
  }).join('');
}

function shape(text: string): string[][] {
  return releaseNoteSections(text).map(section => section.map(block => block.kind));
}

describe('releaseNoteSections', () => {
  it('drops a leading "What\'s Changed" heading so the dialog heading stays the only title', () => {
    expect(shape("## What's Changed\n### Bug Fixes\n* a")).toEqual([['heading', 'list']]);
    expect(shape('## What’s changed\n* a')).toEqual([['list']]);
  });

  it('keeps a "What\'s Changed" heading that does not open the notes', () => {
    expect(shape("Intro\n\n## What's Changed\n* a")).toEqual([['paragraph'], ['heading', 'list']]);
  });

  it('gives every heading its own section, whatever its level', () => {
    expect(shape('### A\n* a\n### B\n* b\n## New Contributors\n* c')).toEqual([
      ['heading', 'list'],
      ['heading', 'list'],
      ['heading', 'list'],
    ]);
  });

  it('moves a paragraph that follows a category list out of that category', () => {
    expect(shape('### Other\n* a\n\n**Full Changelog**: https://example.com/compare')).toEqual([['heading', 'list'], ['paragraph']]);
    expect(shape('### Breaking\n* a\n\nMigrate by hand.')).toEqual([['heading', 'list'], ['paragraph']]);
  });

  it('keeps a paragraph between a category heading and its list inside the category', () => {
    expect(shape('### A\nIntro\n* a')).toEqual([['heading', 'paragraph', 'list']]);
  });

  it('labels bare GitHub links the way the release page does and links @mentions to profiles', () => {
    const [[list]] = releaseNoteSections([
      '* Fix by @manuelmayer-dev in https://github.com/Macro-Deck-App/Macro-Deck/pull/697',
      '* Port of https://github.com/Other/Repo/issues/12, mail a@b.example',
      '* Range https://github.com/Macro-Deck-App/Macro-Deck/compare/v3.0.0-beta.2...v3.0.0-beta.3',
      '* See [the docs](https://macro-deck.app/docs) and https://macro-deck.app',
    ].join('\n'));

    const items = list.kind === 'list' ? list.items.map(item => item.blocks.flatMap(block => block.kind === 'paragraph' ? [written(block.inlines)] : [])) : [];
    expect(items).toEqual([
      ['Fix by [@manuelmayer-dev](https://github.com/manuelmayer-dev) in [#697](https://github.com/Macro-Deck-App/Macro-Deck/pull/697)'],
      ['Port of [Other/Repo#12](https://github.com/Other/Repo/issues/12), mail a@b.example'],
      ['Range [v3.0.0-beta.2...v3.0.0-beta.3](https://github.com/Macro-Deck-App/Macro-Deck/compare/v3.0.0-beta.2...v3.0.0-beta.3)'],
      ['See [the docs](https://macro-deck.app/docs) and [https://macro-deck.app](https://macro-deck.app/)'],
    ]);
  });

  it('returns no sections for notes that are only a comment', () => {
    expect(shape('<!-- generated -->\n')).toEqual([]);
  });
});
