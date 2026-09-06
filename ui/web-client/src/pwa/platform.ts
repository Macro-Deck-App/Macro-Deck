export interface PlatformNavigator {
  readonly userAgent: string;
  readonly platform?: string;
  readonly maxTouchPoints?: number;
}

export type Platform = 'ios' | 'android' | 'desktop';

export function detectPlatform(nav: PlatformNavigator): Platform {
  const ua = nav.userAgent;
  if (/iPhone|iPod|iPad/.test(ua)) {
    return 'ios';
  }
  const touchPoints = typeof nav.maxTouchPoints === 'number' ? nav.maxTouchPoints : 0;
  if (nav.platform === 'MacIntel' && touchPoints > 1) {
    return 'ios';
  }
  if (/Android/.test(ua)) {
    return 'android';
  }
  return 'desktop';
}
