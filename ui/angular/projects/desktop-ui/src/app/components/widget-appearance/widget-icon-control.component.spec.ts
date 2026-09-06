import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ApiService, IconImageService } from '@shared';
import { IconPickerDialogComponent } from '../icon-picker-dialog/icon-picker-dialog.component';
import { IconImportBatchModel, IconModel, IconPackModel, IconPackService } from '../../services/icon-pack.service';
import { WidgetIconControlComponent } from './widget-icon-control.component';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('WidgetIconControlComponent', () => {
  let fixture: ComponentFixture<WidgetIconControlComponent>;

  function findButton(label: string): HTMLButtonElement | null {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('shared-button button')) as HTMLButtonElement[];
    return buttons.find(b => b.textContent?.trim() === label) ?? null;
  }

  beforeEach(() => {
    const iconPacksStub = {
      packs: signal<IconPackModel[]>([]),
      isLoading: signal(false),
      loadError: signal<string | null>(null),
      activeBatches: signal<ReadonlyMap<string, IconImportBatchModel>>(new Map()),
      loadPacks: jasmine.createSpy('loadPacks').and.resolveTo(undefined),
      iconsFor: () => signal<IconModel[]>([]).asReadonly(),
      import: jasmine.createSpy('import').and.resolveTo(null),
      dismissBatch: jasmine.createSpy('dismissBatch'),
    };

    TestBed.configureTestingModule({
      imports: [WidgetIconControlComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: IconPackService, useValue: iconPacksStub },
        {
          provide: IconImageService,
          useValue: { getIconUrl: (id: string | undefined) => (id ? 'http://host/icon.webp' : null) },
        },
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });

    fixture = TestBed.createComponent(WidgetIconControlComponent);
  });

  it('renders the thumbnail image when an icon is set', () => {
    fixture.componentRef.setInput('icon', { type: 'icon-pack', reference: 'icon-1' });
    fixture.detectChanges();

    const img = fixture.nativeElement.querySelector('.icon-preview img') as HTMLImageElement | null;
    expect(img).not.toBeNull();
    expect(img!.src).toContain('icon.webp');
  });

  it('renders the placeholder glyph and no image when empty', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.icon-preview img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.icon-preview .icon-image')).not.toBeNull();
  });

  it('renders no thumbnail for a non-icon-pack reference, since only the icon-pack catalog resolves to an image URL here', () => {
    fixture.componentRef.setInput('icon', { type: 'plugin-asset', reference: 'spotify:album/x' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.icon-preview img')).toBeNull();
  });

  it('labels the trigger "Choose icon" with no icon and "Change icon" with one', () => {
    fixture.detectChanges();
    expect(findButton('Choose icon')).not.toBeNull();
    expect(findButton('Change icon')).toBeNull();

    fixture.componentRef.setInput('icon', { type: 'icon-pack', reference: 'icon-1' });
    fixture.detectChanges();

    expect(findButton('Change icon')).not.toBeNull();
  });

  it('shows Remove only once an icon is set, and clicking it emits undefined', () => {
    fixture.detectChanges();
    expect(findButton('Remove')).toBeNull();

    fixture.componentRef.setInput('icon', { type: 'icon-pack', reference: 'icon-1' });
    fixture.detectChanges();

    let emitted: unknown = 'not called';
    fixture.componentInstance.valueChange.subscribe(v => (emitted = v));
    findButton('Remove')!.click();

    expect(emitted).toBeUndefined();
  });

  it('disables the picker and remove controls while a provider is authoritative, without hiding them', () => {
    fixture.componentRef.setInput('icon', { type: 'icon-pack', reference: 'icon-1' });
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    expect(findButton('Change icon')!.disabled).toBeTrue();
    expect(findButton('Remove')!.disabled).toBeTrue();
  });

  it('re-renders the dialog after it was closed (regression guard for the missing (closed) binding)', () => {
    fixture.detectChanges();
    findButton('Choose icon')!.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('shared-icon-picker-dialog')).not.toBeNull();

    const dialog = fixture.debugElement.query(By.directive(IconPickerDialogComponent))
      .componentInstance as IconPickerDialogComponent;
    dialog.closed.emit();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-icon-picker-dialog')).toBeNull();

    findButton('Choose icon')!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-icon-picker-dialog')).not.toBeNull();
  });
});
