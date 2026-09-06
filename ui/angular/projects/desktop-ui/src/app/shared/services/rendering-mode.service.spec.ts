import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';

import { RenderingModeService, SIMPLE_RENDERING_CLASS } from './rendering-mode.service';

function create(): RenderingModeService {
  TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  return TestBed.inject(RenderingModeService);
}

describe('RenderingModeService', () => {
  afterEach(() => {
    document.documentElement.classList.remove(SIMPLE_RENDERING_CLASS);
    localStorage.removeItem('macro-deck.rendering-mode');
  });

  it('renders everything until asked not to', () => {
    const service = create();

    expect(service.mode()).toBe('standard');
    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeFalse();
  });

  // The stylesheets that drop shadows and animations are global and keyed on this class, so the mode
  // is only real once it reaches the document element.
  it('marks the document so the global simplifications apply', () => {
    const service = create();

    service.setMode('simple');

    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeTrue();

    service.setMode('standard');

    expect(document.documentElement.classList.contains(SIMPLE_RENDERING_CLASS)).toBeFalse();
  });

  // A wall-mounted tablet is chosen once and reloaded many times.
  it('keeps the choice across a reload', () => {
    create().setMode('simple');
    TestBed.resetTestingModule();

    expect(create().mode()).toBe('simple');
  });
});
