import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { IconAppearanceModel, IconModel, IconPackModel, IconPackService } from '../../../services/icon-pack.service';
import { IconAppearancesDialogComponent } from './icon-appearances-dialog.component';

describe('IconAppearancesDialogComponent', () => {
  let fixture: ComponentFixture<IconAppearancesDialogComponent>;
  let iconPacks: jasmine.SpyObj<IconPackService>;
  let toasts: ToastService;

  const dark: IconAppearanceModel = {
    id: 'asset-dark',
    key: 'colorScheme=dark',
    traits: { colorScheme: 'dark' },
    contentHash: 'sha256:dark',
    isAnimated: false,
    width: 256,
    height: 256,
    processingState: 'Ready',
    processingError: null,
  };

  function icon(id: string, overrides: Partial<IconModel> = {}): IconModel {
    return {
      id,
      packId: 'pack',
      name: id,
      isAnimated: false,
      processingState: 'Ready',
      availableSizes: [128],
      contentHash: `sha256:${id}`,
      appearances: [],
      ...overrides,
    };
  }

  function pack(overrides: Partial<IconPackModel> = {}): IconPackModel {
    return {
      id: 'pack',
      name: 'Pack',
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 3,
      ownerKind: 'User',
      canDelete: true,
      ...overrides,
    };
  }

  const target = icon('logo', { appearances: [dark] });
  const library = [target, icon('logo-static'), icon('other', { appearances: [{ ...dark, id: 'asset-other' }] })];

  function render(packOverrides: Partial<IconPackModel> = {}): HTMLElement {
    fixture.componentRef.setInput('icon', target);
    fixture.componentRef.setInput('pack', pack(packOverrides));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function menuLabels(): string[] {
    return Array.from(document.querySelectorAll('button.menu-item')).map(item => item.textContent?.trim() ?? '');
  }

  function menuItem(label: string): HTMLButtonElement {
    return Array.from(document.querySelectorAll<HTMLButtonElement>('button.menu-item'))
      .find(item => item.textContent?.trim() === label)!;
  }

  function translate(key: string): string {
    return TestBed.inject(LocalizationService).translateKey(key);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => requestAnimationFrame(resolve));
    fixture.detectChanges();
  }

  function button(root: ParentNode, selector: string): HTMLButtonElement {
    return root.querySelector(`${selector} button`) as HTMLButtonElement;
  }

  async function pickFile(root: HTMLElement, file: File): Promise<void> {
    const input = root.querySelector('input[type=file]') as HTMLInputElement;
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  beforeEach(() => {
    iconPacks = jasmine.createSpyObj<IconPackService>('IconPackService', [
      'iconsFor', 'addAppearance', 'removeAppearance', 'mergeAppearance',
    ]);
    iconPacks.iconsFor.and.returnValue(signal(library).asReadonly());
    iconPacks.addAppearance.and.resolveTo(true);
    iconPacks.removeAppearance.and.resolveTo(true);
    iconPacks.mergeAppearance.and.resolveTo(true);

    const api = jasmine.createSpyObj<ApiService>('ApiService', ['getIconImageUrl', 'onNotification']);
    api.getIconImageUrl.and.callFake((iconId: string) => `http://host/api/icons/${iconId}/image`);
    api.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [IconAppearancesDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: IconPackService, useValue: iconPacks },
      ],
    });
    toasts = TestBed.inject(ToastService);
    spyOn(HTMLInputElement.prototype, 'click');
    fixture = TestBed.createComponent(IconAppearancesDialogComponent);
  });

  it('lists the default image and every appearance with its own preview', () => {
    const root = render();

    const rows = Array.from(root.querySelectorAll('.row')).map(row => row.getAttribute('data-appearance'));
    expect(rows).toEqual(['default', 'colorScheme=dark']);
    const preview = root.querySelector('[data-appearance="colorScheme=dark"] img') as HTMLImageElement;
    expect(preview.getAttribute('src')).toBe('http://host/api/icons/asset-dark/image');
  });

  it('offers only the known kinds the icon does not have yet, and adds the picked file as that kind', async () => {
    const root = render();
    const file = new File(['png'], 'static.png');

    button(root, '.add').click();
    fixture.detectChanges();
    expect(menuLabels()).not.toContain(translate(AppStrings.IconPacks.Appearances.Kind.Dark));
    expect(menuLabels().length).toBe(8);
    expect(menuLabels()).toContain(translate(AppStrings.IconPacks.Appearances.AddCustom));
    menuItem(translate(AppStrings.IconPacks.Appearances.Kind.Static)).click();
    await pickFile(root, file);

    expect(iconPacks.addAppearance).toHaveBeenCalledWith('logo', 'motion=static', file);
  });

  function typeName(name: string): void {
    const input = document.querySelector('shared-input input') as HTMLInputElement;
    input.value = name;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('adds a custom appearance under the name the user types, showing how it will read', async () => {
    const root = render();
    const file = new File(['png'], 'outlined.png');

    button(root, '.add').click();
    fixture.detectChanges();
    menuItem(translate(AppStrings.IconPacks.Appearances.AddCustom)).click();
    await settle();
    typeName('Duo-tone');
    expect(document.querySelector('.preview')?.textContent).toContain('Duo tone');
    button(document, '.confirm-name').click();
    await pickFile(root, file);

    expect(iconPacks.addAppearance).toHaveBeenCalledWith('logo', 'variant=duoTone', file);
  });

  it('refuses a custom name that cannot become a key', async () => {
    const root = render();

    button(root, '.add').click();
    fixture.detectChanges();
    menuItem(translate(AppStrings.IconPacks.Appearances.AddCustom)).click();
    await settle();
    typeName('填充');

    expect(document.querySelector('.error')).not.toBeNull();
    expect(button(document, '.confirm-name').disabled).toBeTrue();
  });

  it('lists a custom appearance by its name and lets it be replaced like any other', async () => {
    const outlined: IconAppearanceModel = { ...dark, id: 'asset-outlined', key: 'variant=outlined', traits: { variant: 'outlined' } };
    fixture.componentRef.setInput('icon', icon('logo', { appearances: [dark, outlined] }));
    fixture.componentRef.setInput('pack', pack());
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const file = new File(['png'], 'outlined-v2.png');

    expect(root.querySelector('[data-appearance="variant=outlined"] .label')?.textContent?.trim()).toBe('Outlined');
    button(root.querySelector('[data-appearance="variant=outlined"]')!, '.replace').click();
    await pickFile(root, file);

    expect(iconPacks.addAppearance).toHaveBeenCalledWith('logo', 'variant=outlined', file);
  });

  it('replaces an appearance by uploading a new file under the same kind', async () => {
    const root = render();
    const file = new File(['png'], 'dark-v2.png');

    button(root.querySelector('[data-appearance="colorScheme=dark"]')!, '.replace').click();
    await pickFile(root, file);

    expect(iconPacks.addAppearance).toHaveBeenCalledWith('logo', 'colorScheme=dark', file);
  });

  it('removes an appearance only after the removal is confirmed', async () => {
    const root = render();

    button(root.querySelector('[data-appearance="colorScheme=dark"]')!, '.remove').click();
    fixture.detectChanges();
    expect(iconPacks.removeAppearance).not.toHaveBeenCalled();

    fixture.debugElement.query(By.directive(ConfirmationModalComponent))
      .componentInstance.confirm.emit();
    await fixture.whenStable();

    expect(iconPacks.removeAppearance).toHaveBeenCalledWith('logo', 'asset-dark');
  });

  it('merges another icon of the pack as the chosen kind, never the icon itself or one with appearances', async () => {
    const root = render();

    button(root, '.merge').click();
    await settle();
    const tiles = Array.from(document.querySelectorAll<HTMLElement>('.merge-grid .tile'));
    expect(tiles.map(tile => tile.querySelector('.name')?.textContent?.trim())).toEqual(['logo-static']);

    tiles[0].click();
    fixture.detectChanges();
    button(document, '.confirm-merge').click();
    await fixture.whenStable();

    expect(iconPacks.mergeAppearance).toHaveBeenCalledWith('logo', 'logo-static', 'colorScheme=light');
  });

  it('merges another icon as a custom appearance under the typed name', async () => {
    const root = render();

    button(root, '.merge').click();
    await settle();
    const select = fixture.debugElement.query(By.css('.merge-form shared-select'));
    select.triggerEventHandler('ngModelChange', 'custom');
    await settle();
    typeName('Red');
    document.querySelectorAll<HTMLElement>('.merge-grid .tile')[0].click();
    fixture.detectChanges();
    button(document, '.confirm-merge').click();
    await fixture.whenStable();

    expect(iconPacks.mergeAppearance).toHaveBeenCalledWith('logo', 'logo-static', 'variant=red');
  });

  it('tells the user when the host refuses a change', async () => {
    iconPacks.removeAppearance.and.resolveTo(false);
    const show = spyOn(toasts, 'show');
    const root = render();

    button(root.querySelector('[data-appearance="colorScheme=dark"]')!, '.remove').click();
    fixture.detectChanges();
    fixture.debugElement.query(By.directive(ConfirmationModalComponent))
      .componentInstance.confirm.emit();
    await fixture.whenStable();

    expect(show).toHaveBeenCalledWith(jasmine.any(String), jasmine.objectContaining({ variant: 'error' }));
  });

  it('only lists the appearances of a read-only pack', () => {
    const root = render({ isReadOnly: true });

    expect(root.querySelector('.read-only')).not.toBeNull();
    expect(root.querySelectorAll('.row').length).toBe(2);
    expect(root.querySelector('.replace, .remove, .add, .merge')).toBeNull();
  });
});
