import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AnnouncementMarkdownComponent } from './announcement-markdown.component';

describe('AnnouncementMarkdownComponent', () => {
  let openExternal: jasmine.Spy;

  beforeEach(() => {
    openExternal = jasmine.createSpy('openExternal').and.resolveTo(true);
    (window as { macroDeckShell?: unknown }).macroDeckShell = { openExternal };
    TestBed.configureTestingModule({
      imports: [AnnouncementMarkdownComponent],
      providers: [provideZonelessChangeDetection()],
    });
  });

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function render(markdown: string): HTMLElement {
    const fixture: ComponentFixture<AnnouncementMarkdownComponent> = TestBed.createComponent(AnnouncementMarkdownComponent);
    fixture.componentRef.setInput('text', markdown);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders GitHub-flavoured Markdown the way the Creator Portal preview does', () => {
    const element = render([
      '## What\'s new',
      '#### Details',
      '- **Bold** and _em_ and ~~gone~~',
      '  - nested',
      '- [x] done',
      '',
      '| Feature | State |',
      '|:--|--:|',
      '| Decks | `ready` |',
      '',
      '> quoted',
      '',
      '---',
    ].join('\n'));

    expect(Array.from(element.querySelectorAll('h4, h5')).map(heading => heading.textContent?.trim()))
      .toEqual(['What\'s new', 'Details']);
    expect(element.querySelector('strong')?.textContent).toBe('Bold');
    expect(element.querySelector('em')?.textContent).toBe('em');
    expect(element.querySelector('del')?.textContent).toBe('gone');
    expect(element.querySelector('ul ul li')?.textContent?.trim()).toBe('nested');
    expect(element.textContent).not.toContain('####');
    expect(element.textContent).not.toContain('[x]');

    const checkboxes = element.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    expect(checkboxes.length).toBe(1);
    expect(checkboxes[0].checked).toBeTrue();
    expect(checkboxes[0].disabled).toBeTrue();

    expect(Array.from(element.querySelectorAll('th')).map(cell => cell.textContent?.trim())).toEqual(['Feature', 'State']);
    expect(element.querySelector('td code')?.textContent).toBe('ready');
    expect(element.querySelector('blockquote')?.textContent?.trim()).toBe('quoted');
    expect(element.querySelector('hr')).not.toBeNull();
  });

  it('shows raw HTML as text and never interprets it', () => {
    const element = render('<script>alert(1)</script>\n\nA <b>tag</b> and <!-- a comment -->');

    expect(element.querySelector('script')).toBeNull();
    expect(element.querySelector('b')).toBeNull();
    expect(element.textContent).toContain('<script>alert(1)</script>');
    expect(element.textContent).toContain('<b>tag</b>');
    expect(element.textContent).toContain('<!-- a comment -->');
  });

  it('shows a comment on its own line as text, like the Creator Portal preview', () => {
    expect(render('<!-- a block comment -->\n\nAfter').textContent).toContain('<!-- a block comment -->');
  });

  it('decodes character references in text but keeps code literal', () => {
    const element = render('&lt;b&gt; &copy; &#169; &amp; `&amp;`');

    expect(element.querySelector('p')?.textContent).toBe('<b> © © & &amp;');
  });

  it('opens http, https and mailto links in the browser and never navigates the app', () => {
    const element = render('[a](https://macro-deck.app/notes "Notes") [b](http://example.com) [c](mailto:hi@macro-deck.app) '
      + '[d](javascript:alert(1)) [e](file:///etc/passwd) [f](/relative)');

    const links = Array.from(element.querySelectorAll('a'));
    expect(links.map(link => link.textContent)).toEqual(['a', 'b', 'c']);
    expect(links[0].title).toBe('Notes');
    expect(element.textContent).toContain('d');
    expect(element.textContent).toContain('f');

    const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
    links[2].dispatchEvent(click);
    const middle = new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 });
    links[0].dispatchEvent(middle);

    expect(click.defaultPrevented).toBeTrue();
    expect(middle.defaultPrevented).toBeTrue();
    expect(openExternal.calls.allArgs()).toEqual([['mailto:hi@macro-deck.app'], ['https://macro-deck.app/notes']]);
  });

  it('embeds https images and videos and shows everything else as its alt text', () => {
    const element = render([
      '![A screenshot](https://store-assets.macro-deck.app/announcements/media/abc.PNG?v=1)',
      '',
      '![A clip](https://store-assets.macro-deck.app/announcements/media/abc.webm)',
      '',
      '![Insecure](http://store-assets.macro-deck.app/announcements/media/abc.png)',
      '',
      '![Unknown type](https://store-assets.macro-deck.app/announcements/media/abc.svg)',
    ].join('\n'));

    const image = element.querySelector('img')!;
    const video = element.querySelector('video')!;
    expect(element.querySelectorAll('img, video').length).toBe(2);
    expect(image.alt).toBe('A screenshot');
    expect(video.getAttribute('aria-label')).toBe('A clip');
    expect(video.controls).toBeTrue();
    expect(video.muted).toBeTrue();
    expect(video.autoplay).toBeFalse();
    expect(element.textContent).toContain('Insecure');
    expect(element.textContent).toContain('Unknown type');
  });

  it('shows the alt text of media that cannot be loaded', () => {
    const fixture = TestBed.createComponent(AnnouncementMarkdownComponent);
    fixture.componentRef.setInput('text', '![A clip](https://store-assets.macro-deck.app/announcements/media/abc.mp4)');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('video').dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('video')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('A clip');
  });
});
