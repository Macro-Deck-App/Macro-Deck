import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LIBRARY_CONTENT_TYPES } from './domain/library-content-type';
import { IconPacksPageComponent } from './components/pages/icon-packs-page/icon-packs-page.component';
import { routes } from './app.routes';

describe('application routes', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter(routes)],
    });
    router = TestBed.inject(Router);
  });

  function activatedComponent(): unknown {
    let snapshot = router.routerState.snapshot.root;
    while (snapshot.firstChild) {
      snapshot = snapshot.firstChild;
    }
    return snapshot.component;
  }

  it('opens the library on its own landing view rather than a content type', async () => {
    expect(await router.navigateByUrl('/library')).toBeTrue();

    expect(router.url).toBe('/library');
    expect(activatedComponent()).not.toBe(IconPacksPageComponent);
  });

  it('shows icon pack management inside the library', async () => {
    expect(await router.navigateByUrl('/library/icon-packs')).toBeTrue();

    expect(router.url).toBe('/library/icon-packs');
    expect(activatedComponent()).toBe(IconPacksPageComponent);
  });

  // Issue #845 moved the view; a link someone already has must still arrive at it.
  it('still lands a pre-library icon packs link on icon pack management', async () => {
    expect(await router.navigateByUrl('/icon-packs')).toBeTrue();

    expect(router.url).toBe('/library/icon-packs');
    expect(activatedComponent()).toBe(IconPacksPageComponent);
  });

  // A registered content type with no reachable route is a dead card on the library overview.
  it('gives every registered library content type a reachable route', async () => {
    for (const type of TestBed.inject(LIBRARY_CONTENT_TYPES)) {
      expect(await router.navigateByUrl(type.route)).withContext(type.id).toBeTrue();
      expect(router.url).withContext(type.id).toBe(type.route);
    }
  });
});
