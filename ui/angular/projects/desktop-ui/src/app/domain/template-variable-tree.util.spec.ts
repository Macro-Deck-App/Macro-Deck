import type { Variable } from '@macro-deck/runtime';
import { buildVariableTree, countVariables, filterVariableTree, variableLabelFor } from './template-variable-tree.util';
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

const names: Record<string, string> = { obs: 'OBS Studio', spotify: 'Spotify' };
const nameOf = (id: string) => names[id] ?? id;

describe('template-variable-tree.util', () => {
  describe('buildVariableTree', () => {
    it('sub-groups a provider by configurationKey with subgroup labels', () => {
      const mac = variable({
        id: 'o1', name: 'cpu_mac', classification: 'integration', ownerIntegrationId: 'obs',
        configurationKey: 'mac', configurationName: 'Mac',
      });
      const pc = variable({
        id: 'o2', name: 'cpu_pc', classification: 'integration', ownerIntegrationId: 'obs',
        configurationKey: 'win', configurationName: 'Windows PC',
      });
      const resolveConfigName = (v: Variable): string | null =>
        typeof v.configurationName === 'string' && v.configurationName ? v.configurationName : null;

      const tree = buildVariableTree([mac, pc], 'This widget', nameOf, resolveConfigName, t);

      expect(tree).toHaveSize(1);
      expect(tree[0].key).toBe('integration:obs');
      expect(tree[0].subgroups.map(s => s.label)).toEqual(['Mac', 'Windows PC']);
    });

    it('counterexample: a provider with no configurationKey renders exactly one single-level group', () => {
      const a = variable({ id: 'o1', name: 'a', classification: 'integration', ownerIntegrationId: 'obs' });
      const b = variable({ id: 'o2', name: 'b', classification: 'integration', ownerIntegrationId: 'obs' });

      const tree = buildVariableTree([a, b], 'This widget', nameOf, () => null, t);

      expect(tree).toHaveSize(1);
      expect(tree[0].subgroups).toHaveSize(1);
      // The single subgroup carries no label at all, so the tree renders one level; a synthesized
      // placeholder would show up here as a second heading under the provider.
      expect(tree[0].subgroups[0].label).toBe('');
      const subgroupLabels = tree.flatMap(g => g.subgroups.map(s => s.label));
      expect(subgroupLabels).not.toContain('Default');
      expect(subgroupLabels).not.toContain('General');
      expect(subgroupLabels).not.toContain('Unnamed');
    });

    it('mixed within one provider: config-less variables survive in a leading unlabelled subgroup', () => {
      const noConfig = variable({ id: 'o1', name: 'a', classification: 'integration', ownerIntegrationId: 'obs' });
      const withConfig = variable({
        id: 'o2', name: 'b', classification: 'integration', ownerIntegrationId: 'obs',
        configurationKey: 'mac', configurationName: 'Mac',
      });
      const resolveConfigName = (v: Variable): string | null =>
        typeof v.configurationName === 'string' && v.configurationName ? v.configurationName : null;

      const tree = buildVariableTree([noConfig, withConfig], 'This widget', nameOf, resolveConfigName, t);

      expect(tree[0].subgroups[0].label).toBe('');
      expect(tree[0].subgroups[0].variables).toEqual([noConfig]);
      expect(tree[0].subgroups[1].label).toBe('Mac');
      expect(tree[0].subgroups[1].variables).toEqual([withConfig]);
    });

    it('a configurationKey with no resolvable label falls back to the raw key', () => {
      const v = variable({
        id: 'o1', name: 'a', classification: 'integration', ownerIntegrationId: 'obs', configurationKey: 'unit-42',
      });

      const tree = buildVariableTree([v], 'This widget', nameOf, () => null, t);

      expect(tree[0].subgroups[0].label).toBe('unit-42');
    });

    it('orders integration groups by display name, not id', () => {
      const zulu = variable({ id: 'z1', name: 'z', classification: 'integration', ownerIntegrationId: 'zulu-id' });
      const alpha = variable({ id: 'a1', name: 'a', classification: 'integration', ownerIntegrationId: 'alpha-id' });
      const displayNames: Record<string, string> = { 'zulu-id': 'Alpha Provider', 'alpha-id': 'Zulu Provider' };

      const tree = buildVariableTree([zulu, alpha], 'This widget', id => displayNames[id] ?? id, () => null, t);

      expect(tree.map(g => g.label)).toEqual(['Alpha Provider', 'Zulu Provider']);
    });
  });

  describe('variableLabelFor', () => {
    it('display name primary, vars.<identifier> secondary', () => {
      const v = variable({ name: 'now_playing_uri' });

      expect(variableLabelFor(v, 'Track title')).toEqual({ primary: 'Track title', secondary: 'vars.now_playing_uri' });
    });

    it('counterexample: no display name -> primary is the identifier, secondary is null', () => {
      const v = variable({ name: 'now_playing_uri' });

      expect(variableLabelFor(v, '')).toEqual({ primary: 'vars.now_playing_uri', secondary: null });
    });

    it('an event-origin variable qualifies as event.<name>', () => {
      const v = variable({ name: 'volume', origin: 'event' });

      expect(variableLabelFor(v, '').primary).toBe('event.volume');
    });
  });

  describe('search (filterVariableTree)', () => {
    const spotifyVar = variable({
      id: 's1', name: 'now_playing_uri', classification: 'integration', ownerIntegrationId: 'spotify',
    });
    const cpuVar = variable({
      id: 'c1', name: 'obs_mac_cpu_usage', classification: 'integration', ownerIntegrationId: 'obs',
    });
    const displayNames: Record<string, string> = { now_playing_uri: 'Track title', obs_mac_cpu_usage: 'Processor load' };
    const displayNameOf = (v: Variable): string => displayNames[v.name] ?? '';

    function tree(): ReturnType<typeof buildVariableTree> {
      return buildVariableTree([spotifyVar, cpuVar], 'This widget', nameOf, () => null, t);
    }

    it('matches by identifier alone', () => {
      const filtered = filterVariableTree(tree(), 'uri', displayNameOf);
      expect(countVariables(filtered)).toBe(1);
      expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([spotifyVar]);
    });

    it('matches by display name alone', () => {
      const filtered = filterVariableTree(tree(), 'processor', displayNameOf);
      expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([cpuVar]);
    });

    it('matches by provider alone, even though the provider name is in neither identifier nor display name', () => {
      const filtered = filterVariableTree(tree(), 'spotify', displayNameOf);
      expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([spotifyVar]);
    });

    it('is case-insensitive and substring, not prefix', () => {
      for (const query of ['CPU', 'cpu', 'mac_cpu']) {
        const filtered = filterVariableTree(tree(), query, displayNameOf);
        expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([cpuVar]);
      }
    });

    it('matches the qualified form a row actually shows, including event.<name>', () => {
      const eventVar = variable({ id: 'e1', name: 'channel', origin: 'event' });
      const eventTree = buildVariableTree([eventVar], 'This widget', nameOf, () => null, t);

      const filtered = filterVariableTree(eventTree, 'event.chan', () => '');

      expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([eventVar]);
    });

    it('matches a configuration label, keeping that whole subgroup', () => {
      const macVar = variable({
        id: 'm1', name: 'cpu', classification: 'integration', ownerIntegrationId: 'obs',
        configurationKey: 'mac', configurationName: 'Living room',
      });
      const pcVar = variable({
        id: 'p1', name: 'cpu2', classification: 'integration', ownerIntegrationId: 'obs',
        configurationKey: 'win', configurationName: 'Gaming PC',
      });
      const configTree = buildVariableTree([macVar, pcVar], 'This widget', nameOf,
        v => (typeof v.configurationName === 'string' ? v.configurationName : null), t);

      const filtered = filterVariableTree(configTree, 'living', () => '');

      expect(filtered.flatMap(g => g.subgroups.flatMap(s => s.variables))).toEqual([macVar]);
    });

    it('a query matching nothing yields an empty tree', () => {
      const filtered = filterVariableTree(tree(), 'no-such-thing', displayNameOf);
      expect(countVariables(filtered)).toBe(0);
    });

    it('an empty query returns the tree unchanged', () => {
      expect(filterVariableTree(tree(), '', displayNameOf)).toEqual(tree());
    });
  });

  describe('countVariables', () => {
    it('sums across every group and subgroup', () => {
      const a = variable({ id: 'a1', name: 'a' });
      const b = variable({ id: 'b1', name: 'b', classification: 'integration', ownerIntegrationId: 'obs' });
      const tree = buildVariableTree([a, b], 'This widget', nameOf, () => null, t);

      expect(countVariables(tree)).toBe(2);
    });
  });
});
