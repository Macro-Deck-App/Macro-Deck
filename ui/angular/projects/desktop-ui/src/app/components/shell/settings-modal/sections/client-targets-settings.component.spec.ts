import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import {
  AdbStateChangedEvent,
  WebClientTargetDto,
  WebClientTargetProvisioningResultDto,
} from '@macro-deck/runtime';
import {
  ApiService,
} from '@shared';

import { ClientTargetsSettingsComponent } from './client-targets-settings.component';

describe('ClientTargetsSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let adbStateChanged: Subject<AdbStateChangedEvent>;

  const carThing: WebClientTargetDto = { targetId: 'carthing', canProvision: true };

  const firstStep: WebClientTargetProvisioningResultDto = {
    kind: 'Step',
    step: {
      stepId: 'detect',
      title: 'Find the Car Thing',
      description: 'Connect it and make sure it is authorised.',
      instructions: ['Connect the Car Thing with a USB cable.'],
      values: [],
      links: [],
      fields: [],
      canContinue: true,
    },
    message: null,
  };

  const configureStep: WebClientTargetProvisioningResultDto = {
    kind: 'Step',
    step: {
      stepId: 'configure',
      title: 'Point the device at Macro Deck',
      description: '',
      instructions: [],
      values: [],
      links: [],
      fields: [{
        fieldId: 'kioskConfigPath',
        label: 'Kiosk configuration file',
        description: '',
        defaultValue: '/etc/macrodeck/kiosk-url',
        choices: null,
      }],
      canContinue: true,
    },
    message: null,
  };

  beforeEach(async () => {
    adbStateChanged = new Subject<AdbStateChangedEvent>();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getWebClientTargets',
      'startWebClientTargetProvisioning',
      'advanceWebClientTargetProvisioning',
      'onAdbStateChanged',
      'onNotification',
      'getLocalization',
    ]);
    api.onAdbStateChanged.and.returnValue(adbStateChanged.asObservable());
    api.onNotification.and.returnValue(EMPTY);
    api.getWebClientTargets.and.resolveTo([carThing]);
    api.startWebClientTargetProvisioning.and.resolveTo(firstStep);
    api.advanceWebClientTargetProvisioning.and.resolveTo(configureStep);

    await TestBed.configureTestingModule({
      imports: [ClientTargetsSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<ClientTargetsSettingsComponent>> {
    const fixture = TestBed.createComponent(ClientTargetsSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function button(fixture: ComponentFixture<unknown>, text: string): HTMLButtonElement | undefined {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(candidate => candidate.textContent?.trim() === text);
  }

  async function click(fixture: ComponentFixture<unknown>, text: string): Promise<void> {
    button(fixture, text)!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('says the feature is not finished, before anyone starts a device setup', async () => {
    const fixture = await create();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Experimental');
  });

  it('starts the walkthrough when the set-up button is pressed', async () => {
    const fixture = await create();

    await click(fixture, 'Set up');

    expect(api.startWebClientTargetProvisioning).toHaveBeenCalledWith('carthing');
  });

  it('shows the step the host answered with', async () => {
    const fixture = await create();

    await click(fixture, 'Set up');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Find the Car Thing');
    expect(text).toContain('Connect the Car Thing with a USB cable.');
  });

  it('advances from the step on screen when continue is pressed', async () => {
    const fixture = await create();
    await click(fixture, 'Set up');

    await click(fixture, 'Continue');

    expect(api.advanceWebClientTargetProvisioning).toHaveBeenCalledWith('carthing',
      jasmine.objectContaining({ stepId: 'detect' }));
  });

  it("sends a step's fields seeded from the documented defaults", async () => {
    const fixture = await create();
    await click(fixture, 'Set up');
    await click(fixture, 'Continue');
    api.advanceWebClientTargetProvisioning.calls.reset();

    await click(fixture, 'Continue');

    expect(api.advanceWebClientTargetProvisioning).toHaveBeenCalledWith('carthing', {
      stepId: 'configure',
      input: { kioskConfigPath: '/etc/macrodeck/kiosk-url' },
    });
  });

  it('leaves the walkthrough when it is cancelled', async () => {
    const fixture = await create();
    await click(fixture, 'Set up');

    await click(fixture, 'Cancel');

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Find the Car Thing');
    expect(button(fixture, 'Set up')).toBeDefined();
  });

  it('explains itself when the host packages no device targets at all', async () => {
    api.getWebClientTargets.and.resolveTo([]);
    const fixture = await create();

    expect((fixture.nativeElement as HTMLElement).textContent)
      .toContain('Enable ADB and USB connections under Settings');
  });

  it('offers no set-up for a device the host cannot provision yet', async () => {
    api.getWebClientTargets.and.resolveTo([{ targetId: 'carthing', canProvision: false }]);
    const fixture = await create();

    expect(button(fixture, 'Set up')?.disabled).toBeTrue();
  });

  it('enables set-up once ADB is turned on, without reopening the section', async () => {
    // Turning ADB on happens in the neighbouring settings section while this one is on screen, so a
    // row that stays disabled until the modal is reopened reads as a broken button.
    api.getWebClientTargets.and.resolveTo([{ targetId: 'carthing', canProvision: false }]);
    const fixture = await create();
    expect(button(fixture, 'Set up')?.disabled).toBeTrue();

    api.getWebClientTargets.and.resolveTo([carThing]);
    adbStateChanged.next({} as AdbStateChangedEvent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(button(fixture, 'Set up')?.disabled).toBeFalse();
  });
});
