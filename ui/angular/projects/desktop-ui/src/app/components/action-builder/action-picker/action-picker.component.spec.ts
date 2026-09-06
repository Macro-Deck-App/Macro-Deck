import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { ActionBlockDefinition } from '@macro-deck/runtime';
import { ActionPickerComponent } from './action-picker.component';
import { ActionFlowStore } from '../services/action-flow.store';
import { IntegrationService } from '../../../services/integration.service';

function def(
  partial: Partial<ActionBlockDefinition> & Pick<ActionBlockDefinition, 'category'>,
): ActionBlockDefinition {
  return {
    blockType: `${partial.category}.block`,
    type: 'action',
    label: 'Block',
    color: '#000',
    ...partial,
  };
}

const BUILT_IN_CATEGORY_IDS: Readonly<Record<string, string>> = {
  Deck: 'app.macro-deck.deck',
  Logic: 'Logic',
  Scripts: 'app.macro-deck.scripts',
  Widget: 'app.macro-deck.widget',
};

function builtIn(category: string, extra: Partial<ActionBlockDefinition> = {}): ActionBlockDefinition {
  return def({ category, categoryId: BUILT_IN_CATEGORY_IDS[category], ...extra });
}

describe('ActionPickerComponent category glyphs', () => {
  let component: ActionPickerComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ActionPickerComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionFlowStore,
          useValue: {
            pickerOpenForList: signal<string | null>(null),
            flows: signal([]),
            closePicker: () => {},
            pickAction: () => {},
          },
        },
        {
          provide: ApiService,
          useValue: {
            getIntegrationIconUrl: (id: string) => `/api/integrations/${id}/icon`,
            onNotification: () => new Subject(),
            connectionStateSignal: signal('disconnected'),
          },
        },
        { provide: IntegrationService, useValue: { integrations: signal([]) } },
      ],
    });
    component = TestBed.createComponent(ActionPickerComponent).componentInstance;
  });

  function glyphFor(category: string, defs: ActionBlockDefinition[]): string {
    component.definitions = defs;
    const cat = component.categories().find(c => c.name === category);
    return cat!.fallbackIcon;
  }

  it('maps the Deck category to the grid glyph', () => {
    expect(glyphFor('Deck', [builtIn('Deck', { integrationId: 'app.macro-deck.deck' })])).toBe('icon-grid');
  });

  it('maps the Logic category to the code glyph', () => {
    expect(glyphFor('Logic', [builtIn('Logic')])).toBe('icon-code');
  });

  it('maps the Scripts category to its own glyph, not the one "All" uses', () => {
    const scripts = builtIn('Scripts', { integrationId: 'app.macro-deck.scripts' });
    expect(glyphFor('Scripts', [scripts])).toBe('icon-list-play');
  });

  it('gives every built-in source a glyph of its own, distinct from the "All" entry', () => {
    const ALL_GLYPH = 'icon-layers';
    const builtIns = ['Deck', 'Logic', 'Scripts'];
    const glyphs = builtIns.map(category => glyphFor(category, [builtIn(category)]));

    expect(glyphs).not.toContain(ALL_GLYPH);
    expect(new Set(glyphs).size).withContext(`glyphs collide: ${glyphs.join(', ')}`).toBe(builtIns.length);
  });

  it('falls back to the puzzle glyph for unmapped, brand-less categories', () => {
    expect(glyphFor('Misc', [def({ category: 'Misc' })])).toBe('icon-puzzle');
  });

  // The built-in Widget Actions integration ships no brand icon either (issue #230), so without a
  // mapping it would fall through to the same generic puzzle glyph every unmapped category gets.
  it('maps the Widget category to its own glyph, not the puzzle fallback', () => {
    const widget = builtIn('Widget', { integrationId: 'app.macro-deck.widget' });
    expect(glyphFor('Widget', [widget])).toBe('icon-action-button-type');
  });

  it('prefers an explicit definition icon over the category glyph', () => {
    expect(glyphFor('Deck', [builtIn('Deck', { icon: 'icon-star' })])).toBe('icon-star');
  });

  // The action rows in the right-hand list resolve their own glyph, and have to agree with the source
  // list beside them - both key off the stable category id, never the translated display name.
  it('gives an action row the same glyph its source entry gets', () => {
    component.definitions = [];

    expect(component.glyphFor(builtIn('Logic'))).toBe('icon-code');
    expect(component.glyphFor(builtIn('Deck', { integrationId: 'app.macro-deck.deck' }))).toBe('icon-grid');
    expect(component.glyphFor(def({ category: 'Misc' }))).toBe('icon-puzzle');
  });
});
