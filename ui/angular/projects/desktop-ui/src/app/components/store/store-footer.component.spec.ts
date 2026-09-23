import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ExternalLinkService } from '../../services/external-link.service';
import { StoreFooterComponent } from './store-footer.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('StoreFooterComponent', () => {
  let externalLinks: jasmine.SpyObj<ExternalLinkService>;

  function render(): HTMLElement {
    externalLinks = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);
    TestBed.configureTestingModule({
      imports: [StoreFooterComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ExternalLinkService, useValue: externalLinks },
      ],
    });
    const fixture = TestBed.createComponent(StoreFooterComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('opens the Creator Portal, imprint and privacy policy in the system browser', () => {
    const host = render();

    for (const link of Array.from(host.querySelectorAll('a'))) {
      link.click();
    }

    expect(externalLinks.open.calls.allArgs()).toEqual([
      ['https://creators.macro-deck.app'],
      ['https://macro-deck.app/imprint'],
      ['https://macro-deck.app/privacy/platform'],
    ]);
  });

  it('names the current year in the copyright line', () => {
    expect(render().querySelector('.copyright')?.textContent).toContain(String(new Date().getFullYear()));
  });
});
