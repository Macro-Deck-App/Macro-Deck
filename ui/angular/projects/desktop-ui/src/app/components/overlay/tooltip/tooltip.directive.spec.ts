import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TOOLTIP_SHOW_DELAY_MS, TooltipDirective } from './tooltip.directive';

@Component({
  standalone: true,
  imports: [TooltipDirective],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<button class="host" [sharedTooltip]="text" [tooltipSecondary]="secondary"
                     [tooltipDisabled]="disabled">Icon</button>`,
})
class HostComponent {
  text: string | null = 'Deck';
  secondary: string | null = null;
  disabled = false;
}

describe('TooltipDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HTMLButtonElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    host = fixture.nativeElement.querySelector('.host');
  });

  function tooltipEl(): HTMLElement | null {
    return fixture.nativeElement.querySelector('shared-tooltip');
  }

  function showViaHover(): void {
    host.dispatchEvent(new Event('mouseenter'));
    jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
    fixture.detectChanges();
  }

  it('renders nothing before the delay elapses', () => {
    jasmine.clock().install();
    try {
      host.dispatchEvent(new Event('mouseenter'));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows with its text after the delay on mouseenter', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      const tooltip = tooltipEl();
      expect(tooltip).not.toBeNull();
      expect(tooltip!.textContent).toContain('Deck');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('cancels a pending show when the pointer leaves early', () => {
    jasmine.clock().install();
    try {
      host.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS - 1);
      host.dispatchEvent(new Event('mouseleave'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides on mouseleave', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      host.dispatchEvent(new Event('mouseleave'));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows on focus after the same delay', () => {
    jasmine.clock().install();
    try {
      host.dispatchEvent(new Event('focus'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()).not.toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides on blur', () => {
    jasmine.clock().install();
    try {
      host.dispatchEvent(new Event('focus'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();
      expect(tooltipEl()).not.toBeNull();

      host.dispatchEvent(new Event('blur'));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('keeps the tooltip open when the pointer leaves an item that still has focus', () => {
    jasmine.clock().install();
    try {
      host.dispatchEvent(new Event('focus'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      host.dispatchEvent(new Event('mouseenter'));
      host.dispatchEvent(new Event('mouseleave'));
      fixture.detectChanges();

      expect(tooltipEl()).not.toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides on click', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      host.dispatchEvent(new Event('click'));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('renders nothing while tooltipDisabled', () => {
    jasmine.clock().install();
    try {
      fixture.componentInstance.disabled = true;
      fixture.changeDetectorRef.markForCheck();
      fixture.detectChanges();

      showViaHover();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('tears an open tooltip down synchronously when tooltipDisabled flips true', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      fixture.componentInstance.disabled = true;
      fixture.changeDetectorRef.markForCheck();
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('repaints an open tooltip when its text changes', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      fixture.componentInstance.text = 'Automations';
      fixture.changeDetectorRef.markForCheck();
      fixture.detectChanges();

      expect(tooltipEl()!.textContent).toContain('Automations');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides when the text is cleared to null', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      fixture.componentInstance.text = null;
      fixture.changeDetectorRef.markForCheck();
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('renders the secondary line only when secondary text is provided', () => {
    jasmine.clock().install();
    try {
      fixture.componentInstance.secondary = 'Coming Soon';
      fixture.changeDetectorRef.markForCheck();
      fixture.detectChanges();

      showViaHover();

      expect(tooltipEl()!.querySelector('.tt-secondary')!.textContent).toContain('Coming Soon');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('renders no secondary line when none is provided', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      expect(tooltipEl()!.querySelector('.tt-secondary')).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('marks the tooltip aria-hidden', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      expect(tooltipEl()!.getAttribute('aria-hidden')).toBe('true');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('inserts the tooltip as a sibling of the host, never a descendant', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      expect(host.querySelector('shared-tooltip')).toBeNull();
      expect(fixture.nativeElement.querySelector('shared-tooltip')).not.toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('positions itself with position: fixed beside the host', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      const box = tooltipEl()!.querySelector('.tt') as HTMLElement;
      expect(getComputedStyle(box).position).toBe('fixed');
      expect(parseFloat(box.style.left)).toBeGreaterThanOrEqual(host.getBoundingClientRect().right);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('takes up no layout box of its own', () => {
    jasmine.clock().install();
    try {
      showViaHover();

      expect(getComputedStyle(tooltipEl()!).display).toBe('contents');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('keeps a single tooltip instance across repeated mouseenter events', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      showViaHover();

      expect(fixture.nativeElement.querySelectorAll('shared-tooltip').length).toBe(1);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides on Escape', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides when the window loses focus', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      window.dispatchEvent(new Event('blur'));
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('removes the tooltip on destroy', () => {
    jasmine.clock().install();
    try {
      showViaHover();
      expect(tooltipEl()).not.toBeNull();

      fixture.destroy();

      expect(document.querySelectorAll('shared-tooltip').length).toBe(0);
    } finally {
      jasmine.clock().uninstall();
    }
  });
});
