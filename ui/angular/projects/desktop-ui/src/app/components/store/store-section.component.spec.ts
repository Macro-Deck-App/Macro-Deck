import { CUSTOM_ELEMENTS_SCHEMA, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StoreCatalogItemBody } from '@macro-deck/runtime';
import { StoreOperationService } from '../../services/store-operation.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';
import { StoreExtensionCardComponent } from './store-extension-card.component';
import { StoreSectionComponent } from './store-section.component';

describe('StoreSectionComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  function forwardScrollOffset(direction: 'ltr' | 'rtl'): number {
    document.documentElement.setAttribute('dir', direction);
    TestBed.configureTestingModule({
      imports: [StoreSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: StoreOperationService, useValue: { operationFor: () => () => null } },
      ],
    });
    TestBed.overrideComponent(StoreSectionComponent, {
      remove: { imports: [StoreExtensionCardComponent] },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    const fixture = TestBed.createComponent(StoreSectionComponent);
    fixture.componentRef.setInput('heading', 'Featured');
    fixture.componentRef.setInput('layout', 'row');
    fixture.componentRef.setInput('items', [{ kind: 'plugin', id: 'a' } as unknown as StoreCatalogItemBody]);
    (fixture.nativeElement as HTMLElement).setAttribute('dir', direction);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const track = element.querySelector('.store-section-grid') as HTMLElement;
    track.style.width = '200px';
    expect(getComputedStyle(track).direction).toBe(direction);
    const scrollBy = spyOn(track, 'scrollBy');
    (element.querySelectorAll('.nav-button')[1] as HTMLButtonElement).click();
    fixture.nativeElement.remove();

    return (scrollBy.calls.mostRecent().args[0] as ScrollToOptions).left!;
  }

  it('scrolls a row towards its later items in a left-to-right app', () => {
    expect(forwardScrollOffset('ltr')).toBeGreaterThan(0);
  });

  it('scrolls a row towards its later items in a right-to-left app', () => {
    expect(forwardScrollOffset('rtl')).toBeLessThan(0);
  });
});
