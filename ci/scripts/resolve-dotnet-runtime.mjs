import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const allowedUrlPrefix = 'https://builds.dotnet.microsoft.com/';

function channelOf(version) {
  const match = /^(\d+)\.(\d+)\./.exec(version ?? '');
  if (!match) {
    throw new Error(`not a .NET runtime version: ${version}`);
  }
  return `${match[1]}.${match[2]}`;
}

function archiveName(rid) {
  return rid.startsWith('win-') ? `aspnetcore-runtime-${rid}.zip` : `aspnetcore-runtime-${rid}.tar.gz`;
}

export function resolveDotnetRuntimes(config, releasesByChannel, sdkRuntimeVersion, rid) {
  const channels = config?.channels ?? [];
  const hostChannel = channelOf(sdkRuntimeVersion);
  if (!channels.includes(hostChannel)) {
    throw new Error(`the SDK runtime channel ${hostChannel} is not listed in the bundled runtime channels`);
  }

  return channels.map((channel) => {
    const document = releasesByChannel[channel];
    if (!document) {
      throw new Error(`no release metadata for channel ${channel}`);
    }
    const wanted = channel === hostChannel ? sdkRuntimeVersion : document['latest-runtime'];
    const release = (document.releases ?? []).find((entry) => entry.runtime?.version === wanted);
    if (!release) {
      throw new Error(`channel ${channel} has no release with runtime ${wanted}`);
    }
    const name = archiveName(rid);
    const file = (release['aspnetcore-runtime']?.files ?? []).find((entry) => entry.name === name);
    if (!file) {
      throw new Error(`runtime ${wanted} has no ${name}`);
    }
    if (typeof file.url !== 'string' || !file.url.startsWith(allowedUrlPrefix)) {
      throw new Error(`runtime ${wanted} archive url is not on ${allowedUrlPrefix}: ${file.url}`);
    }
    if (typeof file.hash !== 'string' || !/^[0-9a-fA-F]{128}$/.test(file.hash)) {
      throw new Error(`runtime ${wanted} archive has a malformed sha512: ${file.hash}`);
    }
    return { channel, version: wanted, url: file.url, sha512: file.hash.toLowerCase() };
  });
}

async function main([configPath, sdkRuntimeVersion, rid]) {
  if (!configPath || !sdkRuntimeVersion || !rid) {
    throw new Error('usage: resolve-dotnet-runtime.mjs <config.json> <sdkRuntimeVersion> <rid>');
  }
  const config = JSON.parse(readFileSync(configPath, 'utf8'));
  const releasesByChannel = {};
  for (const channel of config.channels ?? []) {
    const url = `https://builds.dotnet.microsoft.com/dotnet/release-metadata/${channel}/releases.json`;
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`GET ${url} failed: ${response.status}`);
    }
    releasesByChannel[channel] = await response.json();
  }
  for (const archive of resolveDotnetRuntimes(config, releasesByChannel, sdkRuntimeVersion, rid)) {
    console.log(`${archive.version}\t${archive.url}\t${archive.sha512}`);
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.cause ? `${error.message}: ${error.cause.message ?? error.cause}` : error.message);
    process.exit(1);
  });
}
