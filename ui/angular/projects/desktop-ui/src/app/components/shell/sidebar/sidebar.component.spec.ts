import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NavItem } from '../../../domain/navigation.interface';
import { NotificationCenterService } from '../../../services/notification-center.service';

import { SidebarComponent } from './sidebar.component';
import { NavigationService } from '../../../services';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const notificationCenterStub = {
  hasNotifications: signal(false),
  hasActiveProgress: signal(false),
  badgeText: signal('0'),
};

describe('SidebarComponent', () => {
  let fixture: ComponentFixture<SidebarComponent>;

  function item(id: string): NavItem {
    return { id, label: id, icon: 'grid', route: `/${id}` };
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        provideRouter([]),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SidebarComponent);
  });

  function setGroups(groups: NavItem[][]): void {
    fixture.componentRef.setInput('mainItemGroups', groups);
    fixture.componentRef.setInput('bottomItems', []);
    fixture.detectChanges();
  }

  it('renders every item of every group', () => {
    setGroups([[item('deck')], [item('scripts'), item('automations')]]);

    const items = fixture.nativeElement.querySelectorAll('.sidebar-main app-nav-item');
    expect(items.length).toBe(3);
  });

  it('separates the groups with one divider between each pair, never a trailing one', () => {
    setGroups([[item('deck')], [item('scripts')], [item('store')]]);

    const dividers = fixture.nativeElement.querySelectorAll('.sidebar-main .sidebar-divider');
    expect(dividers.length).toBe(2);
  });

  it('renders no divider for a single group', () => {
    setGroups([[item('deck'), item('scripts')]]);

    expect(fixture.nativeElement.querySelectorAll('.sidebar-main .sidebar-divider').length).toBe(0);
  });
});

describe('NavigationService main groups', () => {
  let service: NavigationService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        NavigationService,
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    });
    service = TestBed.inject(NavigationService);
  });

  // The grouping is a deliberate layout, not an accident of ordering: the deck, the things that run,
  // the things they are built from, and the store.
  it('groups the main navigation as designed', () => {
    expect(service.mainNavGroups().map(group => group.map(entry => entry.id))).toEqual([
      ['deck'],
      ['scripts', 'automations'],
      ['variables', 'integrations', 'library'],
      ['store'],
    ]);
  });

  // Issue #845: the Library replaced the Icon Packs view. Scripts and automations are named in that
  // issue as future library content, but they stay top-level for now - moving them would relocate
  // navigation people already use.
  it('routes the library entry and keeps scripts and automations top-level', () => {
    const entries = service.mainNavGroups().flat();

    expect(entries.filter(entry => entry.id === 'library').map(entry => entry.route)).toEqual(['/library']);
    expect(entries.map(entry => entry.id)).not.toContain('icon-packs');
    expect(entries.map(entry => entry.route)).not.toContain('/icon-packs');
    expect(entries.find(entry => entry.id === 'scripts')?.route).toBe('/scripts');
    expect(entries.find(entry => entry.id === 'automations')?.route).toBe('/automations');
  });

  it('gives every main entry an icon', () => {
    for (const entry of service.mainNavGroups().flat()) {
      expect(entry.icon).withContext(`no icon on nav entry "${entry.id}"`).toBeTruthy();
    }
  });

  it('uses the local source revision as the Development version label', () => {
    service.setAppVersion('3.0.0', false, true, 'abc1234');

    expect(service.versionLabel()).toBe('dev/abc1234');

    service.setAppVersion('3.0.0-beta.42', true, false);
    expect(service.versionLabel()).toBe('3.0.0-beta.42');
  });
});

@Component({
  standalone: true,
  imports: [SidebarComponent],
  template: `
    <div class="host" [style.height.px]="height">
      <app-sidebar
        [isCollapsed]="collapsed"
        [mainItemGroups]="groups"
        [bottomItems]="bottom"></app-sidebar>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: ['.host { display: flex; }'],
})
class SidebarHostComponent {
  collapsed = false;
  height = 600;
  groups: NavItem[][] = [];
  bottom: NavItem[] = [];
}

describe('sidebar geometry', () => {
  const NAV_ITEM_HEIGHT = 44;
  let nav: NavigationService;

  beforeEach(() => {
    document.documentElement.style.removeProperty('font-size');
    document.documentElement.style.setProperty('--transition-slow', '0s');

    TestBed.configureTestingModule({
      imports: [SidebarHostComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        provideRouter([]),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    });
    nav = TestBed.inject(NavigationService);
  });

  afterEach(() => document.documentElement.style.removeProperty('--transition-slow'));

  function render(collapsed: boolean, height = 600): ComponentFixture<SidebarHostComponent> {
    const fixture = TestBed.createComponent(SidebarHostComponent);
    Object.assign(fixture.componentInstance, {
      collapsed,
      height,
      groups: nav.mainNavGroups(),
      bottom: nav.bottomNavItems(),
    });
    fixture.detectChanges();
    return fixture;
  }

  function itemsOf(fixture: ComponentFixture<SidebarHostComponent>): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.nav-item'));
  }

  function railOf(fixture: ComponentFixture<SidebarHostComponent>): DOMRect {
    return (fixture.nativeElement.querySelector('.sidebar') as HTMLElement).getBoundingClientRect();
  }

  function offsetsOf(fixture: ComponentFixture<SidebarHostComponent>): number[] {
    const rail = railOf(fixture);
    return itemsOf(fixture).map(item => Math.round((item.getBoundingClientRect().top - rail.top) * 100) / 100);
  }

  it('gives every item the shared row height in both modes', () => {
    for (const collapsed of [false, true]) {
      const fixture = render(collapsed);
      const items = itemsOf(fixture);

      expect(items.length).toBe(10);
      for (const item of items) {
        expect(item.getBoundingClientRect().height)
          .withContext(`"${item.textContent?.trim()}" collapsed: ${collapsed}`)
          .toBe(NAV_ITEM_HEIGHT);
      }

      fixture.destroy();
    }
  });

  it('gives collapsed items and dividers the same gutter as the rail', () => {
    const fixture = render(true);
    const rail = railOf(fixture);

    const boxes: HTMLElement[] = [
      ...itemsOf(fixture),
      ...Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.sidebar-divider')),
    ];

    for (const box of boxes) {
      const rect = box.getBoundingClientRect();

      expect(rect.left - rail.left).withContext('left gutter').toBeGreaterThan(0);
      expect(rail.right - rect.right).withContext('right gutter').toBeGreaterThan(0);
    }
  });

  // The jumping this replaces (issue #443) came from the items re-laying themselves out while the
  // sidebar width was still animating. Now nothing inside an item moves at all: the icon column is
  // as wide as the collapsed rail in both modes, so the icons are already where they end up.
  it('keeps every icon on the same horizontal position when the sidebar collapses', () => {
    const centers = (fixture: ComponentFixture<SidebarHostComponent>): number[] => {
      const rail = railOf(fixture);
      return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.nav-icon'))
        .map(icon => {
          const rect = icon.getBoundingClientRect();
          return Math.round((rect.left + rect.width / 2 - rail.left) * 100) / 100;
        });
    };

    const expanded = centers(render(false));
    const collapsedFixture = render(true);
    const collapsed = centers(collapsedFixture);
    const rail = railOf(collapsedFixture);

    expect(collapsed).toEqual(expanded);
    for (const center of collapsed) {
      expect(Math.abs(center - rail.width / 2)).withContext('icon off the rail centre').toBeLessThan(1);
    }
  });

  it('keeps every item at the same vertical position when the sidebar collapses', () => {
    const expanded = offsetsOf(render(false));
    const collapsed = offsetsOf(render(true));

    expect(collapsed).toEqual(expanded);
  });

  it('scrolls the navigation column in a short window instead of clipping items', () => {
    const fixture = render(false, 420);
    const main = fixture.nativeElement.querySelector('.sidebar-main') as HTMLElement;
    const items = itemsOf(fixture);

    expect(main.scrollHeight).toBeGreaterThan(main.clientHeight);
    for (const item of items) {
      expect(item.getBoundingClientRect().height).toBe(NAV_ITEM_HEIGHT);
    }
    expect(items[items.length - 1].getBoundingClientRect().bottom).toBeLessThanOrEqual(railOf(fixture).bottom);
  });
});
