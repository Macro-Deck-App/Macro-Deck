import { createErrorBanner } from './error-banner';

describe('error banner', () => {
  it('takes itself off the page when it is dismissed, and says so', () => {
    const root = document.createElement('div');
    let dismissals = 0;
    const banner = createErrorBanner({
      message: 'The host refused the connection.',
      dismissLabel: 'Dismiss',
      onDismiss: () => dismissals++,
    });
    root.appendChild(banner.element);

    banner.element.querySelector<HTMLElement>('.wc-error-banner-dismiss')!.click();

    expect(root.children.length).toBe(0);
    expect(dismissals).toBe(1);
  });

  it('offers no dismiss control when it was given no name for one', () => {
    const banner = createErrorBanner({ message: 'The host refused the connection.' });

    expect(banner.element.querySelector('.wc-error-banner-dismiss')).toBeNull();
  });

  it('names the dismiss control with the text it was given', () => {
    const banner = createErrorBanner({ message: 'Nope', dismissLabel: 'Schließen' });

    const dismiss = banner.element.querySelector('.wc-error-banner-dismiss')!;
    expect(dismiss.getAttribute('aria-label')).toBe('Schließen');
  });

  it('announces itself as an alert', () => {
    const banner = createErrorBanner({ message: 'Nope' });

    expect(banner.element.getAttribute('role')).toBe('alert');
  });
});
