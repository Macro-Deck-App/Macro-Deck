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
  function createFixture(profiles: Profile[] = [profile()]): ComponentFixture<ProfileSelectorComponent> {
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
          },
        },
        { provide: PortabilityService, useValue: {} },
        { provide: ToastService, useValue: { show: jasmine.createSpy('show') } },
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

  it('gives every row its own edit, export and delete action, named after the profile', () => {
    const row: HTMLElement = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })])
      .nativeElement.querySelectorAll('.profile-list-item')[1];

    const labels = Array.from(row.querySelectorAll('.profile-item-actions button'))
      .map(button => button.getAttribute('aria-label'));
    expect(labels).toEqual(['Edit Stream', 'Export Stream', 'Delete Stream']);
    expect(row.querySelector('.profile-item-actions .icon-upload')).not.toBeNull();
  });

  it('acts on the row, not on the selection', () => {
    const fixture = createFixture([profile({ id: 'p1', name: 'Home' }), profile({ id: 'p2', name: 'Stream' })]);
    const row: HTMLElement = fixture.nativeElement.querySelectorAll('.profile-list-item')[1];

    row.querySelector<HTMLButtonElement>('button[aria-label="Edit Stream"]')?.click();

    expect(editState(fixture.componentInstance).isEditing()).toBeTrue();
    expect(fixture.componentInstance['editName']()).toBe('Stream');
  });

  it('disables edit and export on an integration profile and delete on the last user profile', () => {
    const rows: NodeListOf<HTMLElement> = createFixture([profile(), profile({ id: 'p2', isVirtual: true })])
      .nativeElement.querySelectorAll('.profile-list-item');

    const disabled = (row: HTMLElement, label: string) =>
      row.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)?.disabled;
    expect(disabled(rows[1], 'Edit Home')).toBeTrue();
    expect(disabled(rows[1], 'Export Home')).toBeTrue();
    expect(disabled(rows[0], 'Delete Home')).toBeTrue();
  });
});
