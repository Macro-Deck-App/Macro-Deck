import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { SelectComponent } from '../../../forms/select/select.component';
import { AppearanceSettingsComponent } from './appearance-settings.component';

describe('AppearanceSettingsComponent', () => {
  let fixture: ComponentFixture<AppearanceSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    localStorage.clear();

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
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
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
    const buttons = fixture.nativeElement.querySelectorAll('.seg-option');
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

  it('saves the chosen global font to the host', async () => {
    await fixture.whenStable();
    const select = fixture.debugElement.query(By.directive(SelectComponent));

    select.triggerEventHandler('ngModelChange', 'Inter');

    expect(api.updateAppearanceSettings).toHaveBeenCalledWith(jasmine.objectContaining({ fontFamily: 'Inter' }));
  });

  it('offers theme, accent and font, with no interface-scale control', () => {
    const sections = fixture.nativeElement.querySelectorAll('shared-settings-section');

    expect(sections.length).toBe(3);
    expect(fixture.nativeElement.querySelector('shared-slider')).toBeNull();
    expect(fixture.nativeElement.querySelector('.slider-input')).toBeNull();
  });
});
