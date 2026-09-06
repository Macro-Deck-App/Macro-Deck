export interface ServiceWorkerEnvironment {
  readonly isSecureContext: boolean;
  readonly navigator: { readonly serviceWorker?: unknown };
}

export function shouldRegisterServiceWorker(
  environment: ServiceWorkerEnvironment,
  devMode: boolean,
): boolean {
  return !devMode && environment.isSecureContext && Boolean(environment.navigator.serviceWorker);
}
