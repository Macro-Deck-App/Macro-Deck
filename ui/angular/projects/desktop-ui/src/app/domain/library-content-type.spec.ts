import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { bundledTranslator } from '../../testing/localization-test-support';
import { LIBRARY_CONTENT_TYPES } from './library-content-type';

describe('LIBRARY_CONTENT_TYPES', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  // Issue #845 moved icon packs into the library; scripts and automations are named there as future
  // library content but deliberately stay top-level for now.
  it('ships icon packs and leaves scripts and automations out of the library', () => {
    const shipped = TestBed.inject(LIBRARY_CONTENT_TYPES);

    const iconPacks = shipped.find(type => type.route === '/library/icon-packs');
    expect(iconPacks).withContext('icon packs are missing from the library').toBeTruthy();
    expect(iconPacks!.labelKey).toBe(AppStrings.Nav.IconPacks);

    expect(shipped.map(type => type.id)).not.toContain('scripts');
    expect(shipped.map(type => type.id)).not.toContain('automations');
  });

  // A card labelled with a key nobody added to the catalogue renders as literal "[[macrodeck.app:…]]".
  it('labels and describes every content type with a key the catalogue carries', () => {
    for (const type of TestBed.inject(LIBRARY_CONTENT_TYPES)) {
      expect(bundledTranslator(type.labelKey)).withContext(type.id).not.toMatch(/^\[\[/);
      expect(bundledTranslator(type.descriptionKey)).withContext(type.id).not.toMatch(/^\[\[/);
      expect(type.id).withContext('ids must be unique').toBeTruthy();
    }

    const ids = TestBed.inject(LIBRARY_CONTENT_TYPES).map(type => type.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
