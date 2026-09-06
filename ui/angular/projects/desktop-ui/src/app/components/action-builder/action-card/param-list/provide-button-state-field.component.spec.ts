import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlock } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ButtonStateProviderService } from '../../../../services/button-state-provider.service';
import { ActionFlowStore, StateProviderToggleRequest } from '../../services/action-flow.store';
import { ProvideButtonStateFieldComponent } from './provide-button-state-field.component';

function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
  return {
    id: 'blk-1',
    type: 'action',
    blockType: 'app.example.thermostat.turn-on',
    label: 'Turn on',
    color: '',
    integrationId: 'app.example.thermostat',
    actionId: 'turn-on',
    ...overrides,
  };
}

describe('ProvideButtonStateFieldComponent controlled-checkbox contract (issue #612)', () => {
  let fixture: ComponentFixture<ProvideButtonStateFieldComponent>;
  let stateProviderBlockId: WritableSignal<string | undefined>;
  let requestSpy: jasmine.Spy<(request: StateProviderToggleRequest) => void>;

  // The control is a header icon button, not a switch: its checked state is what aria-pressed reports,
  // which is also what a screen reader announces.
  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.provide-state-button') as HTMLButtonElement;
  }

  function isChecked(): boolean {
    return button().getAttribute('aria-pressed') === 'true';
  }

  function flipSwitch(): void {
    button().click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    stateProviderBlockId = signal<string | undefined>(undefined);
    requestSpy = jasmine.createSpy('requestStateProviderToggle');
    const store = {
      stateProviderBlockId,
      requestStateProviderToggle: requestSpy,
    };
    const provider = jasmine.createSpyObj<ButtonStateProviderService>('ButtonStateProviderService', ['getStates']);
    provider.getStates.and.resolveTo([]);

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [ProvideButtonStateFieldComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionFlowStore, useValue: store },
        { provide: ButtonStateProviderService, useValue: provider },
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    fixture = TestBed.createComponent(ProvideButtonStateFieldComponent);
    fixture.componentRef.setInput('block', block());
    fixture.detectChanges();
  });

  it('starts unchecked when no block currently provides state', () => {
    expect(isChecked()).toBeFalse();
  });

  it('requests the toggle but reverts to unchecked when the owner ignores the request', () => {
    flipSwitch();

    expect(requestSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      blockId: 'blk-1',
      integrationId: 'app.example.thermostat',
      actionId: 'turn-on',
      checked: true,
    }));

    // The store never adopted this block as the provider - the next render reflects that, rather
    // than keeping whatever the switch flipped itself to locally.
    fixture.detectChanges();
    expect(isChecked()).toBeFalse();
  });

  it('shows checked once the store actually adopts this block as the state provider', () => {
    flipSwitch();
    fixture.detectChanges();
    expect(isChecked()).toBeFalse();

    stateProviderBlockId.set('blk-1');
    fixture.detectChanges();

    expect(isChecked()).toBeTrue();
  });

  it('reverts to unchecked when a different block becomes the provider instead', () => {
    stateProviderBlockId.set('blk-1');
    fixture.detectChanges();
    expect(isChecked()).toBeTrue();

    stateProviderBlockId.set('blk-2');
    fixture.detectChanges();

    expect(isChecked()).toBeFalse();
  });
});
