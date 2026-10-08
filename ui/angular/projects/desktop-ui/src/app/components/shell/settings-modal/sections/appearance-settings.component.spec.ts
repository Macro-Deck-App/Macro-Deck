import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { SelectComponent } from '../../../forms/select/select.component';
import { FontService } from '../../../../services/font.service';
import { AppearanceSettingsComponent } from './appearance-settings.component';
import { FakeColorPalette, provideColorPaletteTesting } from '../../../../../testing/color-palette-test-support';

describe('AppearanceSettingsComponent', () => {
  let fixture: ComponentFixture<AppearanceSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let palette: FakeColorPalette;

  beforeEach(async () => {
    localStorage.clear();
    palette = new FakeColorPalette();

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getAppearanceSettings',
      'updateAppearanceSettings',
      'onNotification',
      'getSystemFonts',
      'getFontFileUrl',
    ], { connectionStateSignal: signal('disconnected') });
    api.onNotification.and.returnValue(EMPTY);
    api.updateAppearanceSettings.and.resolveTo({ themeMode: 'dark', accentColor: '#2196F3' });
    api.getSystemFonts.and.resolveTo({
      faces: [{
        faceId: 'inter-400', family: 'Inter', weight: 400, width: 5, slant: 'upright', styleName: 'Regular',
        remoteRenderable: true,
      }],
    });
    api.getFontFileUrl.and.callFake(faceId => `http://host/api/system/fonts/${faceId}/file`);

    await TestBed.configureTestingModule({
      imports: [AppearanceSettingsComponent],
      providers: [provideZonelessChangeDetection(), ...provideColorPaletteTesting(palette), { provide: ApiService, useValue: api }],
    }).compileComponents();

    fixture = TestBed.createComponent(AppearanceSettingsComponent);
    fixture.detectChanges();
  });

  // The component drives the real ThemeService, which applies appearance to the
  // shared document root. Left behind, it leaks into whatever specs run after
  // this one (issue #306).
  afterEach(() => {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.style.removeProperty('--color-accent');
    root.style.removeProperty('--color-accent-hover');
    root.style.removeProperty('--color-accent-muted');
    root.style.removeProperty('--font-sans');
    localStorage.clear();
  });

  it('renders all three theme-mode options', () => {
    const themeMode: HTMLElement = fixture.nativeElement.querySelector('shared-segmented-control:not(.cp-mode)');
    const buttons = themeMode.querySelectorAll('.seg-option');
    expect(buttons.length).toBe(3);
  });

  it('updates and highlights the theme mode when an option is clicked', () => {
    const buttons: HTMLButtonElement[] =
      Array.from(fixture.nativeElement.querySelectorAll('.seg-option'));
    const darkButton = buttons.find(b => b.textContent?.includes('Dark'))!;

    darkButton.click();
    fixture.detectChanges();

    expect(api.updateAppearanceSettings).toHaveBeenCalledWith(
      jasmine.objectContaining({ themeMode: 'dark' })
    );
    expect(darkButton.classList.contains('active')).toBeTrue();
  });

  it('renders the accent color picker', () => {
    expect(fixture.nativeElement.querySelector('shared-color-picker')).toBeTruthy();
  });

  it('offers the system default and every installed family as the global font', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    const select = fixture.debugElement.query(By.directive(SelectComponent)).componentInstance as SelectComponent;

    expect(select.options.map(option => option.value)).toEqual(['', 'Inter']);
  });

  it('does not offer an imported font as the global font', async () => {
    api.getSystemFonts.and.resolveTo({
      faces: [
        {
          faceId: 'inter-400', family: 'Inter', weight: 400, width: 5, slant: 'upright', styleName: 'Regular',
          remoteRenderable: true,
        },
        {
          faceId: 'brand-400', family: 'Brand Sans', weight: 400, width: 5, slant: 'upright', styleName: 'Regular',
          remoteRenderable: true, userImported: true,
        },
      ],
    });
    await TestBed.inject(FontService).loadSystemFonts();
    fixture.detectChanges();

    const select = fixture.debugElement.query(By.directive(SelectComponent)).componentInstance as SelectComponent;

    expect(select.options.map(option => option.value)).toEqual(['', 'Inter']);
  });

  it('saves the chosen global font to the host', async () => {
    await fixture.whenStable();
    const select = fixture.debugElement.query(By.directive(SelectComponent));

    select.triggerEventHandler('ngModelChange', 'Inter');

    expect(api.updateAppearanceSettings).toHaveBeenCalledWith(jasmine.objectContaining({ fontFamily: 'Inter' }));
  });

  it('offers theme, accent, color palette and font, with no interface-scale control', () => {
    const sections = fixture.nativeElement.querySelectorAll('shared-settings-section');

    expect(sections.length).toBe(4);
    expect(fixture.nativeElement.querySelector('shared-slider')).toBeNull();
    expect(fixture.nativeElement.querySelector('.slider-input')).toBeNull();
  });

  it('sends a picked accent with an explicit source, so a stored variable reference is replaced or kept', () => {
    const reference = '{{ vars.primary | color | color_darken: 20 }}';

    fixture.componentInstance.onAccentChange(reference);
    expect(api.updateAppearanceSettings).toHaveBeenCalledWith(jasmine.objectContaining({ accentColorSource: reference }));

    fixture.componentInstance.onAccentChange('#ff0000');
    expect(api.updateAppearanceSettings).toHaveBeenCalledWith(
      jasmine.objectContaining({ accentColor: '#ff0000', accentColorSource: '#ff0000' }));
  });

  describe('color palette', () => {
    function entries(): string[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.palette-hex'))
        .map(entry => (entry as HTMLElement).textContent!.trim());
    }

    function addTrigger(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('.palette-add-row shared-button button') as HTMLButtonElement;
    }

    function addButton(): HTMLButtonElement {
      return document.querySelector('.palette-add shared-button button') as HTMLButtonElement;
    }

    it('says so while the palette is empty', () => {
      expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeTruthy();
      expect(entries()).toEqual([]);
    });

    it('lists every custom color and removes one', () => {
      palette.colors.set(['#3ff4ee', '#ef444480']);
      fixture.detectChanges();

      expect(entries()).toEqual(['#3ff4ee', '#ef444480']);

      (fixture.nativeElement.querySelector('.palette-card shared-button button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(palette.removed).toEqual(['#3ff4ee']);
      expect(entries()).toEqual(['#ef444480']);
    });

    it('keeps the picker closed until the add button at the end of the list is used', () => {
      expect(document.querySelector('.palette-add')).toBeNull();

      addTrigger().click();
      fixture.detectChanges();

      expect(document.querySelector('.palette-add .cp-area')).toBeTruthy();
      expect(document.querySelector('.palette-add .cp-chip')).toBeNull();
    });

    it('adds the picked color once, closes the picker and does not offer the same color again', () => {
      fixture.componentInstance.paletteDraft.set('#123456');
      addTrigger().click();
      fixture.detectChanges();

      addButton().click();
      fixture.detectChanges();

      expect(palette.added).toEqual(['#123456']);
      expect(entries()).toEqual(['#123456']);
      expect(document.querySelector('.palette-add')).toBeNull();
      expect(document.activeElement).toBe(addTrigger());

      addTrigger().click();
      fixture.detectChanges();
      expect(addButton().disabled).toBeTrue();
    });

    it('lists default colours by name and lets them be removed like any other', () => {
      palette.colors.set(['#ef4444', '#3ff4ee']);
      fixture.detectChanges();

      const names = Array.from(fixture.nativeElement.querySelectorAll('.palette-name'))
        .map(entry => (entry as HTMLElement).textContent!.trim());
      expect(names).toEqual(['Red', 'Custom color #3ff4ee']);

      (fixture.nativeElement.querySelector('.palette-card shared-button button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(palette.removed).toEqual(['#ef4444']);
    });

    it('asks before restoring the default colours and does nothing when cancelled', async () => {
      const restore = () =>
        (fixture.nativeElement.querySelector('[settingsSectionAction] button') as HTMLButtonElement).click();
      const modal = () => document.querySelector('shared-confirmation-modal');
      const modalButton = (text: string) =>
        Array.from(modal()!.querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent!.trim() === text)!;
      const closed = async () => {
        for (let attempt = 0; attempt < 40 && modal(); attempt++) {
          await new Promise(resolve => setTimeout(resolve, 25));
          fixture.detectChanges();
        }
      };

      restore();
      fixture.detectChanges();
      expect(modal()).toBeTruthy();
      expect(palette.restoredDefaults).toBe(0);

      modalButton('Cancel').click();
      await closed();
      expect(modal()).toBeNull();
      expect(palette.restoredDefaults).toBe(0);

      restore();
      fixture.detectChanges();
      modalButton('Restore').click();
      await closed();
      expect(modal()).toBeNull();
      expect(palette.restoredDefaults).toBe(1);
    });
  });
});
