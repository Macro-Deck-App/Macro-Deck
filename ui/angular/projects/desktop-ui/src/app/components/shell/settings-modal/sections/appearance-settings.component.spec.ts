import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
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
    ], { connectionStateSignal: signal('disconnected') });
    api.onNotification.and.returnValue(EMPTY);
    api.updateAppearanceSettings.and.resolveTo({ themeMode: 'dark', accentColor: '#2196F3' });

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

  it('offers appearance only, with no interface-scale control', () => {
    const sections = fixture.nativeElement.querySelectorAll('shared-settings-section');

    expect(sections.length).toBe(2);
    expect(fixture.nativeElement.querySelector('shared-slider')).toBeNull();
    expect(fixture.nativeElement.querySelector('.slider-input')).toBeNull();
  });
});
