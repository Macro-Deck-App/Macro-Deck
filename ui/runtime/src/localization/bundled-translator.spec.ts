import { AppStrings } from './generated/app-strings';
import { bundledTranslator } from './bundled-translator';

describe('bundledTranslator', () => {
  it('resolves a key no deck client can render', () => {
    expect(bundledTranslator(AppStrings.ActionBuilder.AddAction)).not.toContain('[[');
  });
});
