import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ClipboardCopyResult, TextClipboardService, clipboardFailureDetail } from './text-clipboard.service';

// window.isSecureContext and navigator.clipboard are inherited accessors, so spyOnProperty()
// cannot stub them - it only works on own properties. Object.defineProperty() shadows the
// prototype accessor with an own property; delete restores the original.
function setSecureContext(value: boolean): void {
  Object.defineProperty(window, 'isSecureContext', { value, configurable: true });
}

function setClipboardApi(writeText: jasmine.Spy | undefined): void {
  Object.defineProperty(navigator, 'clipboard', {
    value: writeText ? { writeText } : undefined,
    configurable: true,
  });
}

describe('TextClipboardService', () => {
  let service: TextClipboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), TextClipboardService],
    });
    service = TestBed.inject(TextClipboardService);
  });

  afterEach(() => {
    delete (window as { isSecureContext?: unknown }).isSecureContext;
    delete (navigator as { clipboard?: unknown }).clipboard;
    delete (document as { execCommand?: unknown }).execCommand;
  });

  it('uses the Clipboard API when it is available on a secure context', async () => {
    setSecureContext(true);
    const writeText = jasmine.createSpy('writeText').and.resolveTo(undefined);
    setClipboardApi(writeText);
    const execSpy = spyOn(document, 'execCommand');

    const result = await service.copyText('icon-1');

    expect(result).toEqual({ status: 'copied', via: 'clipboard-api' });
    expect(writeText).toHaveBeenCalledWith('icon-1');
    expect(execSpy).not.toHaveBeenCalled();
  });

  it('falls back to execCommand when the Clipboard API is unavailable on an insecure origin', async () => {
    setSecureContext(false);
    setClipboardApi(undefined);
    const execSpy = spyOn(document, 'execCommand').and.returnValue(true);

    const result = await service.copyText('icon-1');

    expect(result).toEqual({ status: 'copied', via: 'exec-command' });
    expect(execSpy).toHaveBeenCalledWith('copy');
  });

  it('falls back to execCommand when the Clipboard API is unavailable even on a secure origin', async () => {
    setSecureContext(true);
    setClipboardApi(undefined);
    const execSpy = spyOn(document, 'execCommand').and.returnValue(true);

    const result = await service.copyText('icon-1');

    expect(result).toEqual({ status: 'copied', via: 'exec-command' });
    expect(execSpy).toHaveBeenCalledWith('copy');
  });

  it('falls back to execCommand when the Clipboard API rejects, resolving instead of throwing', async () => {
    setSecureContext(true);
    const writeText = jasmine.createSpy('writeText')
      .and.rejectWith(new DOMException('The request is not allowed', 'NotAllowedError'));
    setClipboardApi(writeText);
    const execSpy = spyOn(document, 'execCommand').and.returnValue(true);

    const result = await service.copyText('icon-1');

    expect(result).toEqual({ status: 'copied', via: 'exec-command' });
    expect(execSpy).toHaveBeenCalledWith('copy');
  });

  describe('fallback textarea DOM contract', () => {
    it('places a textarea with the exact value in the DOM only for the duration of the call', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      let seenDuringCall: HTMLTextAreaElement | null = null;
      spyOn(document, 'execCommand').and.callFake(() => {
        seenDuringCall = document.body.querySelector('textarea');
        return true;
      });

      await service.copyText('icon-xyz');

      expect(seenDuringCall).not.toBeNull();
      expect(seenDuringCall!.value).toBe('icon-xyz');
      expect(document.body.querySelector('textarea')).toBeNull();
    });

    it('restores focus to the previously focused element', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      spyOn(document, 'execCommand').and.returnValue(true);
      const button = document.createElement('button');
      document.body.appendChild(button);
      button.focus();

      await service.copyText('icon-1');

      expect(document.activeElement).toBe(button);
      button.remove();
    });

    it('preserves a pre-existing user selection elsewhere on the page', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      spyOn(document, 'execCommand').and.returnValue(true);

      const paragraph = document.createElement('p');
      paragraph.textContent = 'existing selection text';
      document.body.appendChild(paragraph);
      const range = document.createRange();
      range.selectNodeContents(paragraph);
      const selection = document.getSelection();
      selection?.removeAllRanges();
      selection?.addRange(range);

      await service.copyText('icon-1');

      expect(document.getSelection()?.toString()).toBe('existing selection text');
      paragraph.remove();
    });
  });

  describe('complete failure', () => {
    it('reports insecure-context when insecure and execCommand also fails', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      spyOn(document, 'execCommand').and.returnValue(false);

      const result = await service.copyText('icon-1');

      expect(result).toEqual({ status: 'failed', reason: 'insecure-context' });
    });

    it('reports unsupported when secure but the Clipboard API is missing and execCommand fails', async () => {
      setSecureContext(true);
      setClipboardApi(undefined);
      spyOn(document, 'execCommand').and.returnValue(false);

      const result = await service.copyText('icon-1');

      expect(result).toEqual({ status: 'failed', reason: 'unsupported' });
    });

    it('reports denied when the Clipboard API rejects and execCommand also fails', async () => {
      setSecureContext(true);
      const writeText = jasmine.createSpy('writeText')
        .and.rejectWith(new DOMException('The request is not allowed', 'NotAllowedError'));
      setClipboardApi(writeText);
      spyOn(document, 'execCommand').and.returnValue(false);

      const result = await service.copyText('icon-1');

      expect(result).toEqual({ status: 'failed', reason: 'denied' });
    });

    it('resolves to failed instead of throwing when execCommand itself throws', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      spyOn(document, 'execCommand').and.throwError('boom');

      const result = await service.copyText('icon-1');

      expect(result).toEqual({ status: 'failed', reason: 'insecure-context' });
    });

    it('resolves to failed without a TypeError when document.execCommand is entirely undefined', async () => {
      setSecureContext(false);
      setClipboardApi(undefined);
      Object.defineProperty(document, 'execCommand', { value: undefined, configurable: true });

      const result = await service.copyText('icon-1');

      expect(result).toEqual({ status: 'failed', reason: 'insecure-context' });
    });
  });

  it('calls execCommand synchronously, before the returned promise settles (regression guard for the strategy order)', async () => {
    setSecureContext(false);
    setClipboardApi(undefined);
    const execCommandSpy = spyOn(document, 'execCommand').and.returnValue(true);

    const promise = service.copyText('icon-1');
    expect(execCommandSpy).toHaveBeenCalled();
    await promise;
  });

  it('falls back to an explicit Range when select() leaves the selection empty (iOS Safari)', async () => {
    setSecureContext(false);
    setClipboardApi(undefined);
    spyOn(document, 'execCommand').and.returnValue(true);
    const addRangeSpy = jasmine.createSpy('addRange');
    spyOn(document, 'getSelection').and.returnValue({
      rangeCount: 0,
      getRangeAt: () => { throw new Error('no range'); },
      removeAllRanges: () => {},
      addRange: addRangeSpy,
      toString: () => '',
    } as unknown as Selection);

    const result: ClipboardCopyResult = await service.copyText('icon-1');

    expect(result).toEqual({ status: 'copied', via: 'exec-command' });
    expect(addRangeSpy).toHaveBeenCalled();
  });
});

describe('clipboardFailureDetail', () => {
  it('describes an insecure context', () => {
    expect(clipboardFailureDetail('insecure-context'))
      .toBe('Your browser only allows copying on a secure (HTTPS) connection.');
  });

  it('describes a missing Clipboard API', () => {
    expect(clipboardFailureDetail('unsupported'))
      .toBe('Your browser does not allow this page to copy to the clipboard.');
  });

  it('describes a denied permission', () => {
    expect(clipboardFailureDetail('denied')).toBe('Clipboard access was denied by your browser.');
  });
});
