import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TOOLTIP_SHOW_DELAY_MS } from '../../../overlay/tooltip/tooltip.directive';
import { NavItem } from '../../../../domain/navigation.interface';
import { NavigationService } from '../../../../services';
import { NotificationCenterService } from '../../../../services/notification-center.service';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';
import { NavItemComponent } from './nav-item.component';

const notificationCenterStub = {
  hasNotifications: signal(false),
  hasActiveProgress: signal(false),
  badgeText: signal('0'),
};

describe('NavItemComponent', () => {
  let fixture: ComponentFixture<NavItemComponent>;
  let component: NavItemComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NavItemComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        provideRouter([]),
        NavigationService,
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NavItemComponent);
    component = fixture.componentInstance;
  });

  function setItem(item: NavItem): void {
    fixture.componentRef.setInput('item', item);
    fixture.detectChanges();
  }

  function setCollapsed(collapsed: boolean): void {
    fixture.componentRef.setInput('isCollapsed', collapsed);
    fixture.detectChanges();
  }

  function tooltipEl(): HTMLElement | null {
    return fixture.nativeElement.querySelector('shared-tooltip');
  }

  it('renders an action item as a button and emits its action id on click', () => {
    setItem({ id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' });

    const button = fixture.nativeElement.querySelector('button.nav-item') as HTMLButtonElement;
    expect(button).toBeTruthy();
    expect(fixture.nativeElement.querySelector('a.nav-item')).toBeNull();

    const emitted: string[] = [];
    component.action.subscribe(id => emitted.push(id));
    button.click();

    expect(emitted).toEqual(['open-settings']);
  });

  it('reflects the active input on an action item', () => {
    setItem({ id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' });
    expect(fixture.nativeElement.querySelector('.nav-item')?.classList).not.toContain('active');

    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.nav-item')?.classList).toContain('active');
  });

  // Read off the real navigation rather than a literal list: the icon switch has no default branch,
  // so a nav entry naming an icon nobody added a case for renders an empty box (issue #845 added
  // the Library entry that way).
  it('renders a glyph for every icon the navigation actually uses', () => {
    const nav = TestBed.inject(NavigationService);
    const navIcons = [...nav.mainNavGroups().flat(), ...nav.bottomNavItems()]
      .map(entry => entry.icon)
      .filter((icon, index, all) => all.indexOf(icon) === index);

    expect(navIcons.length).toBeGreaterThan(0);

    for (const icon of navIcons) {
      setItem({ id: icon, label: icon, icon, route: `/${icon}` });

      const glyph = fixture.nativeElement.querySelector('.nav-icon .icon') as HTMLElement | null;
      expect(glyph).withContext(`no glyph rendered for icon "${icon}"`).toBeTruthy();
      expect(glyph!.classList).withContext(`wrong mask class for icon "${icon}"`).toContain(`icon-${icon}`);
    }
  });

  it('renders a disabled item as a non-interactive element', () => {
    setItem({ id: 'dev', label: 'Developer', icon: 'code', route: '/developer', disabled: true });

    expect(fixture.nativeElement.querySelector('button.nav-item')).toBeNull();
    expect(fixture.nativeElement.querySelector('.nav-item.disabled')).toBeTruthy();
  });

  it('sets no title attribute on a collapsed route item, action item or disabled item', () => {
    setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
    setCollapsed(true);
    expect(fixture.nativeElement.querySelector('.nav-item').hasAttribute('title')).toBeFalse();

    setItem({ id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' });
    expect(fixture.nativeElement.querySelector('.nav-item').hasAttribute('title')).toBeFalse();

    setItem({
      id: 'store', label: 'Store', icon: 'store', route: '/store', disabled: true, badge: 'Coming Soon',
    });
    expect(fixture.nativeElement.querySelector('.nav-item').hasAttribute('title')).toBeFalse();
  });

  // Deliberate consequence of the removal: expanded Store loses its hover hint too (the badge
  // next to the label stays visible instead).
  it('sets no title attribute on an expanded disabled item', () => {
    setItem({
      id: 'store', label: 'Store', icon: 'store', route: '/store', disabled: true, badge: 'Coming Soon',
    });

    expect(fixture.nativeElement.querySelector('.nav-item.disabled').hasAttribute('title')).toBeFalse();
  });

  it('shows the label in a tooltip when a collapsed route item is hovered', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('a.nav-item');
      el.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()?.textContent).toContain('Deck');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the label in a tooltip when a collapsed route item is focused', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('a.nav-item');
      el.dispatchEvent(new Event('focus'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()?.textContent).toContain('Deck');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the label in a tooltip when a collapsed action item is hovered', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('button.nav-item');
      el.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()?.textContent).toContain('Settings');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the label in a tooltip when a collapsed action item is focused', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('button.nav-item');
      el.dispatchEvent(new Event('focus'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      expect(tooltipEl()?.textContent).toContain('Settings');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the label plus the badge as secondary text for a collapsed disabled item', () => {
    jasmine.clock().install();
    try {
      setItem({
        id: 'store', label: 'Store', icon: 'store', route: '/store', disabled: true, badge: 'Coming Soon',
      });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('.nav-item.disabled');
      el.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();

      const tooltip = tooltipEl();
      expect(tooltip?.textContent).toContain('Store');
      expect(tooltip?.querySelector('.tt-secondary')?.textContent).toContain('Coming Soon');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows no tooltip on hover while expanded', () => {
    jasmine.clock().install();
    try {
      const items: NavItem[] = [
        { id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' },
        { id: 'settings', label: 'Settings', icon: 'settings', action: 'open-settings' },
        { id: 'store', label: 'Store', icon: 'store', route: '/store', disabled: true, badge: 'Coming Soon' },
      ];

      for (const item of items) {
        setItem(item);
        setCollapsed(false);

        const el = fixture.nativeElement.querySelector('.nav-item');
        el.dispatchEvent(new Event('mouseenter'));
        jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
        fixture.detectChanges();

        expect(tooltipEl()).withContext(`tooltip shown for item "${item.id}" while expanded`).toBeNull();

        el.dispatchEvent(new Event('mouseleave'));
        fixture.detectChanges();
      }
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('removes an open tooltip when the sidebar expands', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('a.nav-item');
      el.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();
      expect(tooltipEl()).toBeTruthy();

      fixture.componentRef.setInput('isCollapsed', false);
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('exposes the label as the accessible name of a collapsed route item and drops it again when expanded', () => {
    setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
    setCollapsed(true);

    const el = fixture.nativeElement.querySelector('a.nav-item');
    expect(el.getAttribute('aria-label')).toBe('Deck');

    setCollapsed(false);
    expect(el.getAttribute('aria-label')).toBeNull();
  });

  it('hides the tooltip when a collapsed route item is clicked', () => {
    jasmine.clock().install();
    try {
      setItem({ id: 'deck', label: 'Deck', icon: 'grid', route: '/deck' });
      setCollapsed(true);

      const el = fixture.nativeElement.querySelector('a.nav-item') as HTMLAnchorElement;
      el.dispatchEvent(new Event('mouseenter'));
      jasmine.clock().tick(TOOLTIP_SHOW_DELAY_MS);
      fixture.detectChanges();
      expect(tooltipEl()).toBeTruthy();

      el.click();
      fixture.detectChanges();

      expect(tooltipEl()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  // The notification count has to stay readable over the glyph it sits on: issue #897 had the bell
  // painted across the badge, because the glyph is a mask element and a mask creates a stacking
  // context. Hit-tested rather than asserted on the declaration, so it measures the real cascade.
  it('keeps the notification badge above the icon it overlaps', () => {
    setItem({
      id: 'notifications', label: 'Notifications', icon: 'bell', action: 'open-notifications',
      indicator: '9',
    });

    const badge = fixture.nativeElement.querySelector('.nav-indicator') as HTMLElement;
    const glyph = fixture.nativeElement.querySelector('.nav-icon .icon') as HTMLElement;
    // Earlier specs leave their own fixtures in the runner's page, so this one can sit below the
    // fold - and a hit test only answers for points inside the viewport.
    badge.scrollIntoView({ block: 'center' });

    const badgeRect = badge.getBoundingClientRect();
    const glyphRect = glyph.getBoundingClientRect();

    const left = Math.max(badgeRect.left, glyphRect.left);
    const right = Math.min(badgeRect.right, glyphRect.right);
    const top = Math.max(badgeRect.top, glyphRect.top);
    const bottom = Math.min(badgeRect.bottom, glyphRect.bottom);
    expect(right).toBeGreaterThan(left);
    expect(bottom).toBeGreaterThan(top);

    expect(document.elementFromPoint((left + right) / 2, (top + bottom) / 2)).toBe(badge);
  });
});
