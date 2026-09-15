import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { provideLocalizationTesting } from '../../../localization/localization-test-support';
import { ModalComponent } from '../modal/modal.component';
import { OverlayPanelComponent } from '../overlay-panel/overlay-panel.component';
import { SidePanelComponent } from './side-panel.component';

@Component({
  standalone: true,
  imports: [SidePanelComponent, ModalComponent, OverlayPanelComponent],
  template: `
    <button
      type="button"
      class="outside"
      style="position: fixed; top: 0; left: 0; right: 0; height: 40px">
      Outside
    </button>
    <div class="stage" style="position: fixed; top: 40px; right: 0; bottom: 0; left: 0">
      <button
        type="button"
        class="page"
        style="position: absolute; inset: 0; width: 100%; height: 100%"
        (click)="pageClicks = pageClicks + 1">
        Page
      </button>
      <shared-side-panel
        [isOpen]="open()"
        [edge]="edge()"
        heading="Panel heading"
        closeLabel="Close the panel"
        (closed)="onClosed()">
        <button sidePanelActions type="button" class="action">Action</button>
        <button type="button" class="body-button">Body</button>
      </shared-side-panel>
      @if (modalOpen()) {
        <shared-modal heading="Modal">Modal body</shared-modal>
      }
      <shared-overlay-panel [isOpen]="dropdownOpen()" [x]="0" [y]="0">Dropdown</shared-overlay-panel>
    </div>
  `,
})
class HostComponent {
  readonly open = signal(false);
  readonly edge = signal<'start' | 'end'>('start');
  readonly modalOpen = signal(false);
  readonly dropdownOpen = signal(false);
  pageClicks = 0;
  closedCount = 0;

  onClosed(): void {
    this.closedCount++;
    this.open.set(false);
  }
}

const selectorsOf = (rule: CSSStyleRule): string[] =>
  rule.selectorText.split(',').map(selector => selector.replace(/\[[^\]]*\]/g, '').trim());

describe('SidePanelComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  function query<T extends HTMLElement = HTMLElement>(selector: string): T {
    return fixture.nativeElement.querySelector(selector) as T;
  }

  function render(): void {
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).getAnimations({ subtree: true }).forEach(animation => animation.finish());
  }

  function pagePoint(): { x: number; y: number } {
    const stage = query('.stage').getBoundingClientRect();
    return {
      x: host.edge() === 'start' ? stage.right - 5 : stage.left + 5,
      y: stage.top + stage.height / 2,
    };
  }

  function pressEscape(): jasmine.Spy {
    const laterListener = jasmine.createSpy('later document keydown listener');
    document.addEventListener('keydown', laterListener);
    try {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    } finally {
      document.removeEventListener('keydown', laterListener);
    }
    return laterListener;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    render();
  });

  it('lets every click through to the page while closed', () => {
    const stage = query('.stage').getBoundingClientRect();
    const { x, y } = pagePoint();

    expect(document.elementFromPoint(x, y)).toBe(query('.page'));
    expect(document.elementFromPoint(stage.left + 5, y)).toBe(query('.page'));
    expect(query('.sp-panel').hasAttribute('inert')).toBeTrue();
  });

  it('dims the page while open and closes on a click there without the page receiving it', () => {
    host.open.set(true);
    render();

    const { x, y } = pagePoint();
    const hit = document.elementFromPoint(x, y) as HTMLElement;
    expect(hit).toBe(query('.sp-scrim'));
    expect(getComputedStyle(hit).opacity).toBe('1');

    hit.click();
    render();

    expect(host.closedCount).toBe(1);
    expect(host.pageClicks).toBe(0);
  });

  it('dims only the container it sits in, so controls outside it stay usable while it is open', () => {
    host.open.set(true);
    render();

    const outside = query('.outside');
    const rect = outside.getBoundingClientRect();
    const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);

    expect(hit).toBe(outside);
  });

  it('stays open on a click inside the panel', () => {
    host.open.set(true);
    render();

    const rect = query('.sp-panel').getBoundingClientRect();
    const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2) as HTMLElement;
    expect(query('.sp-panel').contains(hit)).toBeTrue();

    hit.click();

    expect(host.closedCount).toBe(0);
  });

  it('slides in from the start edge and parks past it', () => {
    const stage = query('.stage').getBoundingClientRect();
    const panel = query('.sp-panel');
    expect(panel.getBoundingClientRect().right).toBeLessThanOrEqual(stage.left + 0.5);

    host.open.set(true);
    render();

    expect(panel.getBoundingClientRect().left).toBeCloseTo(stage.left, 0);
  });

  it('slides in from the end edge and parks past it', () => {
    host.edge.set('end');
    render();
    const stage = query('.stage').getBoundingClientRect();
    const panel = query('.sp-panel');
    expect(panel.getBoundingClientRect().left).toBeGreaterThanOrEqual(stage.right - 0.5);

    host.open.set(true);
    render();

    expect(panel.getBoundingClientRect().right).toBeCloseTo(stage.right, 0);
  });

  it('renders its heading, the projected content and a labelled close button that closes it', () => {
    host.open.set(true);
    render();

    expect(query('.sp-title').textContent?.trim()).toBe('Panel heading');
    expect(query('.sp-panel').getAttribute('aria-label')).toBe('Panel heading');
    expect(query('.sp-header .action')).not.toBeNull();
    expect(query('.sp-body .body-button')).not.toBeNull();

    const close = query<HTMLButtonElement>('.sp-close');
    expect(close.getAttribute('aria-label')).toBe('Close the panel');
    close.click();

    expect(host.closedCount).toBe(1);
  });

  it('closes on Escape while open and keeps the key from page handlers registered after it', () => {
    host.open.set(true);
    render();

    const laterListener = pressEscape();

    expect(host.closedCount).toBe(1);
    expect(laterListener).not.toHaveBeenCalled();
  });

  it('leaves Escape alone while closed', () => {
    const laterListener = pressEscape();

    expect(host.closedCount).toBe(0);
    expect(laterListener).toHaveBeenCalled();
  });

  it('leaves Escape to a modal open above it', () => {
    host.open.set(true);
    host.modalOpen.set(true);
    render();

    pressEscape();

    expect(host.closedCount).toBe(0);
  });

  it('leaves Escape to a dropdown open above it', () => {
    host.open.set(true);
    host.dropdownOpen.set(true);
    render();

    pressEscape();

    expect(host.closedCount).toBe(0);
    host.dropdownOpen.set(false);
    render();
  });

  it('hands focus back to the control that opened it when it closes from inside', async () => {
    const opener = query<HTMLButtonElement>('.page');
    opener.focus();
    host.open.set(true);
    render();
    query<HTMLButtonElement>('.sp-close').focus();
    expect(document.activeElement).toBe(query('.sp-close'));

    query<HTMLButtonElement>('.sp-close').click();
    render();
    await fixture.whenStable();

    expect(document.activeElement).toBe(opener);
  });

  it('leaves focus alone when a modal opens as it closes', async () => {
    const opener = query<HTMLButtonElement>('.page');
    opener.focus();
    host.open.set(true);
    render();
    query<HTMLButtonElement>('.sp-close').focus();
    expect(document.activeElement).toBe(query('.sp-close'));

    host.open.set(false);
    host.modalOpen.set(true);
    render();
    await fixture.whenStable();

    expect(document.activeElement).not.toBe(opener);
  });

  it('drops the slide and the fade when reduced motion is preferred, after the base rules', () => {
    let found = false;
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRule[];
      try {
        rules = Array.from(sheet.cssRules);
      } catch {
        continue;
      }

      const isOpenRule = (rule: CSSRule): rule is CSSStyleRule =>
        rule instanceof CSSStyleRule && selectorsOf(rule).includes('.sp-panel-open');
      const mediaIndex = rules.findIndex(rule => rule instanceof CSSMediaRule
        && rule.conditionText.includes('prefers-reduced-motion')
        && Array.from(rule.cssRules).some(isOpenRule));
      if (mediaIndex < 0) continue;
      found = true;

      const reduced = Array.from((rules[mediaIndex] as CSSMediaRule).cssRules).find(isOpenRule)!;
      expect(selectorsOf(reduced)).toEqual(jasmine.arrayContaining(['.sp-panel', '.sp-panel-open', '.sp-scrim']));
      expect(reduced.style.transitionProperty).toBe('none');
      expect(reduced.style.transitionDelay).toBe('0s');

      const baseIndex = rules.findIndex(isOpenRule);
      expect(baseIndex).toBeGreaterThanOrEqual(0);
      expect(mediaIndex).toBeGreaterThan(baseIndex);
    }

    expect(found).toBeTrue();
  });
});
