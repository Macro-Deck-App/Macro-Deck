import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { SnippetInsertion, TemplateSnippetListComponent } from './template-snippet-list.component';

describe('TemplateSnippetListComponent', () => {
  let fixture: ComponentFixture<TemplateSnippetListComponent>;
  let component: TemplateSnippetListComponent;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getVariables', 'onNotification']);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [TemplateSnippetListComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(TemplateSnippetListComponent);
    component = fixture.componentInstance;
  });

  it('gives every filter a description that is not just its own name', () => {
    for (const category of component.categories) {
      for (const filter of component.filtersByCategory(category)) {
        const hint = component.hint(filter);
        expect(hint).withContext(filter.key).not.toBe('');
        expect(hint).withContext(filter.key).not.toBe(filter.label);
      }
    }
  });

  it('gives every logic snippet a description and a one-line example', () => {
    for (const snippet of component.controlSnippets()) {
      const hint = component.hint(snippet);
      expect(hint).withContext(snippet.key).not.toBe('');
      expect(hint).withContext(snippet.key).not.toBe(snippet.label);
      expect(snippet.example).withContext(snippet.key).not.toBe('');
      expect(snippet.example).withContext(snippet.key).not.toContain('\n');
    }
  });

  // The browser pane is a narrow column, and a monospace example spelling out both tags of a
  // control-flow construct is wider than it. Anything that cannot shrink or wrap makes the whole
  // pane scroll sideways, which is what "usable at smaller window sizes" rules out.
  for (const mode of ['filters', 'control-flow'] as const) {
    it(`never scrolls sideways in ${mode} mode, however narrow the pane`, async () => {
      // The harness loads no global tokens, so --text-xs resolves to nothing and the example's real
      // width is unknown. A pane this narrow overflows at any plausible font size, which is what
      // makes the assertion mean something here.
      fixture.nativeElement.style.width = '120px';
      fixture.nativeElement.style.height = '400px';
      component.mode = mode;
      fixture.detectChanges();
      await fixture.whenStable();

      const host = fixture.nativeElement as HTMLElement;
      expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
    });
  }

  it('wraps a filter in braces so it still renders when the caret is not inside a tag', () => {
    const emitted: SnippetInsertion[] = [];
    component.insert.subscribe(v => emitted.push(v));

    const upcase = component.filtersByCategory('text').find(f => f.label === 'upcase')!;
    component.insertFilter(upcase);

    // The bare fragment stays available for a caret that IS inside a tag; the wrapped form is what
    // an empty editor needs, with the caret where the expression to filter belongs.
    expect(emitted[0].text).toBe(' | upcase');
    expect(emitted[0].wrapped).toBe('{{ | upcase }}');
    expect(emitted[0].wrapped!.slice(0, emitted[0].wrappedCaret)).toBe('{{ ');
  });

  describe('search', () => {
    it('narrows the filter list by name', async () => {
      component.search.set('upcase');
      fixture.detectChanges();
      await fixture.whenStable();

      const names = component.categories.flatMap(c => component.filtersByCategory(c)).map(f => f.label);
      expect(names).toContain('upcase');
      expect(names).not.toContain('round');
    });

    it('narrows by description, not only by name', async () => {
      // "decimals" appears in round's description, not in any filter name.
      component.search.set('decimals');
      fixture.detectChanges();
      await fixture.whenStable();

      const names = component.categories.flatMap(c => component.filtersByCategory(c)).map(f => f.label);
      expect(names).toContain('round');
      expect(names).not.toContain('upcase');
    });

    it('narrows the logic list too', async () => {
      component.mode = 'control-flow';
      component.search.set('unless');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.controlSnippets().map(s => s.label)).toEqual(['unless']);
    });

    it('reports no matches for a query nothing satisfies', async () => {
      component.search.set('zzzznothing');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.hasMatches()).toBeFalse();
    });
  });

  it('inserts a logic snippet with the caret inside the tag that needs a value', async () => {
    component.mode = 'control-flow';
    const emitted: SnippetInsertion[] = [];
    component.insert.subscribe(value => emitted.push(value));

    const ifElse = component.controlSnippets().find(s => s.key === 'if-else')!;
    component.insertControl(ifElse);

    expect(emitted).toHaveSize(1);
    expect(emitted[0].text).toContain('{% endif %}');
    expect(emitted[0].text.slice(0, emitted[0].caret)).toBe('{% if ');
  });
});
