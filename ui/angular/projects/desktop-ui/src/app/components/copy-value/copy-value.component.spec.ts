import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService, ToastService } from '@shared';
import { clipboardFailureDetail, TextClipboardService } from '../../services/text-clipboard.service';
import { CopyValueComponent } from './copy-value.component';

describe('CopyValueComponent', () => {
  let fixture: ComponentFixture<CopyValueComponent>;
  let clipboard: jasmine.SpyObj<TextClipboardService>;
  let toasts: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    clipboard = jasmine.createSpyObj<TextClipboardService>('TextClipboardService', ['copyText']);
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());

    TestBed.configureTestingModule({
      imports: [CopyValueComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: TextClipboardService, useValue: clipboard },
        { provide: ToastService, useValue: toasts },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    fixture = TestBed.createComponent(CopyValueComponent);
    fixture.componentRef.setInput('label', 'Redirect URI');
    fixture.componentRef.setInput('value', 'http://192.168.1.5:8080/callback');
  });

  function button(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-button button') as HTMLElement;
  }

  function fallbackModal(): Element | null {
    return fixture.nativeElement.querySelector('shared-copy-text-modal');
  }

  it('renders the label and the value verbatim', async () => {
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.cv-label').textContent).toBe('Redirect URI');
    expect(fixture.nativeElement.querySelector('.cv-value').textContent).toBe('http://192.168.1.5:8080/callback');
  });

  it('copies exactly the value when the button is clicked', async () => {
    clipboard.copyText.and.resolveTo({ status: 'copied', via: 'clipboard-api' });
    fixture.detectChanges();
    await fixture.whenStable();

    button().click();
    await fixture.whenStable();

    expect(clipboard.copyText).toHaveBeenCalledWith('http://192.168.1.5:8080/callback');
  });

  it('shows a toast and no fallback modal when the copy succeeds', async () => {
    clipboard.copyText.and.resolveTo({ status: 'copied', via: 'clipboard-api' });
    fixture.detectChanges();
    await fixture.whenStable();

    button().click();
    await fixture.whenStable();

    expect(toasts.show).toHaveBeenCalledWith('Redirect URI copied', { detail: 'http://192.168.1.5:8080/callback' });
    expect(fallbackModal()).toBeNull();
  });

  it('renders the fallback modal with the failure detail and the raw value when the copy fails', async () => {
    clipboard.copyText.and.resolveTo({ status: 'failed', reason: 'insecure-context' });
    fixture.detectChanges();
    await fixture.whenStable();

    button().click();
    await fixture.whenStable();

    const modal = fallbackModal();
    expect(modal).not.toBeNull();
    expect(toasts.show).not.toHaveBeenCalled();

    const expectedMessage = `${clipboardFailureDetail('insecure-context')} Select the value below and copy it manually.`;
    expect(modal?.querySelector('.copy-text-message')?.textContent).toBe(expectedMessage);
    expect((modal?.querySelector('.copy-text-field') as HTMLInputElement).value)
      .toBe('http://192.168.1.5:8080/callback');
  });

  it('forwards fallbackZIndex to the fallback modal', async () => {
    clipboard.copyText.and.resolveTo({ status: 'failed', reason: 'insecure-context' });
    fixture.componentRef.setInput('fallbackZIndex', 1100);
    fixture.detectChanges();
    await fixture.whenStable();

    button().click();
    await fixture.whenStable();

    const overlay = fixture.nativeElement.querySelector('.modal-overlay') as HTMLElement;
    expect(overlay.style.zIndex).toBe('1100');
  });
});
