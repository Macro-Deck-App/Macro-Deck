import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FileSaveService, savedFileDetail } from './file-save.service';
import { provideLocalizationTesting } from '../../testing/localization-test-support';

describe('FileSaveService', () => {
  let service: FileSaveService;

  beforeEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting(), FileSaveService],
    });
    service = TestBed.inject(FileSaveService);
  });

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  it('falls back to a browser download without the shell bridge', async () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:test');
    spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    const result = await service.save(new Blob(['zip']), 'deck.macroDeckProfile');

    expect(result).toEqual({ status: 'saved', viaDialog: false });
    expect(clickSpy).toHaveBeenCalled();
  });

  it('routes through the shell save dialog and reports the chosen path', async () => {
    const saveFile = jasmine
      .createSpy('saveFile')
      .and.resolveTo({ saved: true, canceled: false, path: '/tmp/deck.macroDeckProfile', error: null });
    (window as { macroDeckShell?: unknown }).macroDeckShell = { saveFile };
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    const result = await service.save(new Blob(['zip']), 'deck.macroDeckProfile');

    expect(result).toEqual({ status: 'saved', path: '/tmp/deck.macroDeckProfile', viaDialog: true });
    expect(clickSpy).not.toHaveBeenCalled();
    const options = saveFile.calls.mostRecent().args[0] as { fileName: string; extensions: string[]; data: ArrayBuffer };
    expect(options.fileName).toBe('deck.macroDeckProfile');
    expect(options.extensions).toEqual(['macroDeckProfile']);
    expect(options.data.byteLength).toBe(3);
  });

  it('reports a dismissed save dialog as canceled', async () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      saveFile: () => Promise.resolve({ saved: false, canceled: true, path: null, error: null }),
    };

    const result = await service.save(new Blob(['zip']), 'deck.macroDeckProfile');

    expect(result).toEqual({ status: 'canceled' });
  });

  it('reports a failed write', async () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      saveFile: () => Promise.resolve({ saved: false, canceled: false, path: null, error: 'permission denied' }),
    };

    const result = await service.save(new Blob(['zip']), 'deck.macroDeckProfile');

    expect(result).toEqual({ status: 'error', message: 'permission denied' });
  });

  it('reports a rejected bridge call', async () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      saveFile: () => Promise.reject(new Error('IPC gone')),
    };

    const result = await service.save(new Blob(['zip']), 'deck.macroDeckProfile');

    expect(result).toEqual({ status: 'error', message: 'IPC gone' });
  });

  it('sends no extension filter for a name without one', async () => {
    const saveFile = jasmine
      .createSpy('saveFile')
      .and.resolveTo({ saved: true, canceled: false, path: '/tmp/deck', error: null });
    (window as { macroDeckShell?: unknown }).macroDeckShell = { saveFile };

    await service.save(new Blob(['zip']), 'deck');

    expect((saveFile.calls.mostRecent().args[0] as { extensions: string[] }).extensions).toEqual([]);
  });
});

describe('savedFileDetail', () => {
  it('names the chosen location when the shell wrote the file', () => {
    expect(savedFileDetail({ fileName: 'deck.macroDeckProfile', path: '/tmp/deck.macroDeckProfile' }))
      .toBe('Saved to /tmp/deck.macroDeckProfile');
  });

  it('names the downloaded file in a browser', () => {
    expect(savedFileDetail({ fileName: 'deck.macroDeckProfile' })).toBe('Downloaded deck.macroDeckProfile');
  });

  it('has no detail without a file name or path', () => {
    expect(savedFileDetail({})).toBeUndefined();
  });
});
