import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { ApiService } from '@shared';
import { LanguageSettingsComponent } from './language-settings.component';

describe('LanguageSettingsComponent', () => {
  let fixture: ComponentFixture<LanguageSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    localStorage.clear();

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getLocalization',
      'updateLocalizationSettings',
      'onNotification',
    ]);
    api.getLocalization.and.resolveTo({
      culture: 'de-DE',
      fallbackCulture: 'en',
      translations: {},
      availableCultures: ['en', 'de-DE'],
      followSystem: false,
    });
    api.updateLocalizationSettings.and.resolveTo({
      success: true, error: null, culture: 'en', fallbackCulture: 'en', followSystem: false,
    });
    api.onNotification.and.callFake(<T>(): Observable<T> => new Subject<T>().asObservable());

    await TestBed.configureTestingModule({
      imports: [LanguageSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  async function settle(f: ComponentFixture<LanguageSettingsComponent>): Promise<void> {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  async function create(): Promise<ComponentFixture<LanguageSettingsComponent>> {
    const f = TestBed.createComponent(LanguageSettingsComponent);
    await settle(f);
    await settle(f);
    return f;
  }

  function trigger(f: ComponentFixture<LanguageSettingsComponent>): HTMLButtonElement {
    return f.nativeElement.querySelector('shared-select .control');
  }

  function optionLabels(f: ComponentFixture<LanguageSettingsComponent>): string[] {
    return Array.from(f.nativeElement.querySelectorAll('.sel-option')).map(option =>
      (option as HTMLElement).textContent!.trim()
    );
  }

  it('shows the culture the host reports as active', async () => {
    fixture = await create();

    expect(trigger(fixture).textContent).toContain('Deutsch');
  });

  it('lists every culture the host reports as switchable', async () => {
    fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();

    expect(optionLabels(fixture).some(label => label.includes('English'))).toBeTrue();
    expect(optionLabels(fixture).some(label => label.includes('Deutsch'))).toBeTrue();
  });

  it('calls the host to switch the active language rather than persisting the choice itself', async () => {
    fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();
    const englishOption = Array.from(fixture.nativeElement.querySelectorAll('.sel-option'))
      .find(option => (option as HTMLElement).textContent!.includes('English')) as HTMLButtonElement;
    englishOption.click();
    await fixture.whenStable();

    expect(api.updateLocalizationSettings).toHaveBeenCalledWith({ culture: 'en' });
  });

  it('settles on the culture the host confirmed, not merely the one requested', async () => {
    fixture = await create();
    api.getLocalization.and.resolveTo({
      culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en', 'de-DE'],
      followSystem: false,
    });

    await fixture.componentInstance.selectCulture('en');
    await settle(fixture);

    expect(api.getLocalization).toHaveBeenCalled();
    expect(trigger(fixture).textContent).toContain('English');
  });

  it('shows System, not the culture it resolves to, while no language has been chosen', async () => {
    api.getLocalization.and.resolveTo({
      culture: 'de-DE', fallbackCulture: 'en', translations: {}, availableCultures: ['en', 'de-DE'],
      followSystem: true,
    });

    fixture = await create();

    // The distinction the entry exists for: the reader is on German because their system is, and has
    // to be able to see that and leave it that way, not be shown a German they never picked.
    expect(trigger(fixture).textContent).toContain('System');
    expect(trigger(fixture).textContent).not.toContain('Deutsch');
  });

  it('clears the chosen language rather than sending "system" as one', async () => {
    fixture = await create();

    await fixture.componentInstance.selectCulture('system');

    expect(api.updateLocalizationSettings).toHaveBeenCalledWith({ followSystem: true });
  });

  it('changes only the time format, leaving the language as it is', async () => {
    fixture = await create();

    await fixture.componentInstance.selectTimeFormat('12h');

    expect(api.updateLocalizationSettings).toHaveBeenCalledOnceWith({ timeFormat: '12h' });
  });
});
