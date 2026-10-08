import { applyDocumentLanguage } from './document-language';

describe('applyDocumentLanguage', () => {
  it('lays the client out right to left in Arabic and back after switching away', () => {
    const root = document.createElement('html');

    applyDocumentLanguage(root, 'ar');
    expect(root.lang).toBe('ar');
    expect(root.dir).toBe('rtl');

    applyDocumentLanguage(root, 'ja');
    expect(root.lang).toBe('ja');
    expect(root.dir).toBe('ltr');
  });
});
