import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export const SECRET_HEADER = 'X-MacroDeck-Loopback-Secret';

const developmentSecretFile = fileURLToPath(new URL('../../.data/config/loopback-secret', import.meta.url));

// Read per request: the dev host writes a new secret every time it starts.
export function loopbackSecret(env = process.env) {
	if (env.MACRODECK_LOOPBACK_SECRET) {
		return env.MACRODECK_LOOPBACK_SECRET.trim();
	}
	try {
		return readFileSync(env.MACRODECK_LOOPBACK_SECRET_FILE || developmentSecretFile, 'utf8').trim();
	} catch {
		return null;
	}
}

// No changeOrigin: the host only trusts a loopback Host header, which keeps an ng serve bound to a
// LAN address from handing the secret's trust to other machines.
export function proxyConfig(target, env = process.env) {
	const configure = (proxy) => {
		const present = (proxyRequest) => {
			const secret = loopbackSecret(env);
			if (secret) {
				proxyRequest.setHeader(SECRET_HEADER, secret);
			}
		};
		proxy.on('proxyReq', present);
		proxy.on('proxyReqWs', present);
	};

	return {
		'/ws': { target, ws: true, configure },
		'/api': { target, configure },
	};
}

export default proxyConfig('http://127.0.0.1:5191');
