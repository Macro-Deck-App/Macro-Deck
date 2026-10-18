import { CUSTOM_ELEMENTS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActionBlock, UiNode, UiNodeEvent } from '@macro-deck/runtime';
import {
  LocalizationService, UiSessionHandle, UiSessionOpenRequest, UiSessionOpenRequestSource, UiSessionRejection,
  UiSessionService,
} from '@shared';
import { ActionService } from '../../../../services/action.service';
import { ActionConfigSessionFlusher } from '../../services/action-config-session-flusher.service';
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

  async function render(block: ActionBlock = BLOCK): Promise<void> {
    fixture.componentRef.setInput('block', block);
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

  it('hides the control on a disabled action, which cannot drive the button', async () => {
    consumerSupportsStateProvider = true;
    consumerSupportsIconProvider = true;
    providesWidgetIcon = true;

    await render({ ...BLOCK, disabled: true });

    expect(hasStateControl()).toBeFalse();
    expect(hasIconControl()).toBeFalse();
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

class FakeCardSession implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly generation = signal(1);
  readonly requests: UiSessionOpenRequest[] = [];
  closed = false;

  constructor(private readonly source: UiSessionOpenRequestSource) {
    this.requests.push(typeof source === 'function' ? source() : source);
  }

  readonly sent: UiNodeEvent[] = [];

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }

  replace(): void {
    this.requests.push(typeof this.source === 'function' ? this.source() : this.source);
    this.generation.update(value => value + 1);
  }
}

describe('GenericActionCardComponent configuration session', () => {
  let fixture: ComponentFixture<GenericActionCardComponent>;
  let sessions: FakeCardSession[];

  const block = (value: string): ActionBlock => ({
    id: 'action-1',
    type: 'action',
    blockType: 'acme.say',
    label: 'Say',
    color: '',
    integrationId: 'acme',
    actionId: 'say',
    parameters: [{ name: 'text', value }] as never,
  });

  const tree = (id: string): UiNode => ({ id, type: 'ui.config-stack' } as UiNode);

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    sessions = [];
    await TestBed.configureTestingModule({
      imports: [GenericActionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionFlowStore,
          useValue: {
            providesButtonState: () => false,
            consumerSupportsStateProvider: () => false,
            providesWidgetIcon: () => false,
            consumerSupportsIconProvider: () => false,
            isExpanded: () => true,
            stateProviderBlockId: signal(undefined),
            iconProviderBlockId: signal(undefined),
          },
        },
        { provide: ActionService, useValue: { definitionFor: () => ({ supportsConfigUi: true }) } },
        {
          provide: UiSessionService,
          useValue: {
            open: (source: UiSessionOpenRequestSource) => {
              const session = new FakeCardSession(source);
              sessions.push(session);
              return session;
            },
          },
        },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    })
      .overrideComponent(GenericActionCardComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(GenericActionCardComponent);
    fixture.componentRef.setInput('block', block('a'));
    await settle();
    sessions[0].root.set(tree('first'));
    await settle();
  });

  const parametersOf = (request: UiSessionOpenRequest) => (request as { parameters?: unknown }).parameters;

  it('builds a replaced session from the parameters being edited', async () => {
    fixture.componentRef.setInput('block', block('b'));
    await settle();

    sessions[0].replace();
    await settle();

    expect(parametersOf(sessions[0].requests[1])).toEqual({ text: 'b' });
  });

  it('opens once more with an edit made while a replaced session still showed the old tree', async () => {
    sessions[0].replace();
    await settle();
    fixture.componentRef.setInput('block', block('edited during the gap'));
    await settle();

    sessions[0].root.set(tree('second'));
    await settle();

    expect(sessions[0].closed).toBeTrue();
    expect(sessions.length).toBe(2);
    expect(parametersOf(sessions[1].requests[0])).toEqual({ text: 'edited during the gap' });
  });

  it('keeps a replaced session that already shows what is being edited', async () => {
    sessions[0].replace();
    await settle();
    sessions[0].root.set(tree('second'));
    await settle();

    expect(sessions.length).toBe(1);
    expect(sessions[0].closed).toBeFalse();
  });
});

class FakeReportingSession extends FakeCardSession {
  private listener: ((values: Readonly<Record<string, unknown>>) => void) | null = null;
  flushes = 0;

  onParameterValuesReported(listener: (values: Readonly<Record<string, unknown>>) => void): () => void {
    this.listener = listener;
    return () => this.listener = null;
  }

  flush(): Promise<void> {
    this.flushes++;
    return Promise.resolve();
  }

  report(values: Record<string, unknown>): void {
    this.listener?.(values);
  }
}

describe('GenericActionCardComponent reported parameter values', () => {
  let fixture: ComponentFixture<GenericActionCardComponent>;
  let sessions: FakeReportingSession[];
  let updateParam: jasmine.Spy;

  const BLOCK = {
    id: 'action-1',
    type: 'action',
    blockType: 'acme.compose',
    label: 'Compose',
    color: '',
    integrationId: 'acme',
    actionId: 'compose',
    parameters: [
      { name: 'message', type: 'keyValue', value: {} },
      { name: 'token', type: 'secret', value: '' },
      { name: 'password', type: 'password', value: '' },
    ],
  } as unknown as ActionBlock;

  beforeEach(async () => {
    sessions = [];
    updateParam = jasmine.createSpy('updateParam');
    await TestBed.configureTestingModule({
      imports: [GenericActionCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionFlowStore,
          useValue: {
            providesButtonState: () => false,
            consumerSupportsStateProvider: () => false,
            providesWidgetIcon: () => false,
            consumerSupportsIconProvider: () => false,
            isExpanded: () => true,
            stateProviderBlockId: signal(undefined),
            iconProviderBlockId: signal(undefined),
            updateParam,
          },
        },
        { provide: ActionService, useValue: { definitionFor: () => ({ supportsConfigUi: true }) } },
        {
          provide: UiSessionService,
          useValue: {
            open: (source: UiSessionOpenRequestSource) => {
              const session = new FakeReportingSession(source);
              sessions.push(session);
              return session;
            },
          },
        },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    })
      .overrideComponent(GenericActionCardComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(GenericActionCardComponent);
    fixture.componentRef.setInput('block', BLOCK);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('applies a reported value of a declared parameter as an edit of that parameter', () => {
    sessions[0].report({ message: { content: 'hi' } });

    expect(updateParam).toHaveBeenCalledOnceWith('action-1', 'message', { content: 'hi' });
  });

  it('ignores reported values for undeclared, secret and password parameters', () => {
    sessions[0].report({ undeclared: 'x', token: 'leaked', password: 'hunter2' });

    expect(updateParam).not.toHaveBeenCalled();
  });

  it('is flushed before a save while its session is open, and no longer once it closed', async () => {
    const flusher = TestBed.inject(ActionConfigSessionFlusher);

    await flusher.flushAll();
    fixture.destroy();
    await flusher.flushAll();

    expect(sessions[0].flushes).toBe(1);
  });
});
