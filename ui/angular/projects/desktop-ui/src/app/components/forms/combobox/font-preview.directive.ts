import { DestroyRef, Directive, ElementRef, Input, OnChanges, effect, inject, signal } from '@angular/core';
import { FontLoaderService, internalFontFamily } from '../../../shared/services/font-loader.service';

@Directive({
  selector: '[sharedFontPreview]',
  standalone: true,
})
export class FontPreviewDirective implements OnChanges {
  @Input('sharedFontPreview') faceId: string | undefined;

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly fontLoader = inject(FontLoaderService);
  private readonly visibleFaceId = signal<string | undefined>(undefined);
  private observer: IntersectionObserver | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.observer?.disconnect());
    effect(() => {
      const faceId = this.visibleFaceId();
      const ready = faceId !== undefined && this.fontLoader.ensureFace(faceId)() === 'ready';
      this.element.nativeElement.style.fontFamily = ready ? `"${internalFontFamily(faceId)}", var(--font-sans)` : '';
    });
  }

  ngOnChanges(): void {
    this.observer?.disconnect();
    this.visibleFaceId.set(undefined);
    const faceId = this.faceId;
    if (!faceId) {
      return;
    }
    // A long font list would otherwise fetch every face at once; only rows scrolled into view load theirs.
    if (typeof IntersectionObserver === 'undefined') {
      this.visibleFaceId.set(faceId);
      return;
    }
    this.observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        this.visibleFaceId.set(faceId);
        this.observer?.disconnect();
      }
    });
    this.observer.observe(this.element.nativeElement);
  }
}
