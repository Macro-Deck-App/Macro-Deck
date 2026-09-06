import { CUSTOM_ELEMENTS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActionBlock } from '@macro-deck/runtime';
import { LocalizationService, UiSessionService } from '@shared';
import { ActionService } from '../../../../services/action.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { GenericActionCardComponent } from './generic-action-card.component';

describe('GenericActionCardComponent state provider control', () => {
  let fixture: ComponentFixture<GenericActionCardComponent>;
  let consumerSupportsStateProvider: boolean;
  let providesButtonState: boolean;
  let consumerSupportsIconProvider: boolean;
  let providesWidgetIcon: boolean;

  const BLOCK: ActionBlock = {
    id: 'action-1',
    type: 'action',
    blockType: 'app.macro-deck.obs.toggle-recording',
    label: 'Toggle Recording',
    color: '',
    integrationId: 'app.macro-deck.obs',
    actionId: 'toggle-recording',
    parameters: [],
  };

  function hasStateControl(): boolean {
    return !!fixture.nativeElement.querySelector('shared-provide-button-state-field');
  }

  function hasIconControl(): boolean {
    return !!fixture.nativeElement.querySelector('shared-provide-widget-icon-field');
  }

  async function render(): Promise<void> {
    fixture.componentRef.setInput('block', BLOCK);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    consumerSupportsStateProvider = false;
    providesButtonState = true;
    consumerSupportsIconProvider = false;
    providesWidgetIcon = false;

    await TestBed.configureTestingModule({
      imports: [GenericActionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionFlowStore,
          useValue: {
            providesButtonState: () => providesButtonState,
            consumerSupportsStateProvider: () => consumerSupportsStateProvider,
            providesWidgetIcon: () => providesWidgetIcon,
            consumerSupportsIconProvider: () => consumerSupportsIconProvider,
            isExpanded: () => false,
            stateProviderBlockId: signal(undefined),
            iconProviderBlockId: signal(undefined),
          },
        },
        {
          provide: ActionService,
          useValue: { definitionFor: () => ({ supportsConfigUi: false }) },
        },
        { provide: UiSessionService, useValue: { open: () => null } },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    })
      .overrideComponent(GenericActionCardComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(GenericActionCardComponent);
  });

  it('offers the control where the surface can consume states', async () => {
    consumerSupportsStateProvider = true;

    await render();

    expect(hasStateControl()).toBeTrue();
  });

  it('hides the control where the surface cannot consume states', async () => {
    consumerSupportsStateProvider = false;

    await render();

    expect(hasStateControl()).toBeFalse();
  });

  it('hides the control for an action that cannot provide states', async () => {
    consumerSupportsStateProvider = true;
    providesButtonState = false;

    await render();

    expect(hasStateControl()).toBeFalse();
  });
});

describe('GenericActionCardComponent icon provider control', () => {
  let fixture: ComponentFixture<GenericActionCardComponent>;
  let consumerSupportsIconProvider: boolean;
  let providesWidgetIcon: boolean;

  const BLOCK: ActionBlock = {
    id: 'action-1',
    type: 'action',
    blockType: 'app.example.spotify.current-track',
    label: 'Current Track',
    color: '',
    integrationId: 'app.example.spotify',
    actionId: 'current-track',
    parameters: [],
  };

  function hasIconControl(): boolean {
    return !!fixture.nativeElement.querySelector('shared-provide-widget-icon-field');
  }

  async function render(): Promise<void> {
    fixture.componentRef.setInput('block', BLOCK);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    consumerSupportsIconProvider = false;
    providesWidgetIcon = true;

    await TestBed.configureTestingModule({
      imports: [GenericActionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionFlowStore,
          useValue: {
            providesButtonState: () => false,
            consumerSupportsStateProvider: () => false,
            providesWidgetIcon: () => providesWidgetIcon,
            consumerSupportsIconProvider: () => consumerSupportsIconProvider,
            isExpanded: () => false,
            stateProviderBlockId: signal(undefined),
            iconProviderBlockId: signal(undefined),
          },
        },
        {
          provide: ActionService,
          useValue: { definitionFor: () => ({ supportsConfigUi: false }) },
        },
        { provide: UiSessionService, useValue: { open: () => null } },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    })
      .overrideComponent(GenericActionCardComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(GenericActionCardComponent);
  });

  it('offers the control where the surface can consume an icon', async () => {
    consumerSupportsIconProvider = true;

    await render();

    expect(hasIconControl()).toBeTrue();
  });

  it('hides the control where the surface cannot consume an icon', async () => {
    consumerSupportsIconProvider = false;

    await render();

    expect(hasIconControl()).toBeFalse();
  });

  it('hides the control for an action that cannot provide an icon', async () => {
    consumerSupportsIconProvider = true;
    providesWidgetIcon = false;

    await render();

    expect(hasIconControl()).toBeFalse();
  });
});
