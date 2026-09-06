import { MigrationOfferService } from './migration-offer.service';

describe('MigrationOfferService', () => {
  beforeEach(() => localStorage.clear());

  it('has nothing pending for a device that never armed the offer', () => {
    expect(new MigrationOfferService().pending()).toBeFalse();
  });

  it('remembers an armed offer across instances, as a per-device flag', () => {
    new MigrationOfferService().arm();

    expect(new MigrationOfferService().pending()).toBeTrue();
  });

  it('presents the offer only once told to', () => {
    const service = new MigrationOfferService();
    service.arm();

    expect(service.visible()).toBeFalse();

    service.present();

    expect(service.visible()).toBeTrue();
  });

  it('clears the flag for good once dismissed, even across instances', () => {
    const first = new MigrationOfferService();
    first.arm();
    first.present();

    first.dismiss();

    expect(first.pending()).toBeFalse();
    expect(first.visible()).toBeFalse();
    expect(new MigrationOfferService().pending()).toBeFalse();
  });

  it('tolerates a localStorage that throws', () => {
    spyOn(localStorage, 'getItem').and.throwError('blocked');
    spyOn(localStorage, 'setItem').and.throwError('blocked');
    spyOn(localStorage, 'removeItem').and.throwError('blocked');

    const service = new MigrationOfferService();
    expect(() => service.arm()).not.toThrow();
    expect(() => service.dismiss()).not.toThrow();
  });
});
