// The Docs workflow builds the released docs from production and the beta docs from main.
export const channel = process.env.DOCS_CHANNEL === 'beta' ? 'beta' : 'stable';

export const stableSite = 'https://docs.macro-deck.app';

export const betaSite = 'https://beta.docs.macro-deck.app';

export const site = channel === 'beta' ? betaSite : stableSite;

export const sourceBranch = channel === 'beta' ? 'main' : 'production';
