import { createButton } from './button';

describe('button', () => {
  it('runs the action on a press', () => {
    let presses = 0;
    const button = createButton({ label: 'Save', onClick: () => presses++ });

    button.element.click();

    expect(presses).toBe(1);
  });

  it('ignores a press while it is disabled', () => {
    let presses = 0;
    const button = createButton({ label: 'Save', disabled: true, onClick: () => presses++ });

    button.element.click();
    button.element.dispatchEvent(new Event('click'));

    expect(presses).toBe(0);
  });

  it('ignores a press while it is loading', () => {
    let presses = 0;
    const button = createButton({ label: 'Save', onClick: () => presses++ });

    button.setLoading(true);
    button.element.click();
    button.element.dispatchEvent(new Event('click'));

    expect(presses).toBe(0);
  });

  it('takes presses again once the loading is over', () => {
    let presses = 0;
    const button = createButton({ label: 'Save', onClick: () => presses++ });

    button.setLoading(true);
    button.setLoading(false);
    button.element.click();

    expect(presses).toBe(1);
  });

  it('reports a loading button as busy rather than as one that refuses', () => {
    const button = createButton({ label: 'Save' });

    button.setLoading(true);

    expect(button.element.getAttribute('aria-busy')).toBe('true');

    button.setLoading(false);

    expect(button.element.hasAttribute('aria-busy')).toBeFalse();
  });

  it('stays disabled while both states asked for it', () => {
    const button = createButton({ label: 'Save', disabled: true, loading: true });

    button.setLoading(false);

    expect(button.element.disabled).toBeTrue();
  });
});
