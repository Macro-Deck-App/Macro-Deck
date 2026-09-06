import { Provider } from '@angular/core';

import { HOST_URL_RESOLVER } from '../transport/host-url';

export function provideLocalizationTesting(): Provider[] {
  return [{ provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' }];
}

export { bundledTranslator } from '@macro-deck/runtime';
