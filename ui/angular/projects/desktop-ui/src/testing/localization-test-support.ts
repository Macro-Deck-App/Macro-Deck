import { Provider } from '@angular/core';
import { HOST_URL_RESOLVER } from '@shared';

export function provideLocalizationTesting(): Provider[] {
  return [{ provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' }];
}

// Resolves a key against the bundled catalogue; it needs no Angular, so it lives in the runtime and
// both projects use the one implementation.
export { bundledTranslator } from '@macro-deck/runtime';
