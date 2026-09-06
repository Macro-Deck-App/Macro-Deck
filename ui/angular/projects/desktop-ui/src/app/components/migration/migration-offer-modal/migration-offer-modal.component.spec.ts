import { provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { ApiService, AuthService, AuthState, ConnectionState } from '@shared';
import { MigrationOfferModalComponent } from './migration-offer-modal.component';
import { MigrationOfferService } from '../../../services/migration-offer.service';
import { MigrationService, MigrationSourcesOutcome } from '../../../services/migration.service';
import { MigrationWizardService } from '../../../services/migration-wizard.service';
import { OnboardingService } from '../../../services/onboarding.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MigrationOfferModalComponent', () => {
  let fixture: ComponentFixture<MigrationOfferModalComponent>;
  let offer: MigrationOfferService;
  let wizard: MigrationWizardService;
  let authState: WritableSignal<AuthState>;
  let connectionState: WritableSignal<ConnectionState>;
  let migration: jasmine.SpyObj<MigrationService>;
  let onboarding: OnboardingService;

  async function modal(): Promise<HTMLElement | null> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('shared-confirmation-modal');
  }

  async function clickAndAwaitClose(button: HTMLButtonElement | undefined): Promise<void> {
    jasmine.clock().install();
    try {
      button?.click();
      jasmine.clock().tick(150);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function buttonNamed(root: Element, label: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes(label));
  }

  async function create(sourcesOutcome: MigrationSourcesOutcome): Promise<void> {
    authState = signal<AuthState>('unknown');
    connectionState = signal<ConnectionState>('disconnected');
    migration = jasmine.createSpyObj<MigrationService>('MigrationService', ['getSources']);
    migration.getSources.and.resolveTo(sourcesOutcome);

    await TestBed.configureTestingModule({
      imports: [MigrationOfferModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AuthService, useValue: { state: authState.asReadonly() } },
        {
          provide: ApiService,
          useValue: { connectionStateSignal: connectionState.asReadonly(), onNotification: () => EMPTY },
        },
        { provide: MigrationService, useValue: migration },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MigrationOfferModalComponent);
    offer = TestBed.inject(MigrationOfferService);
    wizard = TestBed.inject(MigrationWizardService);
    onboarding = TestBed.inject(OnboardingService);
    // The offer now queues behind the first-launch onboarding wizard, so every case that is not about
    // that ordering starts from a device that has already seen it.
    onboarding.state.set('done');
  }

  beforeEach(() => localStorage.clear());

  it('never presents itself while no offer is pending', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    offer.arm();
    offer.dismiss();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeNull();
  });

  it('does not present the offer until the app is both connected and authenticated', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    offer.arm();

    expect(await modal()).toBeNull();

    authState.set('authenticated');
    expect(await modal()).toBeNull();

    connectionState.set('connected');
    expect(await modal()).toBeTruthy();
  });

  it('never shows itself when no source has a detected installation', async () => {
    await create({
      status: 'success',
      sources: [{ id: 'macrodeck2', name: 'Macro Deck 2' }],
    });
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeNull();
    expect(offer.pending()).toBeFalse();
  });

  it('shows itself once a source with a detected path is found', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeTruthy();
  });

  it('leaves the flag armed, without showing anything, when the source lookup fails', async () => {
    await create({ status: 'error', message: 'not reachable' });
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeNull();
    expect(offer.pending()).toBeTrue();
  });

  it('opens the migration wizard with the detected path already filled in, and clears the flag when accepted', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');
    const dialog = await modal();

    await clickAndAwaitClose(buttonNamed(dialog!, 'Migrate now'));

    expect(wizard.isOpen()).toBeTrue();
    expect(offer.pending()).toBeFalse();
  });

  it('only clears the flag, without opening the wizard, when declined', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');
    const dialog = await modal();

    await clickAndAwaitClose(buttonNamed(dialog!, 'Not now'));

    expect(wizard.isOpen()).toBeFalse();
    expect(offer.pending()).toBeFalse();
  });

  it('stays behind the onboarding wizard while one is still owed', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    onboarding.state.set('pending');
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeNull();
    expect(offer.pending()).toBeTrue();
  });

  it('presents itself as soon as the onboarding wizard is done, without a reload', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    onboarding.state.set('pending');
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');
    expect(await modal()).toBeNull();

    onboarding.state.set('done');

    expect(await modal()).toBeTruthy();
  });

  it('waits out the window in which it is not yet known whether a wizard is owed', async () => {
    await create({ status: 'success', sources: [{ id: 'macrodeck2', name: 'Macro Deck 2', defaultPath: '/data/macrodeck2' }] });
    onboarding.state.set('unknown');
    offer.arm();
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await modal()).toBeNull();
    expect(offer.pending()).toBeTrue();
  });
});
