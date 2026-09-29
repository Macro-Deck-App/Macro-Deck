import { provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Profile } from '@macro-deck/runtime';
import { ProfileService, ToastService } from '@shared';
import { PortabilityService } from '../../../services/portability.service';
import { FileOpenService } from '../../../services';
import { ProfileSelectorComponent } from './profile-selector.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function profile(overrides: Partial<Profile> = {}): Profile {
  return {
    id: 'p1',
    name: 'Home',
    order: 0,
    layoutType: 'Grid',
    isVirtual: false,
    layout: { rows: 3, columns: 5, rowsLocked: false, columnsLocked: false },
    defaultRows: 3,
    defaultColumns: 5,
    defaultBackground: null,
    defaultSpacing: null,
    defaultBorderRadius: null,
    ...overrides,
  };
}

interface EditState {
  editRows(): number;
  editColumns(): number;
  editSpacing(): number | null;
  editBorderRadius(): number | null;
  editMaxRows(): number;
  editMaxColumns(): number;
  isEditing(): boolean;
  editError(): string | null;
}

function editState(component: ProfileSelectorComponent): EditState {
  return component as unknown as EditState;
}

describe('ProfileSelectorComponent editing', () => {
  let profileServiceStub: {
    selectedProfile: jasmine.Spy;
    updateProfile: jasmine.Spy;
  };

  function createComponent(): ProfileSelectorComponent {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ProfileService, useValue: profileServiceStub },
        { provide: PortabilityService, useValue: {} },
        { provide: ToastService, useValue: { show: jasmine.createSpy('show') } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
      ],
    });
    return TestBed.createComponent(ProfileSelectorComponent).componentInstance;
  }

  beforeEach(() => {
    profileServiceStub = {
      selectedProfile: jasmine.createSpy('selectedProfile').and.returnValue(profile()),
      updateProfile: jasmine.createSpy('updateProfile').and.resolveTo({ success: true }),
    };
  });

  it('seeds the edit signals from the edited profile, including the two new defaults', () => {
    const component = createComponent();

    component.startEdit(profile({ defaultRows: 6, defaultColumns: 8, defaultSpacing: 20, defaultBorderRadius: 30 }));

    const state = editState(component);
    expect(state.editRows()).toBe(6);
    expect(state.editColumns()).toBe(8);
    expect(state.editSpacing()).toBe(20);
    expect(state.editBorderRadius()).toBe(30);
  });

  it('lets the default grid grow to 16 rows and 16 columns when no device constrains the profile', () => {
    const component = createComponent();

    component.startEdit(profile());

    const state = editState(component);
    expect(state.editMaxRows()).toBe(16);
    expect(state.editMaxColumns()).toBe(16);
  });

  it('sends -1 for a fully inherited (null) spacing/radius default on save', async () => {
    const component = createComponent();
    component.startEdit(profile());

    await component.confirmEdit();

    expect(profileServiceStub.updateProfile).toHaveBeenCalledWith('p1', jasmine.objectContaining({
      defaultWidgetSpacing: -1,
      defaultWidgetBorderRadius: -1,
    }));
  });

  it('sends the concrete value when spacing/radius were overridden', async () => {
    const component = createComponent();
    component.startEdit(profile({ defaultSpacing: 18, defaultBorderRadius: 24 }));

    await component.confirmEdit();

    expect(profileServiceStub.updateProfile).toHaveBeenCalledWith('p1', jasmine.objectContaining({
      defaultWidgetSpacing: 18,
      defaultWidgetBorderRadius: 24,
    }));
  });

  it('closes the modal and clears the edit state on success', async () => {
    const component = createComponent();
    component.startEdit(profile());

    await component.confirmEdit();

    const state = editState(component);
    expect(state.isEditing()).toBeFalse();
    expect(state.editError()).toBeNull();
  });

  it('keeps the modal open and shows the host message when the grid shrink is rejected', async () => {
    profileServiceStub.updateProfile.and.resolveTo({
      success: false,
      error: {
        code: 'GridTooSmall',
        message: 'Home needs at least 5 x 3 to keep every widget (including pinned ones) inside',
      },
    });
    const component = createComponent();
    component.startEdit(profile());

    await component.confirmEdit();

    const state = editState(component);
    expect(state.isEditing()).toBeTrue();
    expect(state.editError()).toContain('Home needs at least 5 x 3');
  });

  it('cancelEdit closes the modal and clears a pending error', async () => {
    profileServiceStub.updateProfile.and.resolveTo({
      success: false,
      error: { code: 'GridTooSmall', message: 'rejected' },
    });
    const component = createComponent();
    component.startEdit(profile());
    await component.confirmEdit();
    expect(editState(component).editError()).toBe('rejected');

    component.cancelEdit();

    const state = editState(component);
    expect(state.isEditing()).toBeFalse();
    expect(state.editError()).toBeNull();
  });
});

describe('ProfileSelectorComponent dropdown', () => {
  let duplicateProfile: jasmine.Spy;
  let selectProfile: jasmine.Spy;
  let toastShow: jasmine.Spy;

  function createFixture(profiles: Profile[] = [profile()]): ComponentFixture<ProfileSelectorComponent> {
    duplicateProfile = jasmine.createSpy('duplicateProfile').and.resolveTo({ success: true });
    selectProfile = jasmine.createSpy('selectProfile');
    toastShow = jasmine.createSpy('show');
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: ProfileService,
          useValue: {
            selectedProfile: () => profiles[0],
            selectedProfileId: () => profiles[0].id,
            sortedProfiles: () => profiles,
            profiles: () => profiles,
            isCurrentProfileLocked: () => false,
            duplicateProfile,
            selectProfile,
          },
        },
        { provide: PortabilityService, useValue: {} },
        { provide: ToastService, useValue: { show: toastShow } },
        { provide: FileOpenService, useValue: { pending: signal([]), claim: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(ProfileSelectorComponent);
    (fixture.componentInstance as unknown as { dropdownOpen: WritableSignal<boolean> }).dropdownOpen.set(true);
    fixture.detectChanges();
    return fixture;
  }

  it('pairs a wide create button with a labelled icon-only import button', () => {
    const footer: HTMLElement = createFixture().nativeElement.querySelector('.profile-list-footer');

    const buttons = footer.querySelectorAll('shared-button');
    expect(buttons.length).toBe(2);
    expect(buttons[0].textContent).toContain('New Profile');
    expect(buttons[1].textContent?.trim()).toBe('');
    expect(buttons[1].querySelector('button')?.getAttribute('aria-label'))
      .toBe('Import profile from a .macroDeckProfile file');
  });

  it('marks the footer import with the download icon', () => {
    const host: HTMLElement = createFixture().nativeElement;

    expect(host.querySelector('.profile-list-footer .icon-download')).not.toBeNull();
    expect(host.querySelector('.profile-list-footer .icon-upload')).toBeNull();
  });

  const rows = (fixture: ComponentFixture<ProfileSelectorComponent>): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('.profile-list-item'));

  const menuItems = (): HTMLButtonElement[] => Array.from(document.querySelectorAll('.menu-item'));

  function openRowMenu(fixture: ComponentFixture<ProfileSelectorComponent>, name: string): void {
    fixture.nativeElement.querySelector(`button[aria-label="More actions for ${name}"]`).click();
    fixture.detectChanges();
  }

  function clickMenuItem(fixture: ComponentFixture<ProfileSelectorComponent>, label: string): void {
    menuItems().find(item => item.textContent?.trim() === label)!.click();
    fixture.detectChanges();
  }

  it('shows each profile with its grid and one actions button instead of a row of icons', () => {
    const [, row] = rows(createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]));

    expect(row.querySelector('.profile-list-name')?.textContent?.trim()).toBe('Stream');
    expect(row.querySelector('.profile-list-subtitle')?.textContent?.trim()).toBe('5 × 3 Grid');
    expect(Array.from(row.querySelectorAll('button')).map(button => button.getAttribute('aria-label')))
      .toEqual([null, 'More actions for Stream']);
  });

  it('marks the selected profile', () => {
    const [selected, other] = rows(createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]));

    expect(selected.querySelector('.profile-list-select')?.getAttribute('aria-current')).toBe('true');
    expect(selected.querySelector('.profile-list-check')).not.toBeNull();
    expect(other.querySelector('.profile-list-select')?.getAttribute('aria-current')).toBeNull();
    expect(other.querySelector('.profile-list-check')).toBeNull();
  });

  it('offers edit, duplicate, export and delete in the row menu', () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);

    openRowMenu(fixture, 'Stream');

    expect(menuItems().map(item => item.textContent?.trim())).toEqual(['Edit', 'Duplicate', 'Export', 'Delete']);
  });

  it('acts on the row, not on the selection', () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);

    openRowMenu(fixture, 'Stream');
    clickMenuItem(fixture, 'Edit');

    expect(editState(fixture.componentInstance).isEditing()).toBeTrue();
    expect(fixture.componentInstance['editName']()).toBe('Stream');
  });

  it('duplicates the row it belongs to under a copy name', async () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);

    openRowMenu(fixture, 'Stream');
    clickMenuItem(fixture, 'Duplicate');
    await fixture.whenStable();

    expect(duplicateProfile).toHaveBeenCalledOnceWith('p2', 'Stream (copy)');
    expect(toastShow).not.toHaveBeenCalled();
  });

  it('reports a failed duplicate without showing the host message', async () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' })]);
    duplicateProfile.and.resolveTo({ success: false, error: { code: 'NotFound', message: 'Profile not found' } });

    openRowMenu(fixture, 'Home');
    clickMenuItem(fixture, 'Duplicate');
    await fixture.whenStable();

    expect(toastShow).toHaveBeenCalledOnceWith('Failed to duplicate profile', jasmine.objectContaining({ variant: 'error' }));
  });

  it('disables edit, duplicate and export on an integration profile and delete on the last user profile', () => {
    const fixture = createFixture([profile(), profile({ id: 'p2', name: 'Board', isVirtual: true })]);
    const disabled = () => menuItems().filter(item => item.disabled).map(item => item.textContent?.trim());

    openRowMenu(fixture, 'Board');
    expect(disabled()).toEqual(['Edit', 'Duplicate', 'Export', 'Delete']);

    fixture.componentInstance['rowMenu'].set(null);
    fixture.detectChanges();
    openRowMenu(fixture, 'Home');
    expect(disabled()).toEqual(['Delete']);
  });

  it('filters the list by name and says when nothing matches', () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);

    fixture.componentInstance['search'].set('str');
    fixture.detectChanges();
    expect(rows(fixture).map(row => row.querySelector('.profile-list-name')?.textContent?.trim())).toEqual(['Stream']);

    fixture.componentInstance['search'].set('zzz');
    fixture.detectChanges();
    expect(rows(fixture).length).toBe(0);
    expect(fixture.nativeElement.querySelector('.profile-list-empty')?.textContent?.trim()).toBe('No profiles match your search');
  });

  it('opens the first match on enter and forgets the search when closing', () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);
    fixture.componentInstance['search'].set('str');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.profile-search input')
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(selectProfile).toHaveBeenCalledOnceWith('p2');
    expect(fixture.componentInstance['search']()).toBe('');
  });
});
