import { createInput } from './input';

const type = (input: HTMLInputElement, value: string): void => {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
};

describe('input', () => {
  it('reports what was typed', () => {
    const seen: string[] = [];
    const input = createInput({ onChange: value => seen.push(value) });

    type(input.element, 'ma');
    type(input.element, 'macro');

    expect(seen).toEqual(['ma', 'macro']);
  });

  it('does not report a value the caller wrote', () => {
    let reports = 0;
    const input = createInput({ onChange: () => reports++ });

    input.setValue('macro');

    expect(reports).toBe(0);
    expect(input.value()).toBe('macro');
  });

  it('answers Enter with the submit callback only while it is enabled', () => {
    let submits = 0;
    const input = createInput({ onSubmit: () => submits++ });
    const enter = () => input.element.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));

    enter();
    input.setDisabled(true);
    enter();

    expect(submits).toBe(1);
  });
});
