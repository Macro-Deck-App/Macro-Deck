import { InjectionToken } from '@angular/core';

export type HostUrlResolver = () => Promise<string | null>;

export const HOST_URL_RESOLVER = new InjectionToken<HostUrlResolver>('HOST_URL_RESOLVER');
