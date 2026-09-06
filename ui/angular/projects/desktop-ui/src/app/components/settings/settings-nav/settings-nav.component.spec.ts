import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NavItem, NavItemGroup } from '../../../domain/navigation.interface';
import { SettingsNavComponent } from './settings-nav.component';

describe('SettingsNavComponent', () => {
  let fixture: ComponentFixture<SettingsNavComponent>;
  let component: SettingsNavComponent;

  const items: NavItem[] = [
    { id: 'appearance', label: 'Appearance', icon: 'sun' },
    { id: 'security', label: 'Security', icon: 'lock' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsNavComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsNavComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
  });

  function buttons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.settings-nav__item'));
  }

  it('renders one button per item', () => {
    expect(buttons().length).toBe(2);
    expect(buttons()[0].textContent).toContain('Appearance');
  });

  it('highlights the item matching activeId', () => {
    fixture.componentRef.setInput('activeId', 'security');
    fixture.detectChanges();

    expect(buttons()[0].classList).not.toContain('active');
    expect(buttons()[1].classList).toContain('active');
    expect(buttons()[1].getAttribute('aria-current')).toBe('true');
  });

  it('emits select with the item id on click', () => {
    const selected: string[] = [];
    component.select.subscribe(id => selected.push(id));

    buttons()[1].click();

    expect(selected).toEqual(['security']);
  });

  it('keeps every item reachable and named when collapsed to an icon column', () => {
    fixture.componentRef.setInput('collapsed', true);
    fixture.detectChanges();

    expect(buttons().length).toBe(2);
    expect(buttons().map(button => button.getAttribute('aria-label'))).toEqual(['Appearance', 'Security']);
    // The label fades out rather than being removed, so the rail's width animation has something
    // to shrink; the accessible name above is what still identifies the item.
    expect(getComputedStyle(buttons()[0].querySelector('.settings-nav__label') as Element).opacity).toBe('0');
  });

  describe('grouped items', () => {
    const groups: NavItemGroup[] = [
      { label: 'General', items: [{ id: 'appearance', label: 'Appearance', icon: 'sun' }] },
      {
        label: 'Advanced',
        items: [
          { id: 'logging', label: 'Logging', icon: 'file-text' },
          { id: 'developer', label: 'Developer', icon: 'code' },
        ],
      },
      { items: [{ id: 'about', label: 'About', icon: 'info' }] },
    ];

    beforeEach(() => {
      fixture.componentRef.setInput('groups', groups);
      fixture.detectChanges();
    });

    function groupElements(): HTMLElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.settings-nav__group'));
    }

    it('renders each group with its label and its own items, in order', () => {
      expect(groupElements().length).toBe(3);
      expect(groupElements().map(group => group.querySelector('.settings-nav__group-label')?.textContent?.trim()))
        .toEqual(['General', 'Advanced', undefined]);
      expect(buttons().map(button => button.textContent?.trim()))
        .toEqual(['Appearance', 'Logging', 'Developer', 'About']);
    });

    it('makes group labels a heading rather than a selectable entry', () => {
      const selected: string[] = [];
      component.select.subscribe(id => selected.push(id));

      const label = fixture.nativeElement.querySelector('.settings-nav__group-label') as HTMLElement;
      expect(label.tagName).toBe('H3');
      expect(label.closest('button')).toBeNull();

      label.click();

      expect(selected).toEqual([]);
    });

    it('still highlights and emits for an item inside a group', () => {
      fixture.componentRef.setInput('activeId', 'developer');
      fixture.detectChanges();
      const selected: string[] = [];
      component.select.subscribe(id => selected.push(id));

      const developer = buttons()[2];
      expect(developer.classList).toContain('active');
      expect(developer.getAttribute('aria-current')).toBe('true');

      developer.click();

      expect(selected).toEqual(['developer']);
    });

    it('sets a trailing unlabelled group apart from the labelled ones', () => {
      const [general, advanced, trailing] = groupElements();

      expect(general.classList).not.toContain('settings-nav__group--divided');
      expect(advanced.classList).not.toContain('settings-nav__group--divided');
      expect(trailing.classList).toContain('settings-nav__group--divided');
    });

    it('drops the group headings when collapsed but keeps the groups separated', () => {
      fixture.componentRef.setInput('collapsed', true);
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('.settings-nav__group-label') as HTMLElement;
      expect(getComputedStyle(label).height).toBe('0px');
      expect(getComputedStyle(label).opacity).toBe('0');
      expect(buttons().length).toBe(4);

      // With the headings gone, a rule is all that still marks where one group ends. The design
      // token is not loaded in the harness, and an unresolved var() invalidates the whole shorthand.
      (fixture.nativeElement as HTMLElement).style.setProperty('--color-border', '#333');
      const [, advanced] = groupElements();
      expect(getComputedStyle(advanced).borderTopStyle).toBe('solid');
    });
  });
});
