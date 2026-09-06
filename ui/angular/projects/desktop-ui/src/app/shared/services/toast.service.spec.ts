import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    jasmine.clock().install();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ToastService],
    });
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  it('queues a toast with its detail and default variant', () => {
    service.show('Profile exported', { detail: 'Saved to /tmp/deck.macroDeckProfile' });

    expect(service.toasts()).toEqual([
      jasmine.objectContaining({ message: 'Profile exported', detail: 'Saved to /tmp/deck.macroDeckProfile', variant: 'success' }),
    ]);
  });

  it('auto-dismisses after the default duration', () => {
    service.show('Profile exported');

    jasmine.clock().tick(4999);
    expect(service.toasts().length).toBe(1);

    jasmine.clock().tick(1);
    expect(service.toasts()).toEqual([]);
  });

  it('keeps a toast with a zero duration until it is dismissed', () => {
    const id = service.show('Export failed', { variant: 'error', durationMs: 0 });

    jasmine.clock().tick(60_000);
    expect(service.toasts().length).toBe(1);

    service.dismiss(id);
    expect(service.toasts()).toEqual([]);
  });

  it('dismisses one toast without touching the others', () => {
    const first = service.show('First');
    service.show('Second');

    service.dismiss(first);

    expect(service.toasts().map(toast => toast.message)).toEqual(['Second']);
  });
});
