import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ExternalLinkService } from '../../../services/external-link.service';
import { NavigationService } from '../../../services';
import { FooterBarComponent } from './footer-bar.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('FooterBarComponent', () => {
  let fixture: ComponentFixture<FooterBarComponent>;
  let openedLinks: string[];
  let versionLabel: ReturnType<typeof signal<string>>;

  beforeEach(() => {
    openedLinks = [];
    versionLabel = signal('3.0.0');

    TestBed.configureTestingModule({
      imports: [FooterBarComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NavigationService, useValue: { versionLabel } },
        { provide: ExternalLinkService, useValue: { open: (url: string) => openedLinks.push(url) } },
      ],
    });

    fixture = TestBed.createComponent(FooterBarComponent);
    fixture.detectChanges();
  });

  function linkFor(label: string): HTMLAnchorElement {
    const links: HTMLAnchorElement[] = Array.from(fixture.nativeElement.querySelectorAll('a.footer-link'));
    const link = links.find(candidate => candidate.textContent?.includes(label));
    if (!link) throw new Error(`no footer link found for "${label}"`);
    return link;
  }

  const cases: Array<[string, string]> = [
    ['Donate', 'https://ko-fi.com/manuelmayer'],
    ['Join Discord', 'https://discord.macro-deck.app'],
    ['Report Bug', 'https://github.com/Macro-Deck-App/Macro-Deck/issues'],
  ];

  for (const [label, url] of cases) {
    it(`opens ${label} through ExternalLinkService instead of a live target=_blank navigation`, () => {
      const link = linkFor(label);

      const event = new MouseEvent('click', { bubbles: true, cancelable: true });
      link.dispatchEvent(event);

      expect(openedLinks).toEqual([url]);
      expect(event.defaultPrevented).toBeTrue();
    });
  }

  it('shows exactly what NavigationService.versionLabel supplies, live', () => {
    expect(fixture.nativeElement.textContent).toContain('Version 3.0.0');

    versionLabel.set('3.0.1');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Version 3.0.1');
    expect(fixture.nativeElement.textContent).not.toContain('3.0.0');
  });
});
