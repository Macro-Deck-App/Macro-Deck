import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SegmentedControlComponent, SegmentedOption } from './segmented-control.component';

describe('SegmentedControlComponent', () => {
  let fixture: ComponentFixture<SegmentedControlComponent>;
  let component: SegmentedControlComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SegmentedControlComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(SegmentedControlComponent);
    component = fixture.componentInstance;
    component.options = [
      { value: 'a', label: 'A' },
      { value: 'b', label: 'B', icon: 'sun' },
    ];
    component.value = 'a';
    fixture.detectChanges();
  });

  function optionButtons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.seg-option'));
  }

  it('renders one button per option and marks the active one', () => {
    const buttons = optionButtons();
    expect(buttons.length).toBe(2);
    expect(buttons[0].classList.contains('active')).toBeTrue();
    expect(buttons[1].classList.contains('active')).toBeFalse();
  });

  it('renders an icon span when the option defines one', () => {
    expect(optionButtons()[1].querySelector('.icon-sun')).toBeTruthy();
  });

  it('emits valueChange when a different option is clicked', () => {
    const values: string[] = [];
    component.valueChange.subscribe(v => values.push(v));

    optionButtons()[1].click();
    expect(values).toEqual(['b']);
  });

  it('does not emit when the active option is clicked again', () => {
    const values: string[] = [];
    component.valueChange.subscribe(v => values.push(v));

    optionButtons()[0].click();
    expect(values).toEqual([]);
  });

  it('stays a group of pressed buttons by default, outside the tab order rules', () => {
    const group = fixture.nativeElement.querySelector('.seg') as HTMLElement;
    expect(group.getAttribute('role')).toBe('group');
    expect(optionButtons().map(button => button.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
    expect(optionButtons().some(button => button.hasAttribute('role') || button.hasAttribute('tabindex'))).toBeFalse();
  });

  describe('as a tab list', () => {
    beforeEach(async () => {
      fixture.componentRef.setInput('tablist', true);
      fixture.componentRef.setInput('idPrefix', 'demo');
      fixture.componentRef.setInput('options', [
        { value: 'a', label: 'A' },
        { value: 'b', label: 'B', badge: '3' },
        { value: 'c', label: 'C' },
      ]);
      await fixture.whenStable();
    });

    it('exposes tabs with the selected one in the tab order and tied to its panel', () => {
      const group = fixture.nativeElement.querySelector('.seg-items') as HTMLElement;
      expect(group.getAttribute('role')).toBe('tablist');
      const [first, second] = optionButtons();
      expect(first.getAttribute('role')).toBe('tab');
      expect(first.getAttribute('aria-selected')).toBe('true');
      expect(first.getAttribute('tabindex')).toBe('0');
      expect(first.getAttribute('aria-controls')).toBe('demo-panel-a');
      expect(second.getAttribute('aria-selected')).toBe('false');
      expect(second.getAttribute('tabindex')).toBe('-1');
      expect(second.hasAttribute('aria-pressed')).toBeFalse();
    });

    it('lets assistive technology read the badge as part of the tab name', () => {
      const badge = optionButtons()[1].querySelector('.seg-badge')!;
      expect(badge.hasAttribute('aria-hidden')).toBeFalse();
      expect(optionButtons()[1].hasAttribute('aria-label')).toBeFalse();
    });

    it('renders a link option as a link that reports every activation without becoming selected', async () => {
      fixture.componentRef.setInput('options', [
        { value: 'a', label: 'A' },
        { value: 'code', label: 'Code', link: true },
      ]);
      await fixture.whenStable();
      const values: string[] = [];
      component.valueChange.subscribe(v => values.push(v));

      const link = optionButtons()[1];
      link.click();
      link.click();

      expect(link.getAttribute('role')).toBe('link');
      expect(link.closest('[role="tablist"]')).toBeNull();
      expect(link.hasAttribute('aria-selected')).toBeFalse();
      expect(link.querySelector('.icon-external-link')).toBeTruthy();
      expect(values).toEqual(['code', 'code']);
    });

    it('moves focus between tabs with the arrow, Home and End keys, wrapping at the ends', () => {
      const buttons = optionButtons();
      const group = fixture.nativeElement.querySelector('[role="tablist"]') as HTMLElement;
      const press = (key: string) => group.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

      buttons[0].focus();
      press('ArrowRight');
      expect(document.activeElement).toBe(buttons[1]);
      press('End');
      expect(document.activeElement).toBe(buttons[2]);
      press('ArrowRight');
      expect(document.activeElement).toBe(buttons[0]);
      press('ArrowLeft');
      expect(document.activeElement).toBe(buttons[2]);
      press('Home');
      expect(document.activeElement).toBe(buttons[0]);
    });
  });

  it('toggles the stretch host class with the stretch input', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.classList.contains('stretch')).toBeFalse();

    fixture.componentRef.setInput('stretch', true);
    await fixture.whenStable();
    expect(host.classList.contains('stretch')).toBeTrue();
  });
});

const LONG_LABELS: SegmentedOption[] = [
  { value: 'label', label: 'Beschriftung' },
  { value: 'background', label: 'Hintergrund' },
  { value: 'border', label: 'Rahmen' },
];

@Component({
  standalone: true,
  imports: [SegmentedControlComponent],
  template: `
    <!-- The widget editor header: a fixed centre track between two flexible ones. -->
    <div class="header" [style.width.px]="width()">
      <span>Widget</span>
      <shared-segmented-control [options]="LONG_LABELS" [value]="value()" />
      <span>x</span>
    </div>

    <!-- A side panel column, where the control fills the width it is given. -->
    <div class="panel" [style.width.px]="width()">
      <shared-segmented-control [options]="LONG_LABELS" [value]="value()" [stretch]="true" />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .header {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
      overflow: hidden;
    }
    .panel { overflow: hidden; }
  `,
})
class HostComponent {
  protected readonly LONG_LABELS = LONG_LABELS;
  readonly value = signal('label');
  readonly width = signal(150);
}

describe('SegmentedControlComponent with long localized labels', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  function pill(container: '.header' | '.panel'): HTMLElement {
    return fixture.nativeElement.querySelector(`${container} .seg`);
  }

  function segments(container: '.header' | '.panel'): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll(`${container} .seg-option`));
  }

  it('shrinks to the space the row has, taking the overflow into the pill itself', () => {
    const header: HTMLElement = fixture.nativeElement.querySelector('.header');

    expect(pill('.header').getBoundingClientRect().width).toBeLessThanOrEqual(header.clientWidth);
    expect(pill('.header').scrollWidth).toBeGreaterThan(pill('.header').clientWidth);
  });

  it('renders every label in full rather than clipping it', () => {
    for (const segment of [...segments('.header'), ...segments('.panel')]) {
      expect(segment.scrollWidth).toBeLessThanOrEqual(segment.clientWidth);
    }
  });

  it('scrolls the selected segment into the pill when it sits past the visible edge', async () => {
    fixture.componentInstance.value.set('border');
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => queueMicrotask(() => resolve(null)));

    const active: HTMLElement = fixture.nativeElement.querySelector('.panel .seg-option.active');
    expect(active.getBoundingClientRect().right).toBeLessThanOrEqual(pill('.panel').getBoundingClientRect().right + 1);
  });

  it('sizes each segment by its own label once there is room to spare', async () => {
    fixture.componentInstance.width.set(600);
    fixture.detectChanges();
    await fixture.whenStable();

    const [longest, , shortest] = segments('.panel').map(segment => segment.getBoundingClientRect().width);
    expect(longest).toBeGreaterThan(shortest);
  });
});
