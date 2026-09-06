import { ApplicationConfig, inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, withHashLocation } from '@angular/router';

import { HOST_URL_RESOLVER, AUTH_REQUIRED_SCOPE, CLIENT_TYPE, provideVersionCheck, UiWidgetResourceBaseUrl } from '@shared';
import { TEMPLATE_PREVIEW_SERVICE } from './domain/template-preview.interface';
import { TemplatePreviewApiService } from './services/template-preview.service';
import { routes } from './app.routes';
import { provideWidgetRegistry } from './widget-registration';

async function resolveHostUrl(): Promise<string | null> {
  return window.location.origin;
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes, withHashLocation()),
    provideWidgetRegistry(),
    { provide: HOST_URL_RESOLVER, useValue: resolveHostUrl },
    { provide: AUTH_REQUIRED_SCOPE, useValue: 'admin' as const },
    // Device classification (issue #250): the loopback desktop UI never calls login() and so never
    // registers a device - this only matters for a remote admin login over the public port.
    { provide: CLIENT_TYPE, useValue: 'admin-ui' as const },
    { provide: TEMPLATE_PREVIEW_SERVICE, useExisting: TemplatePreviewApiService },
    provideVersionCheck(),
    // Resolves the host base URL before the first widget tree ever renders, so a folder switch does
    // not turn every button/slider artwork into a cold fetch behind an async resolve (#748).
    provideAppInitializer(() => inject(UiWidgetResourceBaseUrl).get()),
  ]
};
