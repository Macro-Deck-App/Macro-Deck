import { isInsideLiquidTag } from './liquid-context.util';

describe('isInsideLiquidTag', () => {
  it('is false in plain text', () => {
    expect(isInsideLiquidTag('Hello ', 6)).toBeFalse();
  });

  it('is true between the braces of a statement tag', () => {
    const value = '{% if  %}';
    expect(isInsideLiquidTag(value, '{% if '.length)).toBeTrue();
  });

  it('is true between the braces of an output tag', () => {
    const value = '{{  }}';
    expect(isInsideLiquidTag(value, 3)).toBeTrue();
  });

  it('is false again after the tag has closed', () => {
    const value = '{% if x %}text';
    expect(isInsideLiquidTag(value, value.length)).toBeFalse();
  });

  it('is false between two tags', () => {
    const value = '{% if x %} here {% endif %}';
    expect(isInsideLiquidTag(value, '{% if x %} her'.length)).toBeFalse();
  });

  it('handles a caret at the very start', () => {
    expect(isInsideLiquidTag('{% if %}', 0)).toBeFalse();
  });
});
