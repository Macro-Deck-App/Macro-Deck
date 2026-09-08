import { Component, provideZonelessChangeDetection, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  isWorkspaceConstrained,
  SIDEBAR_COLLAPSED_WIDTH,
  SIDEBAR_EXPANDED_WIDTH,
} from '../../../../../services/navigation.service';
import { WidgetEditorShellComponent } from './widget-editor-shell.component';
import {
  EDITOR_LAYOUT_GAP_REM,
  EDITOR_MAIN_MIN_WIDTH_REM,
  EDITOR_SIDEBAR_WIDTH,
  isEditorCompact,
} from './widget-editor-shell.metrics';
import { provideLocalizationTesting } from '../../../../../../testing/localization-test-support';

const compactThreshold = (rootFontSizePx: number): number =>
  EDITOR_SIDEBAR_WIDTH + (EDITOR_MAIN_MIN_WIDTH_REM + EDITOR_LAYOUT_GAP_REM) * rootFontSizePx;

describe('isEditorCompact', () => {
  it('stays wide just above the threshold at the default 16px root', () => {
    expect(isEditorCompact(compactThreshold(16) + 1, 16)).toBeFalse();
  });

  it('goes compact just below the threshold at the default 16px root', () => {
    expect(isEditorCompact(compactThreshold(16) - 1, 16)).toBeTrue();
  });

  it('stays wide exactly at the threshold (a strict less-than comparison)', () => {
    expect(isEditorCompact(compactThreshold(16), 16)).toBeFalse();
  });

  it('scales with the root font size, so a larger browser font moves the breakpoint', () => {
    const width = compactThreshold(16) + 1;
    expect(isEditorCompact(width, 16)).toBeFalse();
    expect(isEditorCompact(width, 32)).toBeTrue();
  });
});

describe('the editor compact breakpoint against the nav rail collapse', () => {
  const BASE_ROOT_FONT_SIZE = 16;

  const MIN_SUPPORTED_ROOT = 12;
  const MAX_SUPPORTED_ROOT = 24;

  const editorContentWidth = (viewportWidth: number, rootFontSizePx: number): number => {
    const railWidth = isWorkspaceConstrained(viewportWidth, rootFontSizePx)
      ? SIDEBAR_COLLAPSED_WIDTH
      : SIDEBAR_EXPANDED_WIDTH;
    return viewportWidth - railWidth - 2 * 0.8125 * rootFontSizePx;
  };

  const countFlips = (rootFontSizePx: number): number => {
    const states: boolean[] = [];
    for (let viewport = 2200; viewport >= 600; viewport -= 2) {
      states.push(isEditorCompact(editorContentWidth(viewport, rootFontSizePx), rootFontSizePx));
    }
    return states.filter((state, index) => index > 0 && state !== states[index - 1]).length;
  };

  it('flips at most once while the window is dragged across the whole supported range', () => {
    expect(countFlips(BASE_ROOT_FONT_SIZE)).toBe(1);
  });

  it('flips at most once at every supported root font size, not just the default one', () => {
    for (let rootFontSizePx = MIN_SUPPORTED_ROOT; rootFontSizePx <= MAX_SUPPORTED_ROOT; rootFontSizePx += 1) {
      expect(countFlips(rootFontSizePx)).withContext(`root font size ${rootFontSizePx}px`).toBe(1);
    }
  });

  it('is wide at the widest end of the range and compact at the narrowest', () => {
    expect(isEditorCompact(editorContentWidth(2200, BASE_ROOT_FONT_SIZE), BASE_ROOT_FONT_SIZE)).toBeFalse();
    expect(isEditorCompact(editorContentWidth(600, BASE_ROOT_FONT_SIZE), BASE_ROOT_FONT_SIZE)).toBeTrue();
  });
});

@Component({
  standalone: true,
  imports: [WidgetEditorShellComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <app-widget-editor-shell>
      <ng-container editorSidebar>
        <div id="projected-sidebar">appearance</div>
      </ng-container>
      <div id="projected-main">actions</div>
    </app-widget-editor-shell>
  `,
})
class HostComponent {
  @ViewChild(WidgetEditorShellComponent) shell!: WidgetEditorShellComponent;
}

describe('WidgetEditorShellComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const rootFontSize = (): number => parseFloat(getComputedStyle(document.documentElement).fontSize);
  const wideWidth = (): number => compactThreshold(rootFontSize()) + 200;
  const narrowWidth = (): number => compactThreshold(rootFontSize()) - 200;

  const query = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const apply = (width: number): void => {
    host.shell.applyAvailableWidth(width);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the sidebar at the width the breakpoint arithmetic assumes', () => {
    apply(wideWidth());

    expect(getComputedStyle(query('.editor-shell-sidebar')!).width).toBe(`${EDITOR_SIDEBAR_WIDTH}px`);
  });

  it('keeps the appearance pane on the left, ahead of the actions in the tab order', () => {
    apply(wideWidth());

    const sidebar = query('.editor-shell-sidebar')!;
    const main = query('.editor-shell-main')!;

    expect(sidebar.getBoundingClientRect().left).toBeLessThan(main.getBoundingClientRect().left);
    expect(sidebar.compareDocumentPosition(main) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('paints the open drawer over the main pane, masked icons and all', () => {
    apply(narrowWidth());
    query('.editor-shell-appearance-toggle')?.click();
    fixture.detectChanges();

    const drawer = query('.editor-shell-sidebar')!;
    const rect = drawer.getBoundingClientRect();
    const topLeft = document.elementFromPoint(rect.left + 5, rect.top + 5);

    expect(drawer.contains(topLeft)).toBeTrue();
    expect(getComputedStyle(drawer).zIndex).not.toBe('auto');
  });

  it('lays both panes out side by side when there is room', () => {
    apply(wideWidth());

    expect(host.shell.isCompact()).toBeFalse();
    expect(query('.editor-shell--compact')).toBeNull();
    expect(query('.editor-shell-appearance-toggle')).toBeNull();
    expect(query('.editor-shell-sidebar')?.hasAttribute('inert')).toBeFalse();
  });

  it('collapses the sidebar into a closed drawer once the editor is too narrow', () => {
    apply(narrowWidth());

    expect(host.shell.isCompact()).toBeTrue();
    expect(query('.editor-shell--compact')).not.toBeNull();
    expect(query('.editor-shell-sidebar')?.hasAttribute('inert')).toBeTrue();

    const toggle = query('.editor-shell-appearance-toggle');
    expect(toggle).not.toBeNull();
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(toggle?.getAttribute('aria-controls')).toBe(query('.editor-shell-sidebar')?.id);
  });

  it('opens the drawer from the toggle and closes it from the scrim', () => {
    apply(narrowWidth());

    query('.editor-shell-appearance-toggle')?.click();
    fixture.detectChanges();

    expect(host.shell.drawerOpen()).toBeTrue();
    expect(query('.editor-shell-sidebar')?.hasAttribute('inert')).toBeFalse();
    expect(query('.editor-shell-appearance-toggle')?.getAttribute('aria-expanded')).toBe('true');
    expect(getComputedStyle(query('.editor-shell-scrim')!).pointerEvents).toBe('auto');

    query('.editor-shell-scrim')?.click();
    fixture.detectChanges();

    expect(host.shell.drawerOpen()).toBeFalse();
    expect(query('.editor-shell-sidebar')?.hasAttribute('inert')).toBeTrue();
  });

  it('keeps Events, Sync State and the appearance form mounted across a layout change', () => {
    apply(wideWidth());
    const sidebarNode = query('#projected-sidebar');
    const mainNode = query('#projected-main');

    apply(narrowWidth());
    expect(query('#projected-sidebar')).toBe(sidebarNode);
    expect(query('#projected-main')).toBe(mainNode);

    apply(wideWidth());
    expect(query('#projected-sidebar')).toBe(sidebarNode);
    expect(query('#projected-main')).toBe(mainNode);
  });

  it('parks the drawer when the window widens back into the two-pane layout', () => {
    apply(narrowWidth());
    query('.editor-shell-appearance-toggle')?.click();
    fixture.detectChanges();
    expect(host.shell.drawerOpen()).toBeTrue();

    apply(wideWidth());

    expect(host.shell.drawerOpen()).toBeFalse();
    expect(host.shell.isCompact()).toBeFalse();
    expect(query('.editor-shell-sidebar')?.hasAttribute('inert')).toBeFalse();
  });

  it('insets the sidebar content equally on both sides once a scrollbar takes layout space', () => {
    const style = document.createElement('style');
    style.textContent = '.editor-shell-sidebar-scroll::-webkit-scrollbar { width: 20px; }';
    document.head.append(style);

    try {
      apply(wideWidth());

      const card = query('.editor-shell-sidebar')!.getBoundingClientRect();
      const content = query('#projected-sidebar')!.getBoundingClientRect();
      const scroll = query('.editor-shell-sidebar-scroll') as HTMLElement;

      expect(scroll.offsetWidth - scroll.clientWidth).toBeGreaterThan(0);
      expect(content.left - card.left).toBeCloseTo(card.right - content.right, 0);
    } finally {
      style.remove();
    }
  });

  it('contains vertical overflow inside the drawer at a short window height', () => {
    const shell = query('.editor-shell') as HTMLElement;
    shell.style.height = '420px';
    apply(narrowWidth());

    query('.editor-shell-appearance-toggle')?.click();
    fixture.detectChanges();

    const scroll = query('.editor-shell-sidebar-scroll') as HTMLElement;
    const filler = document.createElement('div');
    filler.style.flex = '0 0 900px';
    scroll.append(filler);

    try {
      expect(getComputedStyle(scroll).overflowY).toBe('auto');
      expect(scroll.scrollHeight).toBeGreaterThan(scroll.clientHeight);
      expect(shell.scrollHeight).toBeLessThanOrEqual(shell.clientHeight + 1);
    } finally {
      filler.remove();
      shell.style.removeProperty('height');
    }
  });
});
