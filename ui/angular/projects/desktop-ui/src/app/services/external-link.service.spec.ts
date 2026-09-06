import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ExternalLinkService } from './external-link.service';

describe('ExternalLinkService', () => {
  let service: ExternalLinkService;

  beforeEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ExternalLinkService],
    });
    service = TestBed.inject(ExternalLinkService);
  });

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  it('opens a browser tab without the shell bridge', () => {
    const open = spyOn(window, 'open');

    service.open('https://developer.spotify.com/dashboard');

    expect(open).toHaveBeenCalledWith('https://developer.spotify.com/dashboard', '_blank', 'noopener,noreferrer');
    expect(service.usesShell).toBeFalse();
  });

  it('routes through the shell bridge instead of window.open', () => {
    const openExternal = jasmine.createSpy('openExternal').and.resolveTo(true);
    (window as { macroDeckShell?: unknown }).macroDeckShell = { openExternal };
    const open = spyOn(window, 'open');

    service.open('https://developer.spotify.com/dashboard');

    expect(openExternal).toHaveBeenCalledWith('https://developer.spotify.com/dashboard');
    // The whole point: the WebView drops window.open, so it must not be the path taken.
    expect(open).not.toHaveBeenCalled();
    expect(service.usesShell).toBeTrue();
  });

  it('reports a rejected bridge call instead of throwing', async () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      openExternal: () => Promise.reject(new Error('IPC gone')),
    };
    const error = spyOn(console, 'error');

    expect(() => service.open('https://example.com')).not.toThrow();

    await Promise.resolve();
    expect(error).toHaveBeenCalled();
  });

  it('reports a throwing bridge call instead of propagating it', () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      openExternal: () => {
        throw new Error('IPC gone');
      },
    };
    const error = spyOn(console, 'error');

    expect(() => service.open('https://example.com')).not.toThrow();
    expect(error).toHaveBeenCalled();
  });
});
