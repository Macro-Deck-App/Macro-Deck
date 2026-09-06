export type UaFormFactor = 'phone' | 'tablet' | 'desktop';

export interface UserAgentDataLike {
  readonly platform?: string;
  readonly mobile?: boolean;
}

export interface UserAgentInfo {
  browser: string | null;
  platform: string | null;
  formFactor: UaFormFactor;
  proposedName: string;
}

const BROWSER_RULES: ReadonlyArray<{ name: string; pattern: RegExp }> = [
  { name: 'Edge', pattern: /Edg\/(\d+)/ },
  { name: 'Opera', pattern: /(?:OPR|Opera)\/(\d+)/ },
  { name: 'Firefox', pattern: /(?:Firefox|FxiOS)\/(\d+)/ },
  { name: 'Samsung Internet', pattern: /SamsungBrowser\/(\d+)/ },
  { name: 'Chrome', pattern: /(?:Chrome|CriOS)\/(\d+)/ },
  { name: 'Safari', pattern: /Safari\/(\d+)/ },
];

export function parseBrowser(userAgent: string | null | undefined): string | null {
  try {
    if (!userAgent) return null;
    for (const { name, pattern } of BROWSER_RULES) {
      const match = pattern.exec(userAgent);
      if (match) {
        return `${name} ${match[1]}`;
      }
    }
    return null;
  } catch {
    return null;
  }
}

// Prefers navigator.userAgentData.platform where it exists, since UA reduction does not spoof it.
// A modern iPad reports as Macintosh; maxTouchPoints > 1 is the only signal telling it from a Mac.
export function parsePlatform(
  userAgent: string | null | undefined,
  userAgentData?: UserAgentDataLike | null,
  maxTouchPoints = 0,
): string | null {
  try {
    const reportedPlatform = userAgentData?.platform?.trim();
    if (reportedPlatform) return reportedPlatform;

    if (!userAgent) return null;
    if (/Windows NT/.test(userAgent)) return 'Windows';
    if (/Android/.test(userAgent)) return 'Android';
    if (/iPhone|iPod/.test(userAgent)) return 'iOS';
    if (/iPad/.test(userAgent)) return 'iPadOS';
    if (/Macintosh/.test(userAgent)) return maxTouchPoints > 1 ? 'iPadOS' : 'macOS';
    if (/Linux/.test(userAgent)) return 'Linux';
    return null;
  } catch {
    return null;
  }
}

export function parseFormFactor(
  userAgent: string | null | undefined,
  userAgentData?: UserAgentDataLike | null,
  maxTouchPoints = 0,
): UaFormFactor {
  try {
    if (userAgentData?.mobile === true) return 'phone';

    const ua = userAgent ?? '';
    if (/iPad/.test(ua)) return 'tablet';
    if (/Macintosh/.test(ua) && maxTouchPoints > 1) return 'tablet';
    if (/Android/.test(ua) && !/Mobile/.test(ua)) return 'tablet';
    if (/Mobi|iPhone|iPod/.test(ua)) return 'phone';
    return 'desktop';
  } catch {
    return 'desktop';
  }
}

export function buildProposedName(
  userAgent: string | null | undefined,
  userAgentData?: UserAgentDataLike | null,
  maxTouchPoints = 0,
): string {
  const browser = parseBrowser(userAgent);
  const platform = parsePlatform(userAgent, userAgentData, maxTouchPoints);
  const formFactor = parseFormFactor(userAgent, userAgentData, maxTouchPoints);
  const formFactorLabel = formFactor.charAt(0).toUpperCase() + formFactor.slice(1);

  if (browser && platform) return `${browser} on ${platform} – ${formFactorLabel}`;
  if (browser) return `${browser} – ${formFactorLabel}`;
  if (platform) return `${platform} – ${formFactorLabel}`;
  return formFactorLabel;
}

export function collectUserAgentInfo(nav: Navigator = navigator): UserAgentInfo {
  try {
    const userAgent = nav.userAgent ?? '';
    const userAgentData = (nav as Navigator & { userAgentData?: UserAgentDataLike }).userAgentData;
    const maxTouchPoints = nav.maxTouchPoints ?? 0;
    return {
      browser: parseBrowser(userAgent),
      platform: parsePlatform(userAgent, userAgentData, maxTouchPoints),
      formFactor: parseFormFactor(userAgent, userAgentData, maxTouchPoints),
      proposedName: buildProposedName(userAgent, userAgentData, maxTouchPoints),
    };
  } catch {
    return { browser: null, platform: null, formFactor: 'desktop', proposedName: 'Desktop' };
  }
}
