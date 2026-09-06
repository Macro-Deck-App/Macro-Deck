import { computed, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { GetMusicPlayerInstancesResponse, MusicPlayerInstanceDto, MusicPlayerInstancesChangedNotification, MusicPlayerStateChangedNotification, MusicPlayerStatePayload } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { DISCONNECTED_STATE, MusicPlayerService } from './music-player.service';

describe('MusicPlayerService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState: BehaviorSubject<ConnectionState>;
  let stateEvents: Subject<MusicPlayerStateChangedNotification>;
  let instanceEvents: Subject<MusicPlayerInstancesChangedNotification>;
  let service: MusicPlayerService;

  const SPOTIFY = 'app.macro-deck.spotify::entry';

  function instance(id: string): MusicPlayerInstanceDto {
    return {
      instanceId: id,
      integrationId: 'app.macro-deck.spotify',
      providerName: 'Spotify',
      displayName: id,
      hasIcon: true,
    };
  }

  function playing(id: string, trackName: string): MusicPlayerStatePayload {
    return {
      instanceId: id,
      isConnected: true,
      playbackState: 'playing',
      isPlaying: true,
      trackName,
      shuffleEnabled: false,
      repeatMode: 'off',
    };
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      await new Promise<void>(resolve => setTimeout(resolve, 0));
    }
  }

  beforeEach(() => {
    connectionState = new BehaviorSubject<ConnectionState>('connected');
    stateEvents = new Subject<MusicPlayerStateChangedNotification>();
    instanceEvents = new Subject<MusicPlayerInstancesChangedNotification>();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'getMusicPlayerInstances', 'getMusicPlayerState', 'getMusicPlayerArtworkUrl'],
      { connectionState$: connectionState.asObservable() },
    );
    apiSpy.onNotification.and.callFake(
      (name: string) =>
        (name === 'MusicPlayerInstancesChangedNotification'
          ? instanceEvents.asObservable()
          : stateEvents.asObservable()) as Observable<never>,
    );
    apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [] });
    apiSpy.getMusicPlayerState.and.resolveTo(null);
    apiSpy.getMusicPlayerArtworkUrl.and.callFake(
      (instanceId: string, artworkId: string, size?: number) =>
        `/api/music-player/artwork/${artworkId}?instanceId=${instanceId}` + (size ? `&size=${size}` : ''),
    );

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(MusicPlayerService);
  });

  describe('artworkUrl', () => {
    it('returns null when the instance or artwork id is missing', () => {
      expect(service.artworkUrl(undefined, 'art')).toBeNull();
      expect(service.artworkUrl('instance', null)).toBeNull();
      expect(service.artworkUrl('instance', undefined)).toBeNull();
      expect(apiSpy.getMusicPlayerArtworkUrl).not.toHaveBeenCalled();
    });

    it('delegates to the api url builder without a size by default', () => {
      expect(service.artworkUrl('spotify::default', 'abc')).toBe(
        '/api/music-player/artwork/abc?instanceId=spotify::default',
      );
      expect(apiSpy.getMusicPlayerArtworkUrl).toHaveBeenCalledWith('spotify::default', 'abc', undefined);
    });

    it('passes the requested rendition size through', () => {
      expect(service.artworkUrl('spotify::default', 'abc', 128)).toBe(
        '/api/music-player/artwork/abc?instanceId=spotify::default&size=128',
      );
      expect(apiSpy.getMusicPlayerArtworkUrl).toHaveBeenCalledWith('spotify::default', 'abc', 128);
    });
  });

  describe('stateFor', () => {
    it('falls back to the disconnected state for unknown instances', () => {
      expect(service.stateFor('unknown')).toBe(DISCONNECTED_STATE);
      expect(service.stateFor(undefined)).toBe(DISCONNECTED_STATE);
    });

    // Regression test for issue #457, finding 4: stateFor used to read one shared `states` record
    // signal, so a computed depending on a single instance's state re-evaluated on every push to any
    // instance - a busy connected player invalidated every other widget's computed on the page.
    it('does not invalidate a computed reading one instance when another instance changes', async () => {
      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance('a'), instance('b')] });
      apiSpy.getMusicPlayerState.and.callFake(async (id: string) => ({ state: playing(id, id === 'a' ? 'Track A' : 'Track B') }));
      service.start();
      await settle();

      let evaluations = 0;
      const trackNameA = computed(() => {
        evaluations++;
        return service.stateFor('a').trackName;
      });
      expect(trackNameA()).toBe('Track A');
      expect(evaluations).toBe(1);
      const beforeA = service.stateFor('a');

      stateEvents.next({ state: playing('b', 'Track B2') });

      // Read first, then assert the count: a computed is lazy, so an invalidated one only
      // re-evaluates when it is read. Asserting the count before the read would pass even when
      // instance b's push did invalidate this computed.
      expect(trackNameA()).toBe('Track A');
      expect(evaluations).toBe(1);
      expect(service.stateFor('a')).toBe(beforeA);
      expect(service.stateFor('b').trackName).toBe('Track B2');
    });
  });

  describe('unknown instance ids', () => {
    it('returns the disconnected state by reference and never mints a per-lookup entry', () => {
      const first = service.stateFor('never-seen');
      expect(first).toBe(DISCONNECTED_STATE);
      expect(service.stateFor('never-seen')).toBe(DISCONNECTED_STATE);
      expect(service.stateFor(undefined)).toBe(DISCONNECTED_STATE);
      expect(service.stateFor(null)).toBe(DISCONNECTED_STATE);
      expect(service.stateFor('never-seen')).toBe(first);
    });
  });

  describe('instance list', () => {
    it('adopts an instance that appears after the connection was established', async () => {
      service.start();
      await settle();
      expect(service.instances()).toEqual([]);

      instanceEvents.next({ instances: [instance(SPOTIFY)] });

      expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
    });

    it('keeps the previous instances when getMusicPlayerInstances returns null', async () => {
      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance(SPOTIFY)] });
      await service.loadInstances();

      apiSpy.getMusicPlayerInstances.and.resolveTo(null);
      await service.loadInstances();

      expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
    });

    it('clears the instances on an explicit empty response', async () => {
      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance(SPOTIFY)] });
      await service.loadInstances();

      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [] });
      await service.loadInstances();

      expect(service.instances()).toEqual([]);
    });

    it('single-flights concurrent instance loads', async () => {
      await Promise.all([service.loadInstances(), service.loadInstances(), service.loadInstances()]);

      expect(apiSpy.getMusicPlayerInstances).toHaveBeenCalledTimes(1);
    });

    it('does not let a stale empty pull overwrite a list pushed while it was in flight', async () => {
      let resolvePull: (value: GetMusicPlayerInstancesResponse | null) => void = () => undefined;
      apiSpy.getMusicPlayerInstances.and.returnValue(
        new Promise<GetMusicPlayerInstancesResponse | null>(resolve => (resolvePull = resolve)),
      );
      service.start();

      const pull = service.loadInstances();
      instanceEvents.next({ instances: [instance(SPOTIFY)] });
      resolvePull({ instances: [] });
      await pull;
      await settle();

      expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
    });

    it('reloads once when a state arrives for an instance it does not know', async () => {
      service.start();
      await settle();
      apiSpy.getMusicPlayerInstances.calls.reset();

      stateEvents.next({ state: playing(SPOTIFY, 'One') });
      await settle();
      stateEvents.next({ state: playing(SPOTIFY, 'Two') });
      stateEvents.next({ state: playing(SPOTIFY, 'Three') });
      await settle();

      // A list that cannot be repaired must not re-invoke on every push.
      expect(apiSpy.getMusicPlayerInstances).toHaveBeenCalledTimes(1);
    });

    it('re-attempts the heal after a reload that failed', async () => {
      service.start();
      await settle();
      apiSpy.getMusicPlayerInstances.calls.reset();

      // The reload triggered by the unknown instance fails (hub hiccup). That must not permanently
      // disable healing for the id - the next push has to be allowed to try again.
      apiSpy.getMusicPlayerInstances.and.resolveTo(null);
      stateEvents.next({ state: playing(SPOTIFY, 'One') });
      await settle();

      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance(SPOTIFY)] });
      stateEvents.next({ state: playing(SPOTIFY, 'Two') });
      await settle();

      expect(apiSpy.getMusicPlayerInstances.calls.count()).toBeGreaterThanOrEqual(2);
      expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
    });

    it('retries a failed instances pull and applies the eventual answer', async () => {
      jasmine.clock().install();
      try {
        apiSpy.getMusicPlayerInstances.and.resolveTo(null);
        await service.loadInstances();

        apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance(SPOTIFY)] });
        jasmine.clock().tick(1001);
        for (let i = 0; i < 5; i++) {
          await Promise.resolve();
        }

        expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('abandons a scheduled retry once a push supplied a fresher list', async () => {
      jasmine.clock().install();
      try {
        service.start();
        for (let i = 0; i < 5; i++) {
          await Promise.resolve();
        }

        apiSpy.getMusicPlayerInstances.and.resolveTo(null);
        await service.loadInstances();
        apiSpy.getMusicPlayerInstances.calls.reset();

        instanceEvents.next({ instances: [instance(SPOTIFY)] });
        jasmine.clock().tick(9000);
        for (let i = 0; i < 5; i++) {
          await Promise.resolve();
        }

        expect(apiSpy.getMusicPlayerInstances).not.toHaveBeenCalled();
        expect(service.instances().map(i => i.instanceId)).toEqual([SPOTIFY]);
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('state', () => {
    it('retries the state pull after a failed invoke', async () => {
      await service.ensureState(SPOTIFY);
      expect(service.stateFor(SPOTIFY)).toBe(DISCONNECTED_STATE);

      apiSpy.getMusicPlayerState.and.resolveTo({ state: playing(SPOTIFY, 'Song') });
      await service.ensureState(SPOTIFY);

      expect(service.stateFor(SPOTIFY).trackName).toBe('Song');
    });

    it('does not re-pull a state it already has', async () => {
      apiSpy.getMusicPlayerState.and.resolveTo({ state: playing(SPOTIFY, 'Song') });
      await service.ensureState(SPOTIFY);
      await service.ensureState(SPOTIFY);

      expect(apiSpy.getMusicPlayerState).toHaveBeenCalledTimes(1);
    });

    it('re-pulls every known state on a reconnect', async () => {
      apiSpy.getMusicPlayerInstances.and.resolveTo({ instances: [instance(SPOTIFY)] });
      apiSpy.getMusicPlayerState.and.resolveTo({ state: playing(SPOTIFY, 'Song') });
      service.start();
      await settle();
      expect(service.stateFor(SPOTIFY).trackName).toBe('Song');
      apiSpy.getMusicPlayerState.calls.reset();

      apiSpy.getMusicPlayerState.and.resolveTo({ state: playing(SPOTIFY, 'Another') });
      connectionState.next('connecting');
      connectionState.next('connected');
      await settle();

      expect(apiSpy.getMusicPlayerState).toHaveBeenCalledWith(SPOTIFY);
      expect(service.stateFor(SPOTIFY).trackName).toBe('Another');
    });
  });
});
