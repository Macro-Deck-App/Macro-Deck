import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StoreMarkdownComponent } from './store-markdown.component';

describe('StoreMarkdownComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StoreMarkdownComponent],
      providers: [provideZonelessChangeDetection()],
    });
  });

  function render(markdown: string): HTMLElement {
    const fixture = TestBed.createComponent(StoreMarkdownComponent);
    fixture.componentRef.setInput('text', markdown);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders a plugin description the way GitHub lays it out', () => {
    const element = render([
      '[![ko-fi](https://badges.example.invalid/kofi.svg)](https://ko-fi.com/S5O622WBIQ)',
      '',
      '1. Open **Integrations**.',
      '2. Pick your bridge, or type the',
      '   bridge\'s IP address.',
      '3. Press the link button.',
      '',
      '| Action | What it does |',
      '| --- | --- |',
      '| Set scene | Recalls a scene. |',
      '| Update light | Sets lights. |',
      '',
      '- To find bridges:',
      '  1. mDNS',
      '  2. a subnet scan',
    ].join('\n'));

    const steps = Array.from(element.querySelectorAll(':scope .md > ol > li')).map(item => item.textContent?.replace(/\s+/g, ' ').trim());
    expect(steps).toEqual(['Open Integrations.', 'Pick your bridge, or type the bridge\'s IP address.', 'Press the link button.']);
    expect(Array.from(element.querySelectorAll('th')).map(cell => cell.textContent?.trim())).toEqual(['Action', 'What it does']);
    expect(element.querySelectorAll('tbody tr').length).toBe(2);
    expect(Array.from(element.querySelectorAll('ul > li ol > li')).map(item => item.textContent?.trim())).toEqual(['mDNS', 'a subnet scan']);
    expect(element.textContent).not.toContain('![');
    expect(element.textContent).not.toContain('| ---');
  });

  it('shows a linked badge image without sending a referrer, and its description when it cannot load', () => {
    const fixture = TestBed.createComponent(StoreMarkdownComponent);
    fixture.componentRef.setInput('text', '[![ko-fi](https://badges.example.invalid/kofi.svg)](https://ko-fi.com/S5O622WBIQ)');
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const link = element.querySelector('a')!;
    const image = link.querySelector('img')!;
    expect(link.getAttribute('href')).toBe('https://ko-fi.com/S5O622WBIQ');
    expect(link.target).toBe('_blank');
    expect(image.getAttribute('src')).toBe('https://badges.example.invalid/kofi.svg');
    expect(image.alt).toBe('ko-fi');
    expect(image.referrerPolicy).toBe('no-referrer');

    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('a')?.textContent?.trim()).toBe('ko-fi');
  });

  it('renders an img tag like a Markdown image and never lets other attributes reach the DOM', () => {
    const fixture = TestBed.createComponent(StoreMarkdownComponent);
    fixture.componentRef.setInput('text', '<img width="320" height="200" alt="Shot" src="https://example.com/a.png" onerror="alert(1)" style="x:y">');
    fixture.detectChanges();
    const image = (fixture.nativeElement as HTMLElement).querySelector('img')!;

    expect(image.getAttribute('src')).toBe('https://example.com/a.png');
    expect(image.alt).toBe('Shot');
    expect(image.getAttribute('width')).toBe('320');
    expect(image.getAttribute('height')).toBe('200');
    expect(image.referrerPolicy).toBe('no-referrer');
    expect(image.hasAttribute('onerror')).toBeFalse();
    expect(image.hasAttribute('style')).toBeFalse();
  });

  it('shows the description of an img tag with an insecure source', () => {
    const fixture = TestBed.createComponent(StoreMarkdownComponent);
    fixture.componentRef.setInput('text', '<img alt="Fallback" src="http://example.com/a.png">');
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('img')).toBeNull();
    expect(element.textContent?.trim()).toBe('Fallback');
  });

  it('keeps the start number of an ordered list', () => {
    expect(render('3. third\n4. fourth').querySelector('ol')?.getAttribute('start')).toBe('3');
  });
});
