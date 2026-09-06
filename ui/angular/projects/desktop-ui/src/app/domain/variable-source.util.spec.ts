import type { Variable } from '@macro-deck/runtime';
import { groupVariablesBySource, integrationSourceEntries, isScopeLocal, matchesVariableSource } from './variable-source.util';
import { bundledTranslator as t } from '../../testing/localization-test-support';

function variable(overrides: Partial<Variable>): Variable {
  return {
    id: 'id',
    name: 'name',
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
    ...overrides,
  };
}

const userVar = variable({ id: 'u1', name: 'greeting' });
const spotifyVar = variable({
  id: 's1', name: 'is_playing', classification: 'integration', ownerIntegrationId: 'spotify',
});
const systemVar = variable({
  id: 'sys1', name: 'cpu', classification: 'integration', ownerIntegrationId: 'system',
});
const buttonVar = variable({
  id: 'b1', name: 'toggled', scope: 'widget', scopeRefId: 'w1', classification: 'widget',
});

const names: Record<string, string> = { spotify: 'Spotify', system: 'System' };
const nameOf = (id: string) => names[id] ?? id;

describe('variable-source.util', () => {
  it('detects scope-local variables', () => {
    expect(isScopeLocal(buttonVar)).toBeTrue();
    expect(isScopeLocal(userVar)).toBeFalse();
  });

  describe('matchesVariableSource', () => {
    it('matches everything for all', () => {
      for (const v of [userVar, spotifyVar, buttonVar]) {
        expect(matchesVariableSource(v, { kind: 'all' })).toBeTrue();
      }
    });

    it('matches only user-created globals for user', () => {
      expect(matchesVariableSource(userVar, { kind: 'user' })).toBeTrue();
      expect(matchesVariableSource(spotifyVar, { kind: 'user' })).toBeFalse();
      const localUser = variable({ scope: 'widget', scopeRefId: 'w1' });
      expect(matchesVariableSource(localUser, { kind: 'user' })).toBeFalse();
    });

    it('matches only scope-locals for scope', () => {
      expect(matchesVariableSource(buttonVar, { kind: 'scope' })).toBeTrue();
      expect(matchesVariableSource(userVar, { kind: 'scope' })).toBeFalse();
    });

    it('matches by owning integration', () => {
      expect(matchesVariableSource(spotifyVar, { kind: 'integration', integrationId: 'spotify' })).toBeTrue();
      expect(matchesVariableSource(spotifyVar, { kind: 'integration', integrationId: 'system' })).toBeFalse();
      expect(matchesVariableSource(userVar, { kind: 'integration', integrationId: 'spotify' })).toBeFalse();
    });
  });

  describe('groupVariablesBySource', () => {
    it('groups locals first, then user, then integrations alphabetically', () => {
      const groups = groupVariablesBySource(
        [systemVar, userVar, spotifyVar, buttonVar], 'This button', nameOf, t);

      expect(groups.map(g => g.key)).toEqual(
        ['scope', 'user', 'integration:spotify', 'integration:system']);
      expect(groups[0].label).toBe('This button');
      expect(groups[0].variables).toEqual([buttonVar]);
      expect(groups[2].label).toBe('Spotify');
      expect(groups[3].label).toBe('System');
    });

    it('omits empty groups', () => {
      const groups = groupVariablesBySource([userVar], 'This button', nameOf, t);

      expect(groups.map(g => g.key)).toEqual(['user']);
    });

    it('collects unowned non-user globals into an other group', () => {
      const stray = variable({ id: 'x', classification: 'widget' });

      const groups = groupVariablesBySource([stray], 'This button', nameOf, t);

      expect(groups.map(g => g.key)).toEqual(['other']);
    });

    it('groups a bound catalog variable under its owning integration', () => {
      // Where a variable came from is the integration; whether it was bound from a catalog or always
      // provided is not a source of its own, or an integration offering both would appear twice.
      const boundFromCatalog = variable({
        id: 'd1', classification: 'integration', ownerIntegrationId: 'spotify', dynamicResourceId: 'entity/light.kitchen/state',
      });

      const groups = groupVariablesBySource([spotifyVar, boundFromCatalog], 'This button', nameOf, t);

      expect(groups.map(g => g.key)).toEqual(['integration:spotify']);
      expect(groups[0].variables).toEqual([spotifyVar, boundFromCatalog]);
    });
  });

  describe('integrationSourceEntries', () => {
    it('counts global variables per providing integration, sorted by name', () => {
      const entries = integrationSourceEntries(
        [systemVar, spotifyVar, variable({ id: 's2', ownerIntegrationId: 'spotify', classification: 'integration' }), userVar, buttonVar],
        nameOf);

      expect(entries).toEqual([
        { integrationId: 'spotify', name: 'Spotify', count: 2 },
        { integrationId: 'system', name: 'System', count: 1 },
      ]);
    });

    it('ignores scope-local variables even when integration-owned', () => {
      const localOwned = variable({
        id: 'l1', scope: 'widget', scopeRefId: 'w1', ownerIntegrationId: 'spotify',
      });

      expect(integrationSourceEntries([localOwned], nameOf)).toEqual([]);
    });

    it('gives a catalog-only integration an entry of its own with a zero count', () => {
      expect(integrationSourceEntries([], nameOf, ['spotify'])).toEqual([
        { integrationId: 'spotify', name: 'Spotify', count: 0 },
      ]);
    });

    it('lists an integration that both provides variables and offers a catalog exactly once', () => {
      const entries = integrationSourceEntries([spotifyVar, systemVar], nameOf, ['spotify']);

      expect(entries).toEqual([
        { integrationId: 'spotify', name: 'Spotify', count: 1 },
        { integrationId: 'system', name: 'System', count: 1 },
      ]);
    });
  });
});
