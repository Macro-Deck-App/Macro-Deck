import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { AppStrings, ImportUserFontsResponse, UserFont } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { FONT_FACE_BACKEND } from '../../../shared/services/font-loader.service';
import { bundledTranslator, provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { FontsLibraryPageComponent } from './fonts-library-page.component';

function userFont(overrides: Partial<UserFont> = {}): UserFont {
  return {
    fontId: 'brand-regular',
    faceId: 'brand-sans-400-5-upright',
    family: 'Brand Sans',
    styleName: 'Regular',
    weight: 400,
    width: 5,
    slant: 'upright',
    format: 'ttf',
    sizeBytes: 1024,
    ...overrides,
  };
}

describe('FontsLibraryPageComponent', () => {
  let fixture: ComponentFixture<FontsLibraryPageComponent>;
  let api: jasmine.SpyObj<ApiService>;

  async function render(fonts: UserFont[]): Promise<void> {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getUserFonts',
      'importUserFonts',
      'deleteUserFont',
      'getSystemFonts',
      'getFontFileUrl',
      'onNotification',
    ]);
    api.onNotification.and.callFake(() => new Subject());
    api.getUserFonts.and.resolveTo({ fonts });
    api.getSystemFonts.and.resolveTo({ faces: [] });
    api.getFontFileUrl.and.callFake(faceId => `http://host/api/system/fonts/${faceId}/file`);

    TestBed.configureTestingModule({
      imports: [FontsLibraryPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api },
        { provide: FONT_FACE_BACKEND, useValue: null },
      ],
    });

    fixture = TestBed.createComponent(FontsLibraryPageComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent!;
  }

  async function pick(response: ImportUserFontsResponse | Error, names: string[]): Promise<void> {
    if (response instanceof Error) {
      api.importUserFonts.and.rejectWith(response);
    } else {
      api.importUserFonts.and.resolveTo(response);
    }
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    const transfer = new DataTransfer();
    names.forEach(name => transfer.items.add(new File(['x'], name)));
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  function toasts(): { message: string; variant: string }[] {
    return TestBed.inject(ToastService).toasts().map(({ message, variant }) => ({ message, variant }));
  }

  function removeButton(font: UserFont): HTMLButtonElement {
    const label = bundledTranslator(AppStrings.Library.Fonts.RemoveAriaLabel, { name: `${font.family} ${font.styleName}` });
    const button = fixture.nativeElement.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;
    expect(button).withContext(`no remove button labelled "${label}"`).toBeTruthy();
    return button!;
  }

  it('explains how to add fonts when none are imported', async () => {
    await render([]);

    expect(text()).toContain(bundledTranslator(AppStrings.Library.Fonts.EmptyTitle));
    expect(text()).toContain(bundledTranslator(AppStrings.Library.Fonts.LicenseNote));
  });

  it('lists faces under their family with style, format and a sample in the face itself', async () => {
    const regular = userFont();
    const bold = userFont({ fontId: 'brand-bold', faceId: 'brand-sans-700-5-upright', styleName: 'Bold', weight: 700, format: 'otf' });
    await render([bold, regular]);

    const family = fixture.nativeElement.querySelector('.font-family') as HTMLElement;
    expect(family.querySelector('.font-family-name')!.textContent!.trim()).toBe('Brand Sans');
    const rows = Array.from(family.querySelectorAll('.font-face')) as HTMLElement[];
    expect(rows.map(row => row.querySelector('.font-face-style')!.textContent!.trim())).toEqual(['Regular', 'Bold']);
    expect(rows[1].textContent).toContain('OTF');
    const sample = rows[1].querySelector('.font-face-sample') as HTMLElement;
    expect(sample.textContent!.trim()).toBe(bundledTranslator(AppStrings.Library.Fonts.Sample));
    expect(sample.style.fontFamily).toContain('MacroDeckFont_brand-sans-700-5-upright');
  });

  it('imports picked files and says what happened to each one', async () => {
    await render([]);
    const font = userFont();
    api.getUserFonts.and.resolveTo({ fonts: [font] });

    await pick({
      success: true,
      results: [
        { fileName: 'BrandSans.ttf', status: 'Imported', font },
        { fileName: 'Installed.ttf', status: 'AlreadyInstalled', font: null },
        { fileName: 'notes.txt', status: 'UnsupportedFormat', font: null },
      ],
    }, ['BrandSans.ttf', 'Installed.ttf', 'notes.txt']);

    expect(api.importUserFonts).toHaveBeenCalledTimes(1);
    expect(api.importUserFonts.calls.mostRecent().args[0].map(file => file.name))
      .toEqual(['BrandSans.ttf', 'Installed.ttf', 'notes.txt']);
    expect(api.getSystemFonts).toHaveBeenCalled();
    expect(toasts()).toEqual([
      { message: bundledTranslator(`${AppStrings.Library.Fonts.Imported}.One`, { count: 1 }), variant: 'success' },
      { message: bundledTranslator(AppStrings.Library.Fonts.Status.AlreadyInstalled, { fileName: 'Installed.ttf' }), variant: 'error' },
      { message: bundledTranslator(AppStrings.Library.Fonts.Status.UnsupportedFormat, { fileName: 'notes.txt' }), variant: 'error' },
    ]);
    expect(fixture.nativeElement.querySelector('.font-family-name')?.textContent?.trim()).toBe('Brand Sans');
  });

  it('says the import failed when the upload does not go through', async () => {
    await render([]);

    await pick(new Error('offline'), ['BrandSans.ttf']);

    expect(toasts()).toEqual([
      { message: bundledTranslator(AppStrings.Library.Fonts.ImportFailed), variant: 'error' },
    ]);
  });

  it('removes a face only after the removal is confirmed', async () => {
    const font = userFont();
    await render([font]);
    api.deleteUserFont.and.resolveTo({ success: true });

    removeButton(font).click();
    await settle();
    expect(api.deleteUserFont).not.toHaveBeenCalled();
    const modal = fixture.debugElement.query(By.directive(ConfirmationModalComponent));
    expect(modal).withContext('no confirmation asked').toBeTruthy();
    expect(text()).toContain(bundledTranslator(AppStrings.Library.Fonts.RemoveConfirmMessage, { name: 'Brand Sans Regular' }));

    (modal.componentInstance as ConfirmationModalComponent).confirm.emit();
    await settle();

    expect(api.deleteUserFont).toHaveBeenCalledWith(font.fontId);
    expect(fixture.nativeElement.querySelector('.font-face')).toBeNull();
    expect(text()).toContain(bundledTranslator(AppStrings.Library.Fonts.EmptyTitle));
  });

  it('keeps the face and says so when it cannot be removed', async () => {
    const font = userFont();
    await render([font]);
    api.deleteUserFont.and.resolveTo({ success: false });

    removeButton(font).click();
    await settle();
    (fixture.debugElement.query(By.directive(ConfirmationModalComponent)).componentInstance as ConfirmationModalComponent)
      .confirm.emit();
    await settle();

    expect(fixture.nativeElement.querySelector('.font-face')).not.toBeNull();
    expect(toasts()).toEqual([
      { message: bundledTranslator(AppStrings.Library.Fonts.RemoveFailed), variant: 'error' },
    ]);
  });
});
