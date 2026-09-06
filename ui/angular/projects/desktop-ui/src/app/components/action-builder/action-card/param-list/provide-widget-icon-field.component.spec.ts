import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlock } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ActionFlowStore, IconProviderToggleRequest } from '../../services/action-flow.store';
import { ProvideWidgetIconFieldComponent } from './provide-widget-icon-field.component';

function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
  return {
    id: 'blk-1',
    type: 'action',
    blockType: 'app.example.spotify.current-track',
    label: 'Current Track',
    color: '',
    integrationId: 'app.example.spotify',
    actionId: 'current-track',
    ...overrides,
  };
}

describe('ProvideWidgetIconFieldComponent controlled-checkbox contract (issue #425)', () => {
  let fixture: ComponentFixture<ProvideWidgetIconFieldComponent>;
  let iconProviderBlockId: WritableSignal<string | undefined>;
  let requestSpy: jasmine.Spy<(request: IconProviderToggleRequest) => void>;

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.provide-icon-button') as HTMLButtonElement;
  }

  function isChecked(): boolean {
    return button().getAttribute('aria-pressed') === 'true';
  }

  function flipSwitch(): void {
    button().click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    iconProviderBlockId = signal<string | undefined>(undefined);
    requestSpy = jasmine.createSpy('requestIconProviderToggle');
    const store = {
      iconProviderBlockId,
      requestIconProviderToggle: requestSpy,
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [ProvideWidgetIconFieldComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionFlowStore, useValue: store },
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    fixture = TestBed.createComponent(ProvideWidgetIconFieldComponent);
    fixture.componentRef.setInput('block', block());
    fixture.detectChanges();
  });

  it('starts unchecked when no block currently provides the icon', () => {
    expect(isChecked()).toBeFalse();
  });

  it('requests the toggle but reverts to unchecked when the owner ignores the request', () => {
    flipSwitch();

    expect(requestSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      blockId: 'blk-1',
      integrationId: 'app.example.spotify',
      actionId: 'current-track',
      checked: true,
    }));

    // The store never adopted this block as the icon provider - the next render reflects that,
    // rather than keeping whatever the switch flipped itself to locally.
    fixture.detectChanges();
    expect(isChecked()).toBeFalse();
  });

  it('shows checked once the store actually adopts this block as the icon provider', () => {
    flipSwitch();
    fixture.detectChanges();
    expect(isChecked()).toBeFalse();

    iconProviderBlockId.set('blk-1');
    fixture.detectChanges();

    expect(isChecked()).toBeTrue();
  });

  it('reverts to unchecked when a different block becomes the provider instead', () => {
    iconProviderBlockId.set('blk-1');
    fixture.detectChanges();
    expect(isChecked()).toBeTrue();

    iconProviderBlockId.set('blk-2');
    fixture.detectChanges();

    expect(isChecked()).toBeFalse();
  });
});
