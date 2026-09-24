import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { ExternalLinkService } from '../../services/external-link.service';
import { StoreFooterComponent } from './store-footer.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('StoreFooterComponent', () => {
  let externalLinks: jasmine.SpyObj<ExternalLinkService>;

  function render(): HTMLElement {
    externalLinks = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);
    const api = jasmine.createSpyObj<ApiService>('ApiService', ['getStoreCreatorGuidelines', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    api.getStoreCreatorGuidelines.and.resolveTo({ available: true, markdown: 'Be nice.' });
    TestBed.configureTestingModule({
      imports: [StoreFooterComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ExternalLinkService, useValue: externalLinks },
        { provide: ApiService, useValue: api },
      ],
    });
    fixture = TestBed.createComponent(StoreFooterComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  let fixture: ComponentFixture<StoreFooterComponent>;

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

  it('keeps the Store guidelines reachable after the first-visit notice is gone', async () => {
    const host = render();

    (host.querySelector('.guidelines-link') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host.querySelector('app-store-guidelines-modal')?.textContent).toContain('Be nice.');
  });
});
