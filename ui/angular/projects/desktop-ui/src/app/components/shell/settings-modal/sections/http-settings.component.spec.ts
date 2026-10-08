import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { HttpSettingsComponent } from './http-settings.component';
import { EMPTY } from 'rxjs';

const DEFAULT_AGENT = 'MacroDeck/3.0.0-beta.15';

describe('HttpSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getHttpSettings',
      'updateHttpSettings',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.getHttpSettings.and.resolveTo({
      customUserAgent: null,
      effectiveUserAgent: DEFAULT_AGENT,
      defaultUserAgent: DEFAULT_AGENT,
    });

    await TestBed.configureTestingModule({
      imports: [HttpSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function settle(f: ComponentFixture<HttpSettingsComponent>): Promise<void> {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  async function create(): Promise<ComponentFixture<HttpSettingsComponent>> {
    const f = TestBed.createComponent(HttpSettingsComponent);
    await settle(f);
    await settle(f);
    return f;
  }

  function input(f: ComponentFixture<HttpSettingsComponent>): HTMLInputElement {
    return f.nativeElement.querySelector('shared-input input');
  }

  function buttons(f: ComponentFixture<HttpSettingsComponent>): HTMLButtonElement[] {
    return Array.from(f.nativeElement.querySelectorAll('shared-button button'));
  }

  async function type(f: ComponentFixture<HttpSettingsComponent>, value: string): Promise<void> {
    const element = input(f);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle(f);
  }

  it('shows the effective User-Agent and the default hint', async () => {
    const f = await create();

    expect(input(f).value).toBe(DEFAULT_AGENT);
    expect(f.nativeElement.querySelector('.http__hint').textContent).toContain(DEFAULT_AGENT);
  });

  it('shows a stored custom value', async () => {
    api.getHttpSettings.and.resolveTo({
      customUserAgent: 'Custom/2.0',
      effectiveUserAgent: 'Custom/2.0',
      defaultUserAgent: DEFAULT_AGENT,
    });

    const f = await create();

    expect(input(f).value).toBe('Custom/2.0');
  });

  it('offers neither Save nor Restore while nothing differs from the default', async () => {
    const f = await create();
    const [save, restore] = buttons(f);

    expect(save.disabled).toBeTrue();
    expect(restore.disabled).toBeTrue();
  });

  it('sends the trimmed custom value on Save and then allows restoring the default', async () => {
    api.updateHttpSettings.and.resolveTo({
      success: true,
      customUserAgent: 'Custom/2.0',
      effectiveUserAgent: 'Custom/2.0',
      defaultUserAgent: DEFAULT_AGENT,
    });
    const f = await create();

    await type(f, '  Custom/2.0  ');
    buttons(f)[0].click();
    await settle(f);

    expect(api.updateHttpSettings).toHaveBeenCalledOnceWith({ userAgent: 'Custom/2.0' });
    expect(buttons(f)[1].disabled).toBeFalse();
  });

  it('sends null on Restore default and shows the default again', async () => {
    api.getHttpSettings.and.resolveTo({
      customUserAgent: 'Custom/2.0',
      effectiveUserAgent: 'Custom/2.0',
      defaultUserAgent: DEFAULT_AGENT,
    });
    api.updateHttpSettings.and.resolveTo({
      success: true,
      customUserAgent: null,
      effectiveUserAgent: DEFAULT_AGENT,
      defaultUserAgent: DEFAULT_AGENT,
    });
    const f = await create();

    buttons(f)[1].click();
    await settle(f);
    await settle(f);

    expect(api.updateHttpSettings).toHaveBeenCalledOnceWith({ userAgent: null });
    expect(input(f).value).toBe(DEFAULT_AGENT);
  });

  it('shows the host rejection and keeps the typed value for correction', async () => {
    api.updateHttpSettings.and.resolveTo({
      success: false,
      error: {
        code: 'InvalidUserAgent',
        message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Http.InvalidUserAgent', arguments: { maxLength: 256 } } },
      },
      customUserAgent: null,
      effectiveUserAgent: DEFAULT_AGENT,
      defaultUserAgent: DEFAULT_AGENT,
    });
    const f = await create();

    await type(f, '(unbalanced');
    buttons(f)[0].click();
    await settle(f);

    expect(f.nativeElement.textContent).toContain('256');
    expect(input(f).value).toBe('(unbalanced');
  });
});
