import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { ExternalLinkService } from '../../services/external-link.service';
import { STORE_COMMUNITY_NOTICE_HINT, StoreCommunityNoticeComponent } from './store-community-notice.component';

const STORED_KEY = `md.hint.dismissed.${STORE_COMMUNITY_NOTICE_HINT}`;

describe('StoreCommunityNoticeComponent', () => {
  let externalLinks: jasmine.SpyObj<ExternalLinkService>;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  function render(): ComponentFixture<StoreCommunityNoticeComponent> {
    externalLinks = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getStoreCreatorGuidelines', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    api.getStoreCreatorGuidelines.and.resolveTo({ available: true, markdown: 'Be nice.' });
    TestBed.configureTestingModule({
      imports: [StoreCommunityNoticeComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLinkService, useValue: externalLinks },
        { provide: ApiService, useValue: api },
      ],
    });
    const fixture = TestBed.createComponent(StoreCommunityNoticeComponent);
    fixture.detectChanges();
    return fixture;
  }

  function notice(fixture: ComponentFixture<StoreCommunityNoticeComponent>): HTMLElement | null {
    return fixture.nativeElement.querySelector('.community-notice');
  }

  function button(fixture: ComponentFixture<StoreCommunityNoticeComponent>, label: string): HTMLButtonElement {
    const match = (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
      .find(candidate => candidate.textContent?.trim() === label);
    if (!match) throw new Error(`no button ${label}`);
    return match;
  }

  it('tells a first-time visitor who is responsible for Store items and where to turn instead of Macro Deck', () => {
    const text = notice(render())?.textContent ?? '';

    expect(text).toContain('made by the community');
    expect(text).toContain('its creator is responsible');
    expect(text).toContain('rate it, open an issue in its repository or report it');
    expect(text).toContain("don't open issues about plugins or icon packs in the official Macro Deck repository");
    expect(text).toContain('The Store guidelines apply');
  });

  it('opens the imprint and the privacy policy in the system browser', () => {
    const fixture = render();

    for (const link of Array.from(notice(fixture)!.querySelectorAll('a'))) {
      link.click();
    }

    expect(externalLinks.open.calls.allArgs()).toEqual([
      ['https://macro-deck.app/imprint'],
      ['https://macro-deck.app/privacy/platform'],
    ]);
  });

  it('shows the Store guidelines in a modal', async () => {
    const fixture = render();

    button(fixture, 'Store guidelines').click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-store-guidelines-modal')?.textContent).toContain('Be nice.');
  });

  it('stays dismissed after Got it, also after the app is opened again', () => {
    const fixture = render();

    button(fixture, 'Got it').click();
    fixture.detectChanges();

    expect(notice(fixture)).toBeNull();
    expect(localStorage.getItem(STORED_KEY)).toBe('1');

    TestBed.resetTestingModule();
    expect(notice(render())).toBeNull();
  });
});
