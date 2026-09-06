import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';

import { TabBarComponent } from './tab-bar.component';
import type { TabItem } from './tab-bar.model';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('TabBarComponent', () => {
  let fixture: ComponentFixture<TabBarComponent>;
  let component: TabBarComponent;

  const items: TabItem[] = Array.from({ length: 8 }, (_, index) => ({
    id: `tab-${index}`,
    label: `Trigger number ${index}`,
  }));

  const bar = (): HTMLElement => fixture.nativeElement.querySelector('.tab-bar');
  const activeTab = (): HTMLElement | null => fixture.nativeElement.querySelector('.tab-item.active');

  const selectAndSettle = (id: string): void => {
    fixture.componentRef.setInput('activeId', id);
    fixture.detectChanges();
    component.ensureActiveVisible();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TabBarComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(TabBarComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('activeId', items[0].id);

    (fixture.nativeElement as HTMLElement).style.width = '200px';
    fixture.detectChanges();
  });

  afterEach(() => {
    (fixture.nativeElement as HTMLElement).style.removeProperty('width');
  });

  it('scrolls the bar rather than widening it, so the row it sits in cannot be pushed out', () => {
    expect(getComputedStyle(bar()).overflowX).toBe('auto');
    expect(bar().scrollWidth).toBeGreaterThan(bar().clientWidth);
  });

  it('brings the active tab back into view when a later tab is selected', () => {
    selectAndSettle(items[items.length - 1].id);

    const barRect = bar().getBoundingClientRect();
    const tabRect = activeTab()!.getBoundingClientRect();

    expect(tabRect.right).toBeLessThanOrEqual(barRect.right + 1);
    expect(tabRect.left).toBeGreaterThanOrEqual(barRect.left - 1);
  });

  it('scrolls back when the selection returns to the first tab', () => {
    selectAndSettle(items[items.length - 1].id);
    expect(bar().scrollLeft).toBeGreaterThan(0);

    selectAndSettle(items[0].id);

    expect(bar().scrollLeft).toBe(0);
    const barRect = bar().getBoundingClientRect();
    const tabRect = activeTab()!.getBoundingClientRect();
    expect(tabRect.left).toBeGreaterThanOrEqual(barRect.left - 1);
  });

  it('leaves the scroll position alone when the active tab is already fully visible', () => {
    selectAndSettle(items[items.length - 1].id);
    const before = bar().scrollLeft;
    expect(before).toBeGreaterThan(0);

    component.ensureActiveVisible();

    expect(bar().scrollLeft).toBe(before);
  });

  describe('idPrefix and roving focus', () => {
    const tabButtons = (): HTMLButtonElement[] =>
      Array.from(fixture.nativeElement.querySelectorAll('.tab-item'));

    it('leaves id/aria-controls off the tabs when idPrefix is not set', () => {
      const button = tabButtons()[0];
      expect(button.hasAttribute('id')).toBeFalse();
      expect(button.hasAttribute('aria-controls')).toBeFalse();
    });

    it('produces id/aria-controls from idPrefix when set', () => {
      fixture.componentRef.setInput('idPrefix', 'catalog');
      fixture.detectChanges();

      const button = tabButtons()[0];
      expect(button.id).toBe(`catalog-tab-${items[0].id}`);
      expect(button.getAttribute('aria-controls')).toBe(`catalog-panel-${items[0].id}`);
    });

    it('only gives the active tab tabindex 0, the rest -1', () => {
      const buttons = tabButtons();
      expect(buttons[0].getAttribute('tabindex')).toBe('0');
      expect(buttons[1].getAttribute('tabindex')).toBe('-1');
    });

    it('keeps aria-controls off the inactive tabs, whose panels are not rendered', () => {
      fixture.componentRef.setInput('idPrefix', 'catalog');
      fixture.detectChanges();

      expect(tabButtons()[1].hasAttribute('aria-controls')).toBeFalse();
    });

    it('keeps the bar reachable when activeId matches no tab', () => {
      // The action builder binds '' to its aux bar while unselected; roving focus must not then
      // drop every tab out of the page's tab order.
      fixture.componentRef.setInput('activeId', '');
      fixture.detectChanges();

      const buttons = tabButtons();
      expect(buttons[0].getAttribute('tabindex')).toBe('0');
      expect(buttons[1].getAttribute('tabindex')).toBe('-1');
    });

    it('moves focus to the next tab on ArrowRight and wraps at the end', () => {
      const buttons = tabButtons();
      buttons[0].focus();

      component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
      expect(document.activeElement).toBe(buttons[1]);

      buttons[buttons.length - 1].focus();
      component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
      expect(document.activeElement).toBe(buttons[0]);
    });

    it('moves focus to the previous tab on ArrowLeft and wraps at the start', () => {
      const buttons = tabButtons();
      buttons[0].focus();

      component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
      expect(document.activeElement).toBe(buttons[buttons.length - 1]);
    });

    it('moves focus to the first/last tab on Home/End', () => {
      const buttons = tabButtons();
      buttons[2].focus();

      component.onKeydown(new KeyboardEvent('keydown', { key: 'End' }));
      expect(document.activeElement).toBe(buttons[buttons.length - 1]);

      component.onKeydown(new KeyboardEvent('keydown', { key: 'Home' }));
      expect(document.activeElement).toBe(buttons[0]);
    });
  });

  describe('per-item remove button (#480)', () => {
    const removableItems: TabItem[] = [
      { id: 'tab-0', label: 'Short Press' },
      { id: 'tab-1', label: 'Long Press', removable: true },
    ];

    beforeEach(() => {
      (fixture.nativeElement as HTMLElement).style.removeProperty('width');
      fixture.componentRef.setInput('items', removableItems);
      fixture.componentRef.setInput('activeId', 'tab-0');
      fixture.detectChanges();
    });

    it('renders no wrapper and no "x" for an item that is not removable', () => {
      const plainTab = fixture.nativeElement.querySelectorAll('.tab-item')[0] as HTMLElement;
      expect(plainTab.closest('.tab-item-wrap')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.tab-remove-btn').length).toBe(1);
    });

    it('renders the "x" as a sibling of the tab button, not nested inside it', () => {
      const wrap = fixture.nativeElement.querySelector('.tab-item-wrap') as HTMLElement;
      expect(wrap).not.toBeNull();
      const tabButton = wrap.querySelector('.tab-item') as HTMLElement;
      const removeButton = wrap.querySelector('.tab-remove-btn') as HTMLElement;
      expect(tabButton.querySelector('button')).toBeNull();
      expect(wrap.children.length).toBe(2);
      expect(Array.from(wrap.children)).toEqual([tabButton, removeButton]);
    });

    it('clicking the "x" emits itemRemove without selecting the tab', () => {
      const activeIdChange = jasmine.createSpy('activeIdChange');
      const itemRemove = jasmine.createSpy('itemRemove');
      component.activeIdChange.subscribe(activeIdChange);
      component.itemRemove.subscribe(itemRemove);

      (fixture.nativeElement.querySelector('.tab-remove-btn') as HTMLButtonElement).click();

      expect(activeIdChange).not.toHaveBeenCalled();
      expect(itemRemove).toHaveBeenCalledOnceWith('tab-1');
    });

    it('labels the "x" with the tab it would remove', () => {
      const removeButton = fixture.nativeElement.querySelector('.tab-remove-btn') as HTMLButtonElement;
      expect(removeButton.getAttribute('aria-label')).toBe('Remove Long Press');
    });

    it('still walks only the tabs on arrow keys, not the "x" buttons', () => {
      const buttons = Array.from(fixture.nativeElement.querySelectorAll('.tab-item')) as HTMLButtonElement[];
      buttons[0].focus();

      component.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }));

      expect(document.activeElement).toBe(buttons[1]);
    });

    it('gives the "x" tabIndexFor its own tab, so it is Tab-reachable exactly when the tab is', () => {
      const removeButton = fixture.nativeElement.querySelector('.tab-remove-btn') as HTMLButtonElement;
      expect(removeButton.getAttribute('tabindex')).toBe('-1');

      fixture.componentRef.setInput('activeId', 'tab-1');
      fixture.detectChanges();

      expect(removeButton.getAttribute('tabindex')).toBe('0');
    });
  });
});
