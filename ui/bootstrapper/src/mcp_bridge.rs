use std::ffi::OsString;
use std::future::Future;
use std::io::{BufRead, Write};
use std::path::PathBuf;
use std::pin::Pin;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use reqwest::header::{ACCEPT, AUTHORIZATION, CONTENT_TYPE};
use reqwest::StatusCode;
use serde_json::{json, Value};
use tokio::sync::mpsc;
use tokio::task::JoinHandle;

use crate::host;
use crate::localization::{self, keys};
use crate::loopback_secret;

pub const SUBCOMMAND: &str = "mcp";
pub const TOKEN_ENVIRONMENT_VARIABLE: &str = "MACRODECK_MCP_TOKEN";
const PORT_OVERRIDE_ENVIRONMENT_VARIABLE: &str = "MACRODECK_HOST_PORT";
const SECRET_FILE_OVERRIDE_ENVIRONMENT_VARIABLE: &str = "MACRODECK_LOOPBACK_SECRET_FILE";
const ENDPOINT_PATH: &str = "/mcp";
const SESSION_HEADER: &str = "Mcp-Session-Id";
const PROTOCOL_HEADER: &str = "MCP-Protocol-Version";
const ACCEPTED_TYPES: &str = "application/json, text/event-stream";
const PROOF_TIMEOUT: Duration = Duration::from_secs(2);
const CONNECT_TIMEOUT: Duration = Duration::from_secs(5);
const CLOSE_TIMEOUT: Duration = Duration::from_secs(2);
const BRIDGE_ERROR_CODE: i64 = -32000;
const PARSE_ERROR_CODE: i64 = -32700;
const REINITIALIZE_ID: &str = "macro-deck-bridge-reinitialize";
const PRODUCTION_IDENTIFIER: &str = "app.macro-deck.macrodeck";
const DEVELOPMENT_IDENTIFIER: &str = "app.macro-deck.macrodeck.development";

pub fn is_invocation(args: impl IntoIterator<Item = OsString>) -> bool {
    args.into_iter().nth(1).is_some_and(|arg| arg == SUBCOMMAND)
}

pub fn run() -> i32 {
    let Some(token) = std::env::var(TOKEN_ENVIRONMENT_VARIABLE)
        .ok()
        .map(|token| token.trim().to_string())
        .filter(|token| !token.is_empty())
    else {
        report(&localization::t_args(
            keys::MCP_TOKEN_MISSING,
            &[("variable", TOKEN_ENVIRONMENT_VARIABLE)],
        ));
        return 2;
    };
    let client = match http_client() {
        Ok(client) => client,
        Err(error) => {
            report(&format!("could not create the HTTP client: {error}"));
            return 1;
        }
    };
    let locate: Locate = Arc::new(|| Box::pin(locate_installed_host()));
    let bridge = Bridge::new(client, token, locate, stdout_output());
    tauri::async_runtime::block_on(serve(bridge, stdin_lines()));
    0
}

// Proxy variables must not apply: every request goes to 127.0.0.1 and carries the bearer token.
fn http_client() -> reqwest::Result<reqwest::Client> {
    reqwest::Client::builder()
        .no_proxy()
        .connect_timeout(CONNECT_TIMEOUT)
        .build()
}

fn report(message: &str) {
    eprintln!("Macro Deck MCP bridge: {message}");
}

fn stdout_output() -> Output {
    Arc::new(|line: String| {
        let mut out = std::io::stdout().lock();
        let _ = writeln!(out, "{line}");
        let _ = out.flush();
    })
}

// A plain thread, because tokio's stdin support is not among the enabled features.
fn stdin_lines() -> mpsc::UnboundedReceiver<String> {
    let (sender, receiver) = mpsc::unbounded_channel();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            let Ok(line) = line else {
                break;
            };
            if sender.send(line).is_err() {
                break;
            }
        }
    });
    receiver
}

async fn serve(bridge: Arc<Bridge>, mut lines: mpsc::UnboundedReceiver<String>) {
    while let Some(line) = lines.recv().await {
        bridge.handle_line(&line).await;
    }
    bridge.close().await;
}

pub fn identifier(build_channel: &str) -> &'static str {
    match build_channel {
        "Development" => DEVELOPMENT_IDENTIFIER,
        "Production" => PRODUCTION_IDENTIFIER,
        _ => unreachable!("build channel is validated by build.rs"),
    }
}

type Environment<'a> = &'a dyn Fn(&str) -> Option<OsString>;

// Mirrors Tauri's app_config_dir. Claude Desktop starts the bridge with only a few variables
// (HOME, PATH and the like, no TMPDIR), so nothing here may depend on the temp directory.
pub fn config_dir(build_channel: &str, environment: Environment) -> Option<PathBuf> {
    base_config_dir(environment).map(|base| base.join(identifier(build_channel)))
}

#[cfg(target_os = "macos")]
fn base_config_dir(environment: Environment) -> Option<PathBuf> {
    absolute(environment("HOME")).map(|home| home.join("Library/Application Support"))
}

#[cfg(target_os = "linux")]
fn base_config_dir(environment: Environment) -> Option<PathBuf> {
    absolute(environment("XDG_CONFIG_HOME"))
        .or_else(|| absolute(environment("HOME")).map(|home| home.join(".config")))
}

#[cfg(windows)]
fn base_config_dir(_environment: Environment) -> Option<PathBuf> {
    use windows::Win32::System::Com::CoTaskMemFree;
    use windows::Win32::UI::Shell::{
        FOLDERID_RoamingAppData, SHGetKnownFolderPath, KF_FLAG_DEFAULT,
    };

    unsafe {
        let path = SHGetKnownFolderPath(&FOLDERID_RoamingAppData, KF_FLAG_DEFAULT, None).ok()?;
        let result = path.to_string().ok().map(PathBuf::from);
        CoTaskMemFree(Some(path.0 as *const _));
        result
    }
}

#[cfg(not(windows))]
fn absolute(value: Option<OsString>) -> Option<PathBuf> {
    value.map(PathBuf::from).filter(|path| path.is_absolute())
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HostSource {
    Configured { port: u16, secret_file: PathBuf },
    Installed { config_dir: PathBuf },
}

// The override pair serves a host that runs without the launcher (development, tests). It is never
// trusted on its own: the port still has to prove that it holds that secret.
pub fn host_source(build_channel: &str, environment: Environment) -> Option<HostSource> {
    let port = environment(PORT_OVERRIDE_ENVIRONMENT_VARIABLE)
        .and_then(|value| value.into_string().ok())
        .and_then(|value| host::parse_port(&value));
    let secret_file = environment(SECRET_FILE_OVERRIDE_ENVIRONMENT_VARIABLE).map(PathBuf::from);
    if let (Some(port), Some(secret_file)) = (port, secret_file) {
        return Some(HostSource::Configured { port, secret_file });
    }
    config_dir(build_channel, environment).map(|config_dir| HostSource::Installed { config_dir })
}

// Port and secret are read again on every call: the launcher may pick another port and always
// writes a new secret when it starts the host.
pub async fn locate(source: &HostSource, timeout: Duration) -> Result<u16, Failure> {
    let (port, secret) = match source {
        HostSource::Configured { port, secret_file } => {
            (Some(*port), loopback_secret::load_file(secret_file))
        }
        HostSource::Installed { config_dir } => (
            host::read_persisted_port(config_dir),
            loopback_secret::load(config_dir),
        ),
    };
    let (Some(port), Some(secret)) = (port, secret) else {
        return Err(Failure::NotRunning);
    };
    if loopback_secret::prove_with(&secret, port, timeout).await {
        Ok(port)
    } else {
        Err(Failure::NotRunning)
    }
}

async fn locate_installed_host() -> Result<u16, Failure> {
    let environment = |name: &str| std::env::var_os(name);
    let source = host_source(host::BUILD_CHANNEL, &environment).ok_or(Failure::NotRunning)?;
    locate(&source, PROOF_TIMEOUT).await
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Failure {
    NotRunning,
    Unauthorized,
    Unavailable,
    Status(u16),
    Lost,
    NotInitialized,
}

impl Failure {
    pub fn message(&self) -> String {
        match self {
            Failure::NotRunning => localization::t(keys::MCP_NOT_RUNNING),
            Failure::Unauthorized => localization::t(keys::MCP_TOKEN_REJECTED),
            Failure::Unavailable => localization::t(keys::MCP_UNAVAILABLE),
            Failure::Status(status) => {
                localization::t_args(keys::MCP_REFUSED, &[("status", &status.to_string())])
            }
            Failure::Lost => localization::t(keys::MCP_CONNECTION_LOST),
            Failure::NotInitialized => localization::t(keys::MCP_NOT_INITIALIZED),
        }
    }
}

pub type Output = Arc<dyn Fn(String) + Send + Sync>;
pub type Locate =
    Arc<dyn Fn() -> Pin<Box<dyn Future<Output = Result<u16, Failure>> + Send>> + Send + Sync>;

#[derive(Debug, Clone)]
struct Target {
    port: u16,
    session_id: Option<String>,
    protocol_version: Option<String>,
    generation: u64,
}

#[derive(Default)]
struct Session {
    initialize: Option<Value>,
    port: Option<u16>,
    session_id: Option<String>,
    protocol_version: Option<String>,
    ready: bool,
    generation: u64,
    stream: Option<JoinHandle<()>>,
}

enum Exchange {
    Done {
        session_id: Option<String>,
        response: Option<Value>,
    },
    NeverReached(Failure),
    Failed(Failure),
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Sink {
    Client,
    Discard,
}

pub struct Bridge {
    client: reqwest::Client,
    token: String,
    locate: Locate,
    output: Output,
    session: Mutex<Session>,
    reinitializing: tokio::sync::Mutex<()>,
}

impl Bridge {
    pub fn new(
        client: reqwest::Client,
        token: String,
        locate: Locate,
        output: Output,
    ) -> Arc<Self> {
        Arc::new(Bridge {
            client,
            token,
            locate,
            output,
            session: Mutex::new(Session::default()),
            reinitializing: tokio::sync::Mutex::new(()),
        })
    }

    // Requests run concurrently: a tool call can stay open while the host sends an elicitation that
    // only the client's next line answers. Only initialize is awaited, it has to finish first.
    pub async fn handle_line(self: &Arc<Self>, line: &str) {
        let line = line.trim();
        if line.is_empty() {
            return;
        }
        let Ok(message) = serde_json::from_str::<Value>(line) else {
            (self.output)(
                error_response(&Value::Null, PARSE_ERROR_CODE, "Parse error").to_string(),
            );
            return;
        };
        if method(&message) == Some("initialize") {
            Arc::clone(self).forward(message).await;
        } else {
            tokio::spawn(Arc::clone(self).forward(message));
        }
    }

    pub async fn close(&self) {
        let target = self.current_target();
        if let Some(stream) = self.lock().stream.take() {
            stream.abort();
        }
        let Some(Target {
            port,
            session_id: Some(session_id),
            ..
        }) = target
        else {
            return;
        };
        let _ = self
            .client
            .delete(endpoint(port))
            .header(AUTHORIZATION, self.bearer())
            .header(SESSION_HEADER, session_id)
            .timeout(CLOSE_TIMEOUT)
            .send()
            .await;
    }

    async fn forward(self: Arc<Self>, message: Value) {
        let request_id = request_id(&message);
        let method = method(&message).map(str::to_string);
        let initialize = method.as_deref() == Some("initialize");
        if initialize {
            self.begin_initialize(&message);
        }
        let mut replayed = false;
        loop {
            let target = if initialize {
                self.fresh_target().await
            } else {
                self.ready_target().await
            };
            let target = match target {
                Ok(target) => target,
                Err(failure) => {
                    self.fail(request_id.as_ref(), &failure);
                    return;
                }
            };
            match self
                .exchange(&target, &message, request_id.as_ref(), Sink::Client)
                .await
            {
                Exchange::Done {
                    session_id,
                    response,
                } => {
                    if initialize {
                        self.finish_initialize(&target, session_id, response.as_ref());
                    } else if method.as_deref() == Some("notifications/initialized") {
                        self.start_stream(target);
                    }
                    return;
                }
                // Safe to send again only because the host provably never saw this request.
                Exchange::NeverReached(_) if !replayed && !initialize => {
                    replayed = true;
                    self.invalidate(target.generation);
                }
                Exchange::NeverReached(failure) | Exchange::Failed(failure) => {
                    if failure == Failure::Lost {
                        self.invalidate(target.generation);
                    }
                    self.fail(request_id.as_ref(), &failure);
                    return;
                }
            }
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Session> {
        self.session
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn bearer(&self) -> String {
        format!("Bearer {}", self.token)
    }

    fn begin_initialize(&self, message: &Value) {
        let mut session = self.lock();
        if let Some(stream) = session.stream.take() {
            stream.abort();
        }
        session.initialize = Some(message.clone());
        session.ready = false;
        session.generation += 1;
    }

    async fn fresh_target(&self) -> Result<Target, Failure> {
        let port = (self.locate)().await?;
        let generation = self.lock().generation;
        Ok(Target {
            port,
            session_id: None,
            protocol_version: None,
            generation,
        })
    }

    fn finish_initialize(
        &self,
        target: &Target,
        session_id: Option<String>,
        response: Option<&Value>,
    ) {
        let mut session = self.lock();
        if session.generation != target.generation {
            return;
        }
        session.port = Some(target.port);
        session.session_id = session_id;
        session.protocol_version = response.and_then(protocol_version);
        session.ready = response.is_some_and(|response| response.get("result").is_some());
    }

    fn current_target(&self) -> Option<Target> {
        let session = self.lock();
        match (session.ready, session.port) {
            (true, Some(port)) => Some(Target {
                port,
                session_id: session.session_id.clone(),
                protocol_version: session.protocol_version.clone(),
                generation: session.generation,
            }),
            _ => None,
        }
    }

    fn invalidate(&self, generation: u64) {
        let mut session = self.lock();
        if session.generation == generation {
            session.ready = false;
            if let Some(stream) = session.stream.take() {
                stream.abort();
            }
        }
    }

    async fn ready_target(self: &Arc<Self>) -> Result<Target, Failure> {
        if let Some(target) = self.current_target() {
            return Ok(target);
        }
        let _guard = self.reinitializing.lock().await;
        if let Some(target) = self.current_target() {
            return Ok(target);
        }
        self.reinitialize().await
    }

    async fn reinitialize(self: &Arc<Self>) -> Result<Target, Failure> {
        let (initialize, generation) = {
            let session = self.lock();
            (session.initialize.clone(), session.generation)
        };
        let Some(mut initialize) = initialize else {
            return Err(Failure::NotInitialized);
        };
        initialize["id"] = json!(REINITIALIZE_ID);
        let port = (self.locate)().await?;
        let probe = Target {
            port,
            session_id: None,
            protocol_version: None,
            generation,
        };
        let reinitialize_id = json!(REINITIALIZE_ID);
        let (session_id, response) = match self
            .exchange(&probe, &initialize, Some(&reinitialize_id), Sink::Discard)
            .await
        {
            Exchange::Done {
                session_id,
                response,
            } => (session_id, response),
            Exchange::NeverReached(failure) | Exchange::Failed(failure) => return Err(failure),
        };
        let Some(response) = response.filter(|response| response.get("result").is_some()) else {
            return Err(Failure::Unavailable);
        };
        let target = {
            let mut session = self.lock();
            if session.generation != generation {
                return Err(Failure::NotInitialized);
            }
            session.generation += 1;
            session.port = Some(port);
            session.session_id = session_id.clone();
            session.protocol_version = protocol_version(&response);
            session.ready = true;
            Target {
                port,
                session_id,
                protocol_version: session.protocol_version.clone(),
                generation: session.generation,
            }
        };
        let initialized = json!({ "jsonrpc": "2.0", "method": "notifications/initialized" });
        match self
            .exchange(&target, &initialized, None, Sink::Discard)
            .await
        {
            Exchange::Done { .. } => {
                self.start_stream(target.clone());
                Ok(target)
            }
            Exchange::NeverReached(failure) | Exchange::Failed(failure) => {
                self.invalidate(target.generation);
                Err(failure)
            }
        }
    }

    fn start_stream(self: &Arc<Self>, target: Target) {
        if target.session_id.is_none() {
            return;
        }
        let bridge = Arc::clone(self);
        let generation = target.generation;
        let handle = tokio::spawn(async move { bridge.listen(target).await });
        let mut session = self.lock();
        if session.generation != generation {
            handle.abort();
            return;
        }
        if let Some(previous) = session.stream.replace(handle) {
            previous.abort();
        }
    }

    async fn listen(&self, target: Target) {
        let request = self.authorized(self.client.get(endpoint(target.port)), &target);
        let Ok(mut response) = request.header(ACCEPT, "text/event-stream").send().await else {
            return;
        };
        if !response.status().is_success() {
            return;
        }
        let mut parser = SseParser::default();
        while let Ok(Some(chunk)) = response.chunk().await {
            for payload in parser.feed(&chunk) {
                if let Ok(message) = serde_json::from_str::<Value>(&payload) {
                    (self.output)(message.to_string());
                }
            }
        }
    }

    fn authorized(
        &self,
        request: reqwest::RequestBuilder,
        target: &Target,
    ) -> reqwest::RequestBuilder {
        let mut request = request.header(AUTHORIZATION, self.bearer());
        if let Some(session_id) = &target.session_id {
            request = request.header(SESSION_HEADER, session_id);
        }
        if let Some(version) = &target.protocol_version {
            request = request.header(PROTOCOL_HEADER, version);
        }
        request
    }

    async fn exchange(
        &self,
        target: &Target,
        message: &Value,
        request_id: Option<&Value>,
        sink: Sink,
    ) -> Exchange {
        let request = self
            .authorized(self.client.post(endpoint(target.port)), target)
            .header(ACCEPT, ACCEPTED_TYPES)
            .json(message);
        let mut response = match request.send().await {
            Ok(response) => response,
            Err(error) if error.is_connect() => return Exchange::NeverReached(Failure::NotRunning),
            // reqwest cannot tell whether the body went out before the reset, so never replay.
            Err(_) => return Exchange::Failed(Failure::Lost),
        };
        let status = response.status();
        if status == StatusCode::NOT_FOUND && target.session_id.is_some() {
            return Exchange::NeverReached(Failure::Unavailable);
        }
        if status == StatusCode::UNAUTHORIZED {
            return Exchange::Failed(Failure::Unauthorized);
        }
        if status == StatusCode::NOT_FOUND {
            return Exchange::Failed(Failure::Unavailable);
        }
        if !status.is_success() {
            return Exchange::Failed(Failure::Status(status.as_u16()));
        }
        let session_id = response
            .headers()
            .get(SESSION_HEADER)
            .and_then(|value| value.to_str().ok())
            .map(str::to_string);
        let event_stream = response
            .headers()
            .get(CONTENT_TYPE)
            .and_then(|value| value.to_str().ok())
            .is_some_and(|value| value.starts_with("text/event-stream"));
        let mut answer = None;
        if event_stream {
            let mut parser = SseParser::default();
            'stream: while let Ok(Some(chunk)) = response.chunk().await {
                for payload in parser.feed(&chunk) {
                    if let Some(message) = self.deliver(&payload, request_id, sink) {
                        answer = Some(message);
                        break 'stream;
                    }
                }
            }
        } else if let Ok(body) = response.text().await {
            if !body.trim().is_empty() {
                answer = self.deliver(&body, request_id, sink);
            }
        }
        if request_id.is_some() && answer.is_none() {
            return Exchange::Failed(Failure::Lost);
        }
        Exchange::Done {
            session_id,
            response: answer,
        }
    }

    fn deliver(&self, payload: &str, request_id: Option<&Value>, sink: Sink) -> Option<Value> {
        let message = serde_json::from_str::<Value>(payload).ok()?;
        if sink == Sink::Client {
            (self.output)(message.to_string());
        }
        let answers = request_id
            .is_some_and(|id| message.get("method").is_none() && message.get("id") == Some(id));
        answers.then_some(message)
    }

    fn fail(&self, request_id: Option<&Value>, failure: &Failure) {
        let message = failure.message();
        report(&message);
        if let Some(id) = request_id {
            (self.output)(error_response(id, BRIDGE_ERROR_CODE, &message).to_string());
        }
    }
}

fn endpoint(port: u16) -> String {
    format!("http://127.0.0.1:{port}{ENDPOINT_PATH}")
}

fn method(message: &Value) -> Option<&str> {
    message.get("method").and_then(Value::as_str)
}

fn request_id(message: &Value) -> Option<Value> {
    method(message)?;
    message.get("id").filter(|id| !id.is_null()).cloned()
}

fn protocol_version(response: &Value) -> Option<String> {
    response
        .get("result")?
        .get("protocolVersion")?
        .as_str()
        .map(str::to_string)
}

fn error_response(id: &Value, code: i64, message: &str) -> Value {
    json!({ "jsonrpc": "2.0", "id": id, "error": { "code": code, "message": message } })
}

#[derive(Default)]
pub struct SseParser {
    pending: Vec<u8>,
    data: Vec<String>,
}

impl SseParser {
    pub fn feed(&mut self, chunk: &[u8]) -> Vec<String> {
        self.pending.extend_from_slice(chunk);
        let mut events = Vec::new();
        while let Some(end) = self.pending.iter().position(|byte| *byte == b'\n') {
            let mut line: Vec<u8> = self.pending.drain(..=end).collect();
            line.pop();
            if line.last() == Some(&b'\r') {
                line.pop();
            }
            let line = String::from_utf8_lossy(&line).into_owned();
            if line.is_empty() {
                if !self.data.is_empty() {
                    events.push(self.data.join("\n"));
                    self.data.clear();
                }
            } else if let Some(value) = line.strip_prefix("data:") {
                self.data
                    .push(value.strip_prefix(' ').unwrap_or(value).to_string());
            }
        }
        events
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;
    use std::io::{BufReader, Read};
    use std::net::{TcpListener, TcpStream};
    use std::path::Path;
    use std::sync::atomic::{AtomicUsize, Ordering};

    const TOKEN: &str = "mdmcp_test-token-value";
    const SECRET: &str = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    const OTHER_SECRET: &str = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

    struct Request {
        method: String,
        headers: HashMap<String, String>,
        body: String,
    }

    impl Request {
        fn json(&self) -> Value {
            serde_json::from_str(&self.body).unwrap_or(Value::Null)
        }

        fn rpc_method(&self) -> String {
            self.json()["method"]
                .as_str()
                .unwrap_or_default()
                .to_string()
        }

        fn header(&self, name: &str) -> Option<&str> {
            self.headers
                .get(&name.to_ascii_lowercase())
                .map(String::as_str)
        }
    }

    fn read_request(stream: &mut TcpStream) -> Option<Request> {
        let mut reader = BufReader::new(stream.try_clone().ok()?);
        let mut line = String::new();
        reader.read_line(&mut line).ok()?;
        let method = line.split_whitespace().next()?.to_string();
        let mut headers = HashMap::new();
        loop {
            let mut header = String::new();
            reader.read_line(&mut header).ok()?;
            let header = header.trim_end();
            if header.is_empty() {
                break;
            }
            let (name, value) = header.split_once(':')?;
            headers.insert(name.trim().to_ascii_lowercase(), value.trim().to_string());
        }
        let length = headers
            .get("content-length")
            .and_then(|value| value.parse::<usize>().ok())
            .unwrap_or(0);
        let mut body = vec![0u8; length];
        reader.read_exact(&mut body).ok()?;
        Some(Request {
            method,
            headers,
            body: String::from_utf8_lossy(&body).into_owned(),
        })
    }

    fn respond_json(stream: &mut TcpStream, session: Option<&str>, body: &Value) {
        let body = body.to_string();
        let session = session
            .map(|id| format!("Mcp-Session-Id: {id}\r\n"))
            .unwrap_or_default();
        let _ = write!(
            stream,
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n{session}Content-Length: {}\r\nConnection: close\r\n\r\n{body}",
            body.len()
        );
    }

    fn respond_status(stream: &mut TcpStream, status: &str) {
        let _ = write!(
            stream,
            "HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
        );
    }

    fn open_event_stream(stream: &mut TcpStream) {
        let _ = write!(
            stream,
            "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"
        );
        let _ = stream.flush();
    }

    fn send_event(stream: &mut TcpStream, message: &Value) {
        let _ = write!(stream, "event: message\r\ndata: {message}\r\n\r\n");
        let _ = stream.flush();
    }

    fn initialize_result(id: &Value) -> Value {
        json!({ "jsonrpc": "2.0", "id": id, "result": { "protocolVersion": "2025-06-18", "capabilities": {}, "serverInfo": { "name": "macro-deck", "version": "1" } } })
    }

    fn serve(handler: impl Fn(Request, TcpStream) + Send + Sync + 'static) -> u16 {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let handler = Arc::new(handler);
        std::thread::spawn(move || {
            for stream in listener.incoming() {
                let Ok(mut stream) = stream else {
                    break;
                };
                let handler = Arc::clone(&handler);
                std::thread::spawn(move || {
                    if let Some(request) = read_request(&mut stream) {
                        handler(request, stream);
                    }
                });
            }
        });
        port
    }

    fn fixed_port(port: u16) -> Locate {
        Arc::new(move || Box::pin(async move { Ok(port) }))
    }

    fn collecting() -> (Output, mpsc::UnboundedReceiver<Value>) {
        let (sender, receiver) = mpsc::unbounded_channel();
        let output: Output = Arc::new(move |line: String| {
            let _ = sender.send(serde_json::from_str(&line).unwrap());
        });
        (output, receiver)
    }

    fn bridge(locate: Locate, output: Output) -> Arc<Bridge> {
        Bridge::new(http_client().unwrap(), TOKEN.to_string(), locate, output)
    }

    async fn next(receiver: &mut mpsc::UnboundedReceiver<Value>) -> Value {
        tokio::time::timeout(Duration::from_secs(10), receiver.recv())
            .await
            .expect("no message from the bridge")
            .expect("output closed")
    }

    async fn initialize(bridge: &Arc<Bridge>, receiver: &mut mpsc::UnboundedReceiver<Value>) {
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{"elicitation":{}},"clientInfo":{"name":"test","version":"1"}}}"#)
            .await;
        assert!(next(receiver).await["result"].is_object());
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","method":"notifications/initialized"}"#)
            .await;
    }

    #[test]
    fn only_the_mcp_subcommand_selects_the_bridge() {
        let args = |list: &[&str]| list.iter().map(OsString::from).collect::<Vec<_>>();
        assert!(is_invocation(args(&["MacroDeck", "mcp"])));
        assert!(!is_invocation(args(&["MacroDeck"])));
        assert!(!is_invocation(args(&["MacroDeck", "macrodeck://open"])));
        assert!(!is_invocation(args(&["MacroDeck", "--flag", "mcp"])));
    }

    #[test]
    fn identifiers_match_the_tauri_configs_of_each_channel() {
        let read = |name: &str| -> Value {
            let path = Path::new(env!("CARGO_MANIFEST_DIR")).join(name);
            serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap()
        };
        assert_eq!(
            read("tauri.conf.json")["identifier"],
            identifier("Development")
        );
        assert_eq!(
            read("release.conf.json")["identifier"],
            identifier("Production")
        );
    }

    #[cfg(not(windows))]
    #[test]
    fn the_config_dir_needs_no_temp_directory_and_differs_per_channel() {
        let environment = |name: &str| (name == "HOME").then(|| OsString::from("/home/someone"));
        let production = config_dir("Production", &environment).unwrap();
        let development = config_dir("Development", &environment).unwrap();
        assert!(production.starts_with("/home/someone"));
        assert!(production.ends_with(PRODUCTION_IDENTIFIER));
        assert!(development.ends_with(DEVELOPMENT_IDENTIFIER));
        assert_ne!(production, development);
        assert_eq!(
            host_source("Production", &environment),
            Some(HostSource::Installed {
                config_dir: production
            })
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn the_override_pair_selects_the_host_but_it_still_has_to_prove_the_secret() {
        let port = proof_host(Arc::new(Mutex::new(SECRET.to_string())));
        let matching = tempdir::Dir::new();
        loopback_secret::persist(matching.path(), SECRET).unwrap();
        let other = tempdir::Dir::new();
        loopback_secret::persist(other.path(), OTHER_SECRET).unwrap();
        let source_for = |secret_dir: &Path| {
            let port = OsString::from(port.to_string());
            let secret_file = loopback_secret::persisted_path(secret_dir).into_os_string();
            let environment = move |name: &str| match name {
                PORT_OVERRIDE_ENVIRONMENT_VARIABLE => Some(port.clone()),
                SECRET_FILE_OVERRIDE_ENVIRONMENT_VARIABLE => Some(secret_file.clone()),
                "HOME" => Some(OsString::from("/home/someone")),
                _ => None,
            };
            host_source("Production", &environment)
        };

        let accepted = source_for(matching.path()).unwrap();
        let refused = source_for(other.path()).unwrap();
        assert_eq!(
            accepted,
            HostSource::Configured {
                port,
                secret_file: loopback_secret::persisted_path(matching.path()),
            }
        );
        assert_eq!(locate(&accepted, PROOF_TIMEOUT).await, Ok(port));
        assert_eq!(
            locate(&refused, PROOF_TIMEOUT).await,
            Err(Failure::NotRunning)
        );
    }

    #[cfg(not(windows))]
    #[test]
    fn half_an_override_pair_is_ignored() {
        let environment = |name: &str| match name {
            PORT_OVERRIDE_ENVIRONMENT_VARIABLE => Some(OsString::from("5291")),
            "HOME" => Some(OsString::from("/home/someone")),
            _ => None,
        };
        assert!(matches!(
            host_source("Production", &environment),
            Some(HostSource::Installed { .. })
        ));
    }

    #[test]
    fn every_failure_message_comes_from_the_catalog() {
        for failure in [
            Failure::NotRunning,
            Failure::Unauthorized,
            Failure::Unavailable,
            Failure::Status(500),
            Failure::Lost,
            Failure::NotInitialized,
        ] {
            let message = failure.message();
            assert!(!message.starts_with("[["), "{failure:?}: {message}");
        }
        assert!(Failure::Status(503).message().contains("503"));
    }

    fn proof_host(secret: Arc<Mutex<String>>) -> u16 {
        serve(move |request, mut stream| {
            let nonce = request.json()["nonce"]
                .as_str()
                .unwrap_or_default()
                .to_string();
            let current = secret.lock().unwrap().clone();
            let proof = loopback_secret::proof_for(&current, &nonce).unwrap();
            respond_json(&mut stream, None, &json!({ "proof": proof }));
        })
    }

    fn persisted_port_path(config_dir: &Path) -> PathBuf {
        config_dir.join("loopback-port")
    }

    fn config_with(port: u16, secret: &str) -> tempdir::Dir {
        let dir = tempdir::Dir::new();
        std::fs::write(persisted_port_path(dir.path()), port.to_string()).unwrap();
        loopback_secret::persist(dir.path(), secret).unwrap();
        dir
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_port_that_cannot_prove_the_secret_is_never_used() {
        let port = proof_host(Arc::new(Mutex::new(OTHER_SECRET.to_string())));
        let dir = config_with(port, SECRET);
        let source = HostSource::Installed {
            config_dir: dir.path().to_path_buf(),
        };
        assert_eq!(
            locate(&source, PROOF_TIMEOUT).await,
            Err(Failure::NotRunning)
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_secret_rotated_by_a_host_restart_still_proves() {
        let secret = Arc::new(Mutex::new(SECRET.to_string()));
        let port = proof_host(Arc::clone(&secret));
        let dir = config_with(port, SECRET);
        let source = HostSource::Installed {
            config_dir: dir.path().to_path_buf(),
        };
        assert_eq!(locate(&source, PROOF_TIMEOUT).await, Ok(port));
        *secret.lock().unwrap() = OTHER_SECRET.to_string();
        loopback_secret::persist(dir.path(), OTHER_SECRET).unwrap();
        assert_eq!(locate(&source, PROOF_TIMEOUT).await, Ok(port));
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn requests_after_initialize_carry_session_and_protocol_version() {
        let seen = Arc::new(Mutex::new(Vec::new()));
        let recorded = Arc::clone(&seen);
        let port = serve(
            move |request, mut stream| match request.rpc_method().as_str() {
                "initialize" => respond_json(
                    &mut stream,
                    Some("s1"),
                    &initialize_result(&request.json()["id"]),
                ),
                "tools/list" => {
                    recorded.lock().unwrap().push((
                        request.header(SESSION_HEADER).map(str::to_string),
                        request.header(PROTOCOL_HEADER).map(str::to_string),
                        request.header("authorization").map(str::to_string),
                    ));
                    respond_json(
                        &mut stream,
                        None,
                        &json!({ "jsonrpc": "2.0", "id": request.json()["id"], "result": { "tools": [] } }),
                    );
                }
                _ if request.method == "GET" => {
                    respond_status(&mut stream, "405 Method Not Allowed")
                }
                _ => respond_status(&mut stream, "202 Accepted"),
            },
        );
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        initialize(&bridge, &mut receiver).await;
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":1,"method":"tools/list"}"#)
            .await;
        assert_eq!(next(&mut receiver).await["result"]["tools"], json!([]));
        assert_eq!(
            seen.lock().unwrap().as_slice(),
            &[(
                Some("s1".to_string()),
                Some("2025-06-18".to_string()),
                Some(format!("Bearer {TOKEN}"))
            )]
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn an_unknown_session_is_reinitialized_and_the_request_replayed_once() {
        let initializes = Arc::new(AtomicUsize::new(0));
        let calls = Arc::new(AtomicUsize::new(0));
        let (counted_initializes, counted_calls) = (Arc::clone(&initializes), Arc::clone(&calls));
        let port = serve(
            move |request, mut stream| match request.rpc_method().as_str() {
                "initialize" => {
                    let n = counted_initializes.fetch_add(1, Ordering::SeqCst) + 1;
                    respond_json(
                        &mut stream,
                        Some(&format!("s{n}")),
                        &initialize_result(&request.json()["id"]),
                    );
                }
                "tools/list" if request.header(SESSION_HEADER) == Some("s1") => {
                    respond_status(&mut stream, "404 Not Found")
                }
                "tools/list" => {
                    counted_calls.fetch_add(1, Ordering::SeqCst);
                    respond_json(
                        &mut stream,
                        None,
                        &json!({ "jsonrpc": "2.0", "id": request.json()["id"], "result": { "tools": ["replayed"] } }),
                    );
                }
                _ if request.method == "GET" => {
                    respond_status(&mut stream, "405 Method Not Allowed")
                }
                _ => respond_status(&mut stream, "202 Accepted"),
            },
        );
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        initialize(&bridge, &mut receiver).await;
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":7,"method":"tools/list"}"#)
            .await;
        let answer = next(&mut receiver).await;
        assert_eq!(answer["id"], 7);
        assert_eq!(answer["result"]["tools"], json!(["replayed"]));
        assert_eq!(initializes.load(Ordering::SeqCst), 2);
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        assert!(
            receiver.try_recv().is_err(),
            "the replayed initialize reached the client"
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_call_lost_after_the_host_accepted_it_is_failed_not_replayed() {
        let calls = Arc::new(AtomicUsize::new(0));
        let counted = Arc::clone(&calls);
        let port = serve(
            move |request, mut stream| match request.rpc_method().as_str() {
                "initialize" => respond_json(
                    &mut stream,
                    Some("s1"),
                    &initialize_result(&request.json()["id"]),
                ),
                "tools/call" => {
                    counted.fetch_add(1, Ordering::SeqCst);
                    open_event_stream(&mut stream);
                    let _ = stream.shutdown(std::net::Shutdown::Both);
                }
                _ if request.method == "GET" => {
                    respond_status(&mut stream, "405 Method Not Allowed")
                }
                _ => respond_status(&mut stream, "202 Accepted"),
            },
        );
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        initialize(&bridge, &mut receiver).await;
        bridge
            .handle_line(
                r#"{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"run_action"}}"#,
            )
            .await;
        let answer = next(&mut receiver).await;
        assert_eq!(answer["id"], 3);
        assert_eq!(answer["error"]["message"], Failure::Lost.message());
        tokio::time::sleep(Duration::from_millis(300)).await;
        assert_eq!(calls.load(Ordering::SeqCst), 1);
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn an_elicitation_inside_an_open_call_is_answered_without_deadlock() {
        let (answer_sender, answer_receiver) = std::sync::mpsc::channel::<Value>();
        let answer_receiver = Arc::new(Mutex::new(answer_receiver));
        let port = serve(
            move |request, mut stream| match request.rpc_method().as_str() {
                "initialize" => respond_json(
                    &mut stream,
                    Some("s1"),
                    &initialize_result(&request.json()["id"]),
                ),
                "tools/call" => {
                    open_event_stream(&mut stream);
                    send_event(
                        &mut stream,
                        &json!({ "jsonrpc": "2.0", "id": "e1", "method": "elicitation/create", "params": { "message": "Run it?" } }),
                    );
                    let answer = answer_receiver
                        .lock()
                        .unwrap()
                        .recv_timeout(Duration::from_secs(10));
                    let accepted =
                        answer.is_ok_and(|answer| answer["result"]["action"] == "accept");
                    send_event(
                        &mut stream,
                        &json!({ "jsonrpc": "2.0", "id": request.json()["id"], "result": { "accepted": accepted } }),
                    );
                }
                _ if request.method == "GET" => {
                    respond_status(&mut stream, "405 Method Not Allowed")
                }
                _ => {
                    let message = request.json();
                    if message.get("method").is_none() {
                        let _ = answer_sender.send(message);
                    }
                    respond_status(&mut stream, "202 Accepted");
                }
            },
        );
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        initialize(&bridge, &mut receiver).await;
        bridge
            .handle_line(
                r#"{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"run_action"}}"#,
            )
            .await;
        let elicitation = next(&mut receiver).await;
        assert_eq!(elicitation["method"], "elicitation/create");
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":"e1","result":{"action":"accept"}}"#)
            .await;
        let answer = next(&mut receiver).await;
        assert_eq!(answer["id"], 4);
        assert_eq!(answer["result"]["accepted"], true);
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_rejected_token_is_reported_without_revealing_it() {
        let port = serve(|_, mut stream| respond_status(&mut stream, "401 Unauthorized"));
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}"#)
            .await;
        let answer = next(&mut receiver).await;
        let message = answer["error"]["message"].as_str().unwrap();
        assert_eq!(message, Failure::Unauthorized.message());
        assert!(!message.contains(TOKEN));
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn no_running_host_is_reported_as_such() {
        let locate: Locate = Arc::new(|| Box::pin(async { Err(Failure::NotRunning) }));
        let (output, mut receiver) = collecting();
        let bridge = bridge(locate, output);
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}"#)
            .await;
        assert_eq!(
            next(&mut receiver).await["error"]["message"],
            Failure::NotRunning.message()
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_locked_host_without_an_endpoint_is_reported_as_unavailable() {
        let port = serve(|_, mut stream| respond_status(&mut stream, "404 Not Found"));
        let (output, mut receiver) = collecting();
        let bridge = bridge(fixed_port(port), output);
        bridge
            .handle_line(r#"{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}"#)
            .await;
        assert_eq!(
            next(&mut receiver).await["error"]["message"],
            Failure::Unavailable.message()
        );
    }

    #[test]
    fn the_event_parser_handles_split_chunks_crlf_and_multi_line_data() {
        let mut parser = SseParser::default();
        assert!(parser
            .feed(b": keep-alive\r\nevent: message\r\nid: 1\r\nda")
            .is_empty());
        assert_eq!(
            parser.feed(b"ta: {\"a\":\r\n\r\n"),
            vec!["{\"a\":".to_string()]
        );
        assert_eq!(
            parser.feed(b"data:one\ndata: two\n\ndata: \xc3"),
            vec!["one\ntwo".to_string()]
        );
        assert_eq!(parser.feed(b"\xa4\n\n"), vec!["\u{e4}".to_string()]);
    }

    mod tempdir {
        use std::path::{Path, PathBuf};

        pub struct Dir(PathBuf);

        impl Dir {
            pub fn new() -> Self {
                let path = std::env::temp_dir().join(format!(
                    "macro-deck-mcp-bridge-{}",
                    crate::loopback_secret::new_nonce()
                ));
                std::fs::create_dir_all(&path).unwrap();
                Dir(path)
            }

            pub fn path(&self) -> &Path {
                &self.0
            }
        }

        impl Drop for Dir {
            fn drop(&mut self) {
                let _ = std::fs::remove_dir_all(&self.0);
            }
        }
    }
}
