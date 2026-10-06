import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { FontFaceLoadStatus, FontLoaderService } from '../../../shared/services/font-loader.service';
import { FontPreviewDirective } from './font-preview.directive';

@Component({
  standalone: true,
  imports: [FontPreviewDirective],
  template: `<span [sharedFontPreview]="faceId">Acme</span>`,
})
class HostComponent {
  faceId: string | undefined = 'acme-400-5-upright';
}

class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = [];
  constructor(private readonly callback: IntersectionObserverCallback) {
    FakeIntersectionObserver.instances.push(this);
  }
  observe(): void {}
  disconnect(): void {}
  reveal(): void {
    this.callback([{ isIntersecting: true } as IntersectionObserverEntry], this as unknown as IntersectionObserver);
  }
}

describe('FontPreviewDirective', () => {
  const realObserver = window.IntersectionObserver;
  let status: ReturnType<typeof signal<FontFaceLoadStatus>>;
  let ensureFace: jasmine.Spy;

  beforeEach(() => {
    FakeIntersectionObserver.instances = [];
    (window as unknown as { IntersectionObserver: unknown }).IntersectionObserver = FakeIntersectionObserver;
    status = signal<FontFaceLoadStatus>('loading');
    ensureFace = jasmine.createSpy('ensureFace').and.callFake(() => status.asReadonly());
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), { provide: FontLoaderService, useValue: { ensureFace } }],
    });
  });

  afterEach(() => {
    (window as unknown as { IntersectionObserver: unknown }).IntersectionObserver = realObserver;
  });

  it('loads a face only once its row is visible and draws the row in it once ready', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const span = fixture.nativeElement.querySelector('span') as HTMLElement;

    expect(ensureFace).not.toHaveBeenCalled();

    FakeIntersectionObserver.instances[0].reveal();
    await fixture.whenStable();
    expect(ensureFace).toHaveBeenCalledWith('acme-400-5-upright');
    expect(span.style.fontFamily).toBe('');

    status.set('ready');
    await fixture.whenStable();
    expect(span.style.fontFamily).toContain('MacroDeckFont_acme-400-5-upright');
  });
});
