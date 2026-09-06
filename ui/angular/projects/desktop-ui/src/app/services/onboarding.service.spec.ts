import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ApiService } from '@shared';
import { OnboardingService } from './onboarding.service';

describe('OnboardingService', () => {
  let api: jasmine.SpyObj<ApiService>;
  let service: OnboardingService;

  function create(): void {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getOnboardingState', 'completeOnboarding']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
      ],
    });

    service = TestBed.inject(OnboardingService);
  }

  it('asks the host nothing until it is told to load', () => {
    create();

    expect(service.state()).toBe('unknown');
    expect(service.visible()).toBeFalse();
    expect(api.getOnboardingState).not.toHaveBeenCalled();
  });

  it('reports a wizard the host still owes as pending, without showing it yet', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: true });

    await service.load();

    expect(service.state()).toBe('pending');
    expect(service.visible()).toBeFalse();
  });

  it('reports a returning user as done', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: false });

    await service.load();

    expect(service.state()).toBe('done');
  });

  it('stays unknown when the host cannot be asked, rather than assuming the wizard was shown', async () => {
    create();
    api.getOnboardingState.and.rejectWith(new Error('offline'));

    await service.load();

    expect(service.state()).toBe('unknown');
    expect(service.visible()).toBeFalse();
  });

  it('cannot be shown once the host says there is nothing owed', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: false });
    await service.load();

    service.present();

    expect(service.visible()).toBeFalse();
  });

  it('shows a pending wizard when asked to', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: true });
    await service.load();

    service.present();

    expect(service.visible()).toBeTrue();
  });

  it('tells the host once, however often it is completed', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: true });
    api.completeOnboarding.and.resolveTo({ pending: false });
    await service.load();
    service.present();

    await service.complete();
    await service.complete();

    expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
    expect(service.state()).toBe('done');
    expect(service.visible()).toBeFalse();
  });

  it('still closes the wizard when the host refuses to record it', async () => {
    create();
    api.getOnboardingState.and.resolveTo({ pending: true });
    api.completeOnboarding.and.rejectWith(new Error('offline'));
    await service.load();
    service.present();

    await service.complete();

    expect(service.visible()).toBeFalse();
    expect(service.state()).toBe('done');
  });
});
