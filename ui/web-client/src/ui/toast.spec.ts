import { dismissAllToasts, dismissToast, mountToastHost, showToast } from './toast';

describe('toasts', () => {
  let root: HTMLElement;

  const toasts = (): NodeListOf<HTMLElement> => root.querySelectorAll<HTMLElement>('.wc-toast');

  beforeEach(() => {
    jasmine.clock().install();
    root = document.createElement('div');
    document.body.appendChild(root);
    mountToastHost(root, { dismissLabel: 'Dismiss' });
  });

  afterEach(() => {
    dismissAllToasts();
    jasmine.clock().uninstall();
    root.remove();
  });

  it('shows what it was given', () => {
    showToast('Exported', { detail: '/tmp/deck.json' });

    expect(toasts().length).toBe(1);
    expect(toasts()[0].querySelector('.wc-toast-message')!.textContent).toBe('Exported');
    expect(toasts()[0].querySelector('.wc-toast-detail')!.textContent).toBe('/tmp/deck.json');
  });

  it('keeps several up at once, in the order they were shown', () => {
    showToast('First');
    showToast('Second');

    const messages: string[] = [];
    toasts().forEach(toast => messages.push(toast.querySelector('.wc-toast-message')!.textContent!));

    expect(messages).toEqual(['First', 'Second']);
  });

  it('takes a toast away once its time is up', () => {
    showToast('Exported');

    jasmine.clock().tick(4999);
    expect(toasts().length).toBe(1);

    jasmine.clock().tick(1);
    expect(toasts().length).toBe(0);
  });

  it('gives each toast its own clock', () => {
    showToast('First', { durationMs: 1000 });
    showToast('Second', { durationMs: 3000 });

    jasmine.clock().tick(1000);

    expect(toasts().length).toBe(1);
    expect(toasts()[0].querySelector('.wc-toast-message')!.textContent).toBe('Second');
  });

  it('keeps a toast with no duration until it is dismissed', () => {
    const id = showToast('Signed out', { durationMs: 0 });

    jasmine.clock().tick(60000);
    expect(toasts().length).toBe(1);

    dismissToast(id);
    expect(toasts().length).toBe(0);
  });

  it('dismisses only the toast it was asked to', () => {
    const first = showToast('First');
    showToast('Second');

    dismissToast(first);

    expect(toasts().length).toBe(1);
    expect(toasts()[0].querySelector('.wc-toast-message')!.textContent).toBe('Second');
  });

  it('leaves no timer behind that could remove a later toast', () => {
    const first = showToast('First');
    dismissToast(first);

    showToast('Second', { durationMs: 0 });
    jasmine.clock().tick(10000);

    expect(toasts().length).toBe(1);
  });

  it('dismisses a toast from its own control', () => {
    showToast('Exported');

    toasts()[0].querySelector<HTMLElement>('.wc-toast-dismiss')!.click();

    expect(toasts().length).toBe(0);
  });

  it('names the dismiss control with the text the host was mounted with', () => {
    showToast('Exported');

    expect(toasts()[0].querySelector('.wc-toast-dismiss')!.getAttribute('aria-label')).toBe('Dismiss');
  });

  it('announces the stack politely', () => {
    const host = root.querySelector('.wc-toasts')!;

    expect(host.getAttribute('role')).toBe('status');
    expect(host.getAttribute('aria-live')).toBe('polite');
  });
});
