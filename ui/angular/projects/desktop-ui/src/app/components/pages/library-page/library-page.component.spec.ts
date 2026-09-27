import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { LIBRARY_CONTENT_TYPES } from '../../../domain/library-content-type';
import { bundledTranslator, provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { routes } from '../../../app.routes';

@Component({
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: '<router-outlet />',
})
class TestHostComponent {}

describe('LibraryPageComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let router: Router;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        // The shell route lives under the app shell, so mount the library subtree on its own.
        provideRouter(
          routes.find(route => route.children)!.children!
            .filter(route => route.path?.startsWith('library'))),
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(TestHostComponent);
  });

  async function open(url: string): Promise<void> {
    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent!;
  }

  // The switcher belongs to the library, not to the overview: a content type has to be reachable
  // while another content type is on screen.
  it('keeps the content type switcher above a content type view', async () => {
    await open('/library/icon-packs');

    expect(fixture.nativeElement.querySelector('app-library-content-type-switcher')).toBeTruthy();
    for (const type of TestBed.inject(LIBRARY_CONTENT_TYPES)) {
      expect(text()).withContext(type.id).toContain(bundledTranslator(type.labelKey));
    }
  });

  it('shows icon pack management itself, not just the library frame', async () => {
    await open('/library/icon-packs');

    expect(text()).toContain(bundledTranslator(AppStrings.Nav.IconPacks));
    expect(text()).toContain(bundledTranslator(AppStrings.IconPacks.NoPacksYetHeading));
  });

  it('marks the shown content type as the current one', async () => {
    await open('/library/icon-packs');

    const active = fixture.nativeElement.querySelector('.seg-option.active') as HTMLElement | null;
    expect(active?.textContent!.trim()).toBe(bundledTranslator(AppStrings.Nav.IconPacks));
  });

  it('still marks the content type current when the link carries query parameters', async () => {
    await open('/library/icon-packs?pack=abc');

    const active = fixture.nativeElement.querySelector('.seg-option.active') as HTMLElement | null;
    expect(active?.textContent!.trim()).toBe(bundledTranslator(AppStrings.Nav.IconPacks));
  });

  it('marks the overview as current on the library landing view', async () => {
    await open('/library');

    const active = fixture.nativeElement.querySelector('.seg-option.active') as HTMLElement | null;
    expect(active?.textContent!.trim()).toBe(bundledTranslator(AppStrings.Library.Page.OverviewOption));
  });
});
