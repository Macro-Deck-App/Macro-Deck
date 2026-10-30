import { createServer } from 'node:http';

// A local HTTP endpoint a flow's HTTP action can call: it records every request and answers with a
// status taken from the path (/status/500), 200 otherwise.
export async function startEchoServer() {
  const requests = [];
  const server = createServer((request, response) => {
    const chunks = [];
    request.on('data', chunk => chunks.push(chunk));
    request.on('end', () => {
      const body = Buffer.concat(chunks).toString('utf8');
      requests.push({ method: request.method, url: request.url, headers: request.headers, body });
      const match = /^\/status\/(\d{3})/.exec(request.url ?? '');
      response.writeHead(match ? Number(match[1]) : 200, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify({ echo: body }));
    });
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const { port } = server.address();
  return {
    url: path => `http://127.0.0.1:${port}${path}`,
    requests,
    close: () => new Promise(resolve => server.close(() => resolve())),
  };
}
