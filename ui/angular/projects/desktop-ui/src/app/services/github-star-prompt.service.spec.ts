import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetGitHubStarPromptResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import {
  GITHUB_STAR_PROMPT_MAX_RETRIES,
  GITHUB_STAR_PROMPT_RETRY_MS,
  GitHubStarPromptService,
} from './github-star-prompt.service';

describe('GitHubStarPromptService', () => {
  let connection: ReturnType<typeof signal<string>>;
  let getPrompt: jasmine.Spy<() => Promise<GetGitHubStarPromptResponse>>;
  let markShown: jasmine.Spy;

  beforeEach(() => {
    jasmine.clock().install();
    connection = signal('connected');
    getPrompt = jasmine.createSpy('getGitHubStarPrompt').and.resolveTo({ due: true });
    markShown = jasmine.createSpy('markGitHubStarPromptShown').and.resolveTo(undefined);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            onNotification: () => EMPTY,
            connectionStateSignal: connection,
            getGitHubStarPrompt: getPrompt,
            markGitHubStarPromptShown: markShown,
          },
        },
      ],
    });
  });

  afterEach(() => jasmine.clock().uninstall());

  async function start(): Promise<GitHubStarPromptService> {
    const service = TestBed.inject(GitHubStarPromptService);
    TestBed.tick();
    await flush();
    return service;
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
  }

  async function reconnect(): Promise<void> {
    connection.set('disconnected');
    TestBed.tick();
    connection.set('connected');
    TestBed.tick();
    await flush();
  }

  it('asks the host once connected and becomes pending when the prompt is due', async () => {
    const service = await start();

    expect(getPrompt).toHaveBeenCalledTimes(1);
    expect(service.pending()).toBeTrue();
  });

  it('stays quiet while the prompt is not due and asks again only on the next connection', async () => {
    getPrompt.and.resolveTo({ due: false });
    const service = await start();
    jasmine.clock().tick(24 * 60 * 60 * 1000);
    await flush();

    expect(service.pending()).toBeFalse();
    expect(getPrompt).toHaveBeenCalledTimes(1);

    getPrompt.and.resolveTo({ due: true });
    await reconnect();

    expect(getPrompt).toHaveBeenCalledTimes(2);
    expect(service.pending()).toBeTrue();
  });

  it('retries a failed check a bounded number of times', async () => {
    getPrompt.and.rejectWith(new Error('offline'));
    spyOn(console, 'error');
    await start();

    for (let i = 0; i < GITHUB_STAR_PROMPT_MAX_RETRIES + 3; i++) {
      jasmine.clock().tick(GITHUB_STAR_PROMPT_RETRY_MS);
      await flush();
    }

    expect(getPrompt).toHaveBeenCalledTimes(GITHUB_STAR_PROMPT_MAX_RETRIES + 1);
  });

  it('records the prompt as shown once and never offers it again in this session', async () => {
    const service = await start();

    service.markShown();
    service.markShown();
    service.dismiss();
    await reconnect();

    expect(markShown).toHaveBeenCalledTimes(1);
    expect(getPrompt).toHaveBeenCalledTimes(1);
    expect(service.pending()).toBeFalse();
  });
});
