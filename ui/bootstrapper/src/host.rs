// .NET host lifecycle: the packaged bootstrapper is the installed entry point,
// spawns the sibling MacroDeckHost binary, waits until it answers and stops it
// again on quit (graceful shutdown endpoint first, kill as fallback).

use std::ffi::OsString;
use std::future::Future;
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU16, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};
use tauri::AppHandle;
use tauri::Manager;

use crate::deep_links;
use crate::host_error_window::{self, ExitStatus, HostErrorKind, HostErrorReport};
use crate::host_supervisor::{Decision, ExitAction, Supervisor, MAX_RESTART_ATTEMPTS};
use crate::localization::{self, keys};
use crate::logging::{self, LogTail};
use crate::loopback_secret;
use crate::update_state::{BackupProgress, UpdateFailure, UpdateSnapshot};

pub const BUILD_CHANNEL: &str = env!("MACRODECK_BUILD_CHANNEL");

pub fn default_public_port(build_channel: &str) -> u16 {
    match build_channel {
        "Development" => 7193,
        "Production" => 8193,
        _ => unreachable!("build channel is validated by build.rs"),
    }
}

pub fn loopback_port_file_name(build_channel: &str) -> &'static str {
    match build_channel {
        "Development" => "macro-deck-host-development.port",
        "Production" => "macro-deck-host.port",
        _ => unreachable!("build channel is validated by build.rs"),
    }
}

pub fn data_root_directory_name(build_channel: &str) -> &'static str {
    match build_channel {
        "Development" => ".data",
        "Production" => "MacroDeck",
        _ => unreachable!("build channel is validated by build.rs"),
    }
}

pub fn host_public_port() -> u16 {
    default_public_port(BUILD_CHANNEL)
}

pub fn host_data_root_directory_name() -> &'static str {
    data_root_directory_name(BUILD_CHANNEL)
}

fn port_file_name() -> &'static str {
    loopback_port_file_name(BUILD_CHANNEL)
}

const PERSISTED_PORT_FILE: &str = "loopback-port";

const QUIT_STOP_TIMEOUT: Duration = Duration::from_secs(35);

const UPDATE_STOP_TIMEOUT: Duration = Duration::from_secs(35);

const FORCED_STOP_TIMEOUT: Duration = Duration::from_secs(10);

const REPLACED_HOST_STOP_TIMEOUT: Duration = QUIT_STOP_TIMEOUT;

// Kestrel releases the port at the start of a graceful shutdown while the process lives on for up to
// the host's shutdown timeout; relaunching sooner could start a second host on the same data root.
const ADOPTED_HOST_EXIT_GRACE: Duration = QUIT_STOP_TIMEOUT;

const ADOPTED_HOST_POLL_INTERVAL: Duration = Duration::from_millis(250);

const LEGACY_HOST_STOP_TIMEOUT: Duration = Duration::from_secs(10);

const ADOPTION_PROBE_TIMEOUT: Duration = Duration::from_secs(1);

// Must match UserSessionEnd.ShutdownReason in the host.
#[cfg(any(windows, test))]
const SESSION_END_REASON: &str = "session-end";

// Windows kills a windowless app that has not answered WM_ENDSESSION within 5 s, and shows its
// blocking screen to one with a window; staying under that still covers the 2 s dispatch window.
#[cfg(any(windows, test))]
const SESSION_END_STOP_TIMEOUT: Duration = Duration::from_secs(4);

const READY_TIMEOUT: Duration = Duration::from_secs(60);

const RETRY_READY_TIMEOUT: Duration = Duration::from_secs(30);

// Must match StartupHeartbeat.Line in the host. The host prints it while it applies or rolls back a
// staged restore before it listens; the ready timeout then counts from the last one.
const HOST_HEARTBEAT_LINE: &str = "macrodeck-host-heartbeat";

const UNRESPONSIVE_STOP_TIMEOUT: Duration = Duration::from_secs(5);

// Mirrors the host's own configured HostOptions.ShutdownTimeout (not the shorter per-plugin grace
// inside it), so the two budgets cannot silently drift apart.
#[cfg(test)]
pub const HOST_SHUTDOWN_WORST_CASE: Duration = Duration::from_secs(30);

// Mirrors the host's Server stopped dispatch window in ServerLifecycleEventBackgroundService.
#[cfg(test)]
pub const HOST_SERVER_STOPPED_DISPATCH_WINDOW: Duration = Duration::from_secs(2);

#[cfg(test)]
pub const WINDOWS_END_SESSION_ALLOWANCE: Duration = Duration::from_secs(5);

pub const HOST_RESTART_EXIT_CODE: i32 = 86;

pub const HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE: i32 = 87;

pub const HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE: i32 = 88;

pub const RELAUNCH_MARKER_ENVIRONMENT_VARIABLE: &str = "MACRODECK_RELAUNCH";

pub const RELAUNCH_SETTLE_DELAY: Duration = Duration::from_millis(1500);

const STOP_POLL_INTERVAL: Duration = Duration::from_millis(250);

const SHUTDOWN_RETRY_INTERVAL: Duration = Duration::from_secs(1);

pub struct HostState {
    child: Mutex<Option<(u64, Child)>>,
    #[cfg(windows)]
    job: Mutex<Option<crate::host_job::HostJob>>,
    pub exited: AtomicBool,
    pub ready: AtomicBool,
    pub shutdown_expected: AtomicBool,
    adopted: AtomicBool,
    pub log_tail: Arc<LogTail>,
    heartbeat: Arc<HostHeartbeat>,
    stopping: AtomicBool,
    ui_port: AtomicU16,
    supervisor: Mutex<Supervisor>,
}

impl HostState {
    pub fn new() -> Self {
        Self {
            child: Mutex::new(None),
            #[cfg(windows)]
            job: Mutex::new(None),
            exited: AtomicBool::new(false),
            ready: AtomicBool::new(false),
            shutdown_expected: AtomicBool::new(false),
            adopted: AtomicBool::new(false),
            log_tail: Arc::new(LogTail::new(40)),
            heartbeat: Arc::new(HostHeartbeat::default()),
            stopping: AtomicBool::new(false),
            ui_port: AtomicU16::new(0),
            supervisor: Mutex::new(Supervisor::new()),
        }
    }

    pub fn ui_port(&self) -> Option<u16> {
        match self.ui_port.load(Ordering::SeqCst) {
            0 => None,
            port => Some(port),
        }
    }

    fn spawned(&self) -> bool {
        self.child
            .lock()
            .map(|child| child.is_some())
            .unwrap_or(false)
    }
}

pub fn parse_port(raw: &str) -> Option<u16> {
    let port: u32 = raw.trim().parse().ok()?;
    if port > 0 && port <= u16::MAX as u32 {
        Some(port as u16)
    } else {
        None
    }
}

fn env_port_override() -> Option<u16> {
    std::env::var("MACRODECK_HOST_PORT")
        .ok()
        .and_then(|value| parse_port(&value))
}

pub fn resolve_public_port(configured_port: Option<&str>) -> u16 {
    configured_port
        .and_then(parse_port)
        .unwrap_or_else(host_public_port)
}

fn public_port() -> u16 {
    let configured_port = std::env::var("MACRO_DECK_PORT").ok();
    resolve_public_port(configured_port.as_deref())
}

pub fn is_development_build() -> bool {
    BUILD_CHANNEL == "Development"
}

fn port_file_port() -> Option<u16> {
    let path = std::env::temp_dir().join(port_file_name());
    std::fs::read_to_string(path)
        .ok()
        .and_then(|raw| parse_port(&raw))
}

pub fn current_port() -> Option<u16> {
    env_port_override().or_else(port_file_port)
}

fn is_port_free(port: u16) -> bool {
    std::net::TcpListener::bind(("127.0.0.1", port)).is_ok()
}

fn allocate_free_port() -> Option<u16> {
    std::net::TcpListener::bind(("127.0.0.1", 0))
        .and_then(|listener| listener.local_addr())
        .map(|addr| addr.port())
        .ok()
}

pub fn choose_port(
    persisted: Option<u16>,
    is_free: impl Fn(u16) -> bool,
    allocate: impl Fn() -> Option<u16>,
) -> Option<u16> {
    match persisted {
        Some(port) if is_free(port) => Some(port),
        _ => allocate(),
    }
}

pub fn read_persisted_port(config_dir: &Path) -> Option<u16> {
    std::fs::read_to_string(config_dir.join(PERSISTED_PORT_FILE))
        .ok()
        .and_then(|raw| parse_port(&raw))
}

fn acquire_loopback_port(config_dir: &Path) -> Option<u16> {
    let persisted = read_persisted_port(config_dir);
    let port = choose_port(persisted, is_port_free, allocate_free_port)?;
    if persisted != Some(port) {
        let _ = std::fs::create_dir_all(config_dir);
        let path = config_dir.join(PERSISTED_PORT_FILE);
        if let Err(error) = std::fs::write(&path, port.to_string()) {
            logging::warn(&format!("[host] could not persist loopback port: {error}"));
        }
    }
    Some(port)
}

pub fn adoption_candidate(
    env_port: Option<u16>,
    port_file_port: Option<u16>,
    persisted_port: Option<u16>,
) -> Option<u16> {
    if env_port.is_some() {
        return env_port;
    }
    match (port_file_port, persisted_port) {
        (Some(from_file), Some(persisted)) if from_file == persisted => Some(from_file),
        _ => None,
    }
}

pub fn parse_trusted_status(body: &str) -> bool {
    serde_json::from_str::<serde_json::Value>(body)
        .ok()
        .and_then(|value| value.get("trusted").and_then(serde_json::Value::as_bool))
        .unwrap_or(false)
}

#[derive(Debug, PartialEq, Eq)]
pub enum Adoption {
    Adopt,
    Replace,
    Ignore,
}

// Fail-closed: an unreadable version is replaced too, so a host of another build never stays adopted.
pub fn adoption_decision(
    proved: bool,
    running_version: Option<&str>,
    own_version: &str,
) -> Adoption {
    if !proved {
        return Adoption::Ignore;
    }
    match running_version {
        Some(version) if version.trim() == own_version.trim() => Adoption::Adopt,
        _ => Adoption::Replace,
    }
}

#[derive(Deserialize)]
struct HostStatus {
    version: Option<String>,
}

async fn host_version(port: u16) -> Option<String> {
    let client = loopback_secret::http_client(ADOPTION_PROBE_TIMEOUT).ok()?;
    let response = client
        .get(format!("http://127.0.0.1:{port}/api/auth/status"))
        .send()
        .await
        .ok()?;
    if !response.status().is_success() {
        return None;
    }
    response.json::<HostStatus>().await.ok()?.version
}

async fn wait_until_unreachable(port: u16, timeout: Duration) -> bool {
    let deadline = Instant::now() + timeout;
    while Instant::now() < deadline {
        if !is_reachable(Some(port), Duration::from_millis(300)).await {
            return true;
        }
        tokio::time::sleep(STOP_POLL_INTERVAL).await;
    }
    !is_reachable(Some(port), Duration::from_millis(300)).await
}

// A host from before the per-launch secret trusts any loopback caller; only such a host of ours
// answers trusted to a request that carries no credential at all.
async fn is_legacy_trusted_loopback(port: u16, timeout: Duration) -> bool {
    let Ok(client) = loopback_secret::http_client(timeout) else {
        return false;
    };
    match client
        .get(format!("http://127.0.0.1:{port}/api/auth/status"))
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => response
            .text()
            .await
            .map(|body| parse_trusted_status(&body))
            .unwrap_or(false),
        _ => false,
    }
}

pub async fn is_reachable(port: Option<u16>, timeout: Duration) -> bool {
    let Some(port) = port else {
        return false;
    };
    let Ok(client) = loopback_secret::http_client(timeout) else {
        return false;
    };
    match client
        .get(format!("http://127.0.0.1:{port}/api/auth/status"))
        .send()
        .await
    {
        Ok(response) => response.status().is_success(),
        Err(_) => false,
    }
}

pub fn is_packaged() -> bool {
    !cfg!(debug_assertions)
}

// Windows keeps separate host names for Development and Production so their NSIS installers can
// stop only their own host before replacing its directory. The friendly Task Manager name comes
// from the Win32 version resource. On Linux/macOS the launcher is renamed to "Macro Deck Host" at
// publish time so `ps`/`top`/Activity Monitor show a friendly name (issue #79). Keep these names
// in sync with host/src/MacroDeckHost/MacroDeckHost.csproj and installer/hooks.nsh.
fn host_binary_name_for(build_channel: &str, windows: bool) -> &'static str {
    if windows {
        match build_channel {
            "Development" => "MacroDeckHostDevelopment.exe",
            "Production" => "MacroDeckHost.exe",
            _ => unreachable!("build channel is validated by build.rs"),
        }
    } else {
        "Macro Deck Host"
    }
}

fn host_binary_name() -> &'static str {
    host_binary_name_for(BUILD_CHANNEL, cfg!(windows))
}

fn autostart_executable<F: Fn(&Path) -> bool>(
    appimage: Option<PathBuf>,
    current_exe: Option<PathBuf>,
    is_file: F,
) -> Option<PathBuf> {
    appimage
        .filter(|path| path.is_absolute() && is_file(path))
        .or(current_exe)
}

pub fn shell_executable() -> Option<PathBuf> {
    autostart_executable(
        std::env::var_os("APPIMAGE").map(PathBuf::from),
        std::env::current_exe().ok(),
        |path| path.is_file(),
    )
}

fn host_binary_candidates(app: &AppHandle) -> Vec<PathBuf> {
    let binary = host_binary_name();
    let mut candidates = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            candidates.push(dir.join("host").join(binary));
        }
    }
    if let Ok(dir) = app.path().resource_dir() {
        candidates.push(dir.join("host").join(binary));
    }
    candidates
}

pub fn host_binary_path(app: &AppHandle) -> Option<PathBuf> {
    host_binary_candidates(app)
        .into_iter()
        .find(|path| path.exists())
}

pub fn host_dir(app: &AppHandle) -> PathBuf {
    let dirs: Vec<PathBuf> = host_binary_candidates(app)
        .into_iter()
        .filter_map(|binary| binary.parent().map(Path::to_path_buf))
        .collect();
    dirs.iter()
        .find(|dir| dir.is_dir())
        .or(dirs.first())
        .cloned()
        .unwrap_or_else(|| PathBuf::from("host"))
}

fn detect_port_conflict(log: &str) -> bool {
    let lowered = log.to_lowercase();
    lowered.contains("address already in use")
        || lowered.contains("address in use")
        || lowered.contains("failed to bind")
}

pub fn conflicting_port_from_log(log: &str) -> Option<u16> {
    log.lines()
        .filter(|line| detect_port_conflict(line))
        .filter_map(port_from_address)
        .next()
}

fn port_from_address(line: &str) -> Option<u16> {
    let authority = line.split("://").nth(1)?.split_whitespace().next()?;
    let port = authority.rsplit(':').next()?;
    parse_port(port.trim_end_matches(|c: char| !c.is_ascii_digit()))
}

pub fn startup_failure_reason(log: &str) -> String {
    if detect_port_conflict(log) {
        let port = conflicting_port_from_log(log)
            .unwrap_or_else(public_port)
            .to_string();
        localization::t_args(keys::ERRORS_PORT_IN_USE, &[("port", &port)])
    } else {
        localization::t(keys::ERRORS_HOST_DID_NOT_START)
    }
}

fn exit_failure_reason(code: Option<i32>, log: &str) -> String {
    if code == Some(HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE) {
        localization::t(keys::ERRORS_CRYPTOGRAPHY_UNAVAILABLE)
    } else if code == Some(HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE) {
        localization::t(keys::ERRORS_DATABASE_FROM_NEWER_VERSION)
    } else {
        startup_failure_reason(log)
    }
}

#[derive(Default)]
pub struct HostHeartbeat {
    last: Mutex<Option<Instant>>,
}

impl HostHeartbeat {
    fn beat(&self, at: Instant) {
        if let Ok(mut last) = self.last.lock() {
            *last = Some(at);
        }
    }

    fn last(&self) -> Option<Instant> {
        self.last.lock().ok().and_then(|last| *last)
    }

    fn clear(&self) {
        if let Ok(mut last) = self.last.lock() {
            *last = None;
        }
    }
}

pub fn ready_deadline(
    started: Instant,
    last_heartbeat: Option<Instant>,
    timeout: Duration,
) -> Instant {
    last_heartbeat.map_or(started, |beat| beat.max(started)) + timeout
}

fn pipe_to_tail<R: std::io::Read + Send + 'static>(
    reader: R,
    tail: Arc<LogTail>,
    heartbeat: Arc<HostHeartbeat>,
) {
    std::thread::spawn(move || drain_into_tail(reader, &tail, &heartbeat));
}

// Reads to the end whatever the output holds: stopping early closes the pipe, and every later write
// by the host, or by a program it started, then fails or raises SIGPIPE.
fn drain_into_tail<R: std::io::Read>(reader: R, tail: &LogTail, heartbeat: &HostHeartbeat) {
    let mut reader = BufReader::new(reader);
    let mut line = Vec::new();
    loop {
        line.clear();
        match reader.read_until(b'\n', &mut line) {
            Ok(0) => return,
            Ok(_) => {
                let text = String::from_utf8_lossy(&line);
                if text.trim() == HOST_HEARTBEAT_LINE {
                    heartbeat.beat(Instant::now());
                } else {
                    tail.push_chunk(&crate::redact::redact(&text));
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::Interrupted => {}
            Err(_) => return,
        }
    }
}

pub async fn ensure_running(app: &AppHandle) -> bool {
    let state = app.state::<Arc<HostState>>();
    if !is_packaged() {
        state.ready.store(true, Ordering::SeqCst);
        return true;
    }

    let config_dir = app.path().app_config_dir().ok();
    let port_file_dir = config_dir.as_deref().unwrap_or(Path::new("."));

    // Reuse an already-running host (e.g. restart of the UI only), but only
    // one this bootstrapper assigned the port to, and only when it proves it
    // holds the persisted secret - never a foreign listener that squats the
    // port and never the public LAN listener.
    if let Some(port) = adoption_candidate(
        env_port_override(),
        port_file_port(),
        read_persisted_port(port_file_dir),
    ) {
        let proved = match loopback_secret::load(port_file_dir) {
            Some(secret) => {
                loopback_secret::set_secret(secret);
                loopback_secret::prove(port, ADOPTION_PROBE_TIMEOUT).await
            }
            None => false,
        };
        let running_version = if proved {
            host_version(port).await
        } else {
            None
        };
        let own_version = crate::updater::current_version(app);
        match adoption_decision(proved, running_version.as_deref(), &own_version) {
            Adoption::Adopt => {
                logging::info(&format!(
                    "[host] adopting running host on loopback port {port}"
                ));
                let generation = state
                    .supervisor
                    .lock()
                    .ok()
                    .and_then(|mut supervisor| supervisor.adopt(Instant::now()));
                state.ui_port.store(port, Ordering::SeqCst);
                if let Some(generation) = generation {
                    state.adopted.store(true, Ordering::SeqCst);
                    spawn_adoption_monitor(app.clone(), generation, port);
                }
                state.ready.store(true, Ordering::SeqCst);
                crate::install_integrity::host_ready(app);
                adopt_host_culture(app, port).await;
                return true;
            }
            Adoption::Replace => {
                logging::info(&format!(
                    "[host] replacing the running host {} on port {port} with {own_version}",
                    running_version.as_deref().unwrap_or("of unknown version")
                ));
                let _ = post_shutdown(port, "replaced").await;
                if !wait_until_unreachable(port, REPLACED_HOST_STOP_TIMEOUT).await {
                    logging::error(&format!(
                        "[host] the replaced host on port {port} is still running; not starting a second one"
                    ));
                    let started = state
                        .supervisor
                        .lock()
                        .map(|mut supervisor| supervisor.start())
                        .unwrap_or(false);
                    if started {
                        let reason = localization::t_args(
                            keys::ERRORS_PORT_IN_USE,
                            &[("port", &port.to_string())],
                        );
                        give_up(app, Some(reason), ExitStatus::NotRecorded, String::new()).await;
                    }
                    return false;
                }
            }
            Adoption::Ignore => {
                if is_legacy_trusted_loopback(port, ADOPTION_PROBE_TIMEOUT).await {
                    logging::info(&format!(
                        "[host] stopping the host on port {port}, which predates the loopback secret"
                    ));
                    let _ = post_shutdown(port, "replaced").await;
                    wait_until_unreachable(port, LEGACY_HOST_STOP_TIMEOUT).await;
                } else if is_reachable(Some(port), Duration::from_millis(500)).await {
                    logging::warn(&format!(
                        "[host] port {port} answers but is not a trusted loopback listener; starting our own host"
                    ));
                }
            }
        }
    }

    let started = state
        .supervisor
        .lock()
        .map(|mut supervisor| supervisor.start())
        .unwrap_or(false);
    if !started {
        return false;
    }
    supervise(app, None).await
}

enum LaunchError {
    NoFreePort,
    BinaryMissing(String),
    SpawnFailed(String),
}

impl LaunchError {
    fn reason(&self) -> String {
        match self {
            LaunchError::NoFreePort => localization::t(keys::ERRORS_NO_FREE_PORT),
            LaunchError::BinaryMissing(paths) => {
                localization::t_args(keys::ERRORS_HOST_BINARY_MISSING, &[("paths", paths)])
            }
            LaunchError::SpawnFailed(error) => localization::t_args(
                keys::ERRORS_HOST_PROCESS_COULD_NOT_START,
                &[("error", error)],
            ),
        }
    }
}

// Runs under the supervisor lock so a stop either happens before the attempt begins or sees the
// new child in the slot; the child and job slots are only replaced once the spawn succeeded.
fn launch(app: &AppHandle, state: &HostState) -> Result<Option<(u64, u16)>, LaunchError> {
    let Ok(mut supervisor) = state.supervisor.lock() else {
        return Ok(None);
    };
    if crate::is_quitting() {
        return Ok(None);
    }
    let Some(generation) = supervisor.begin_attempt() else {
        return Ok(None);
    };

    // None only when the home directory is unresolvable. The port file tolerates that and falls
    // back to the process cwd; the bundle extraction below deliberately does not.
    let config_dir = app.path().app_config_dir().ok();
    let port_file_dir = config_dir.as_deref().unwrap_or(Path::new("."));
    let port = acquire_loopback_port(port_file_dir).ok_or(LaunchError::NoFreePort)?;

    let Some(binary) = host_binary_path(app) else {
        let candidates = host_binary_candidates(app)
            .iter()
            .map(|path| path.display().to_string())
            .collect::<Vec<_>>()
            .join("\n");
        return Err(LaunchError::BinaryMissing(candidates));
    };

    logging::info(&format!(
        "[host] starting {} on loopback port {port}",
        binary.display()
    ));
    let secret = loopback_secret::generate();
    if let Some(dir) = &config_dir {
        if let Err(error) = loopback_secret::persist(dir, &secret) {
            logging::warn(&format!(
                "[host] could not persist the loopback secret: {error}"
            ));
        }
    }
    loopback_secret::set_secret(secret.clone());

    let mut command = Command::new(&binary);
    command
        .current_dir(binary.parent().unwrap_or(Path::new(".")))
        .env("MACRODECK_HOST_PORT", port.to_string())
        .env(loopback_secret::ENVIRONMENT_VARIABLE, &secret)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some(dir) = &config_dir {
        command.env("DOTNET_BUNDLE_EXTRACT_BASE_DIR", dir.join("host-runtime"));
    }
    if let Some(exe) = shell_executable() {
        command.env("MACRODECK_SHELL_EXECUTABLE", exe);
    }
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    // Puts the host in its own process group so a forced stop can signal the whole tree (the
    // plugins it spawns) instead of just the host pid; a .NET child inherits its parent's group.
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        command.process_group(0);
    }

    state.log_tail.clear();
    state.heartbeat.clear();
    let mut child = command
        .spawn()
        .map_err(|error| LaunchError::SpawnFailed(error.to_string()))?;

    if let Some(stdout) = child.stdout.take() {
        pipe_to_tail(stdout, state.log_tail.clone(), state.heartbeat.clone());
    }
    if let Some(stderr) = child.stderr.take() {
        pipe_to_tail(stderr, state.log_tail.clone(), state.heartbeat.clone());
    }

    #[cfg(windows)]
    {
        let job = crate::host_job::HostJob::create_and_assign(&child);
        if job.is_none() {
            logging::warn(
                "[host] falling back to a plain process kill; the job object was not set up",
            );
        }
        if let Ok(mut slot) = state.job.lock() {
            *slot = job;
        }
    }

    if let Ok(mut slot) = state.child.lock() {
        *slot = Some((generation, child));
        state.exited.store(false, Ordering::SeqCst);
        state.ready.store(false, Ordering::SeqCst);
        crate::install_integrity::host_gone();
    }
    state.ui_port.store(port, Ordering::SeqCst);
    drop(supervisor);

    spawn_exit_monitor(app.clone(), generation);
    Ok(Some((generation, port)))
}

#[derive(Deserialize)]
struct LocalizationInfo {
    culture: String,
}

/// Once the host answers, it is the authority on the active language - so this adopts its reported
/// culture and rebuilds the parts of the UI that were already drawn in the pre-host fallback
/// language. Best-effort: a host that cannot be asked, or that answers with something unexpected,
/// just leaves the bootstrapper on its pre-host culture.
async fn adopt_host_culture(app: &AppHandle, port: u16) {
    #[cfg(not(target_os = "macos"))]
    let _ = app;

    let Ok(client) = loopback_secret::http_client(Duration::from_secs(3)) else {
        return;
    };
    let request = client.get(format!("http://127.0.0.1:{port}/api/localization"));
    let response = match loopback_secret::authorize(request, port).await.send().await {
        Ok(response) if response.status().is_success() => response,
        Ok(response) => {
            logging::warn(&format!(
                "[localization] host localization request failed with status {}",
                response.status()
            ));
            return;
        }
        Err(error) => {
            logging::warn(&format!(
                "[localization] could not reach the host localization endpoint: {error}"
            ));
            return;
        }
    };

    match response.json::<LocalizationInfo>().await {
        Ok(info) => {
            logging::info(&format!(
                "[localization] adopting the host's culture: {}",
                info.culture
            ));
            localization::set_culture(&info.culture);
            // The tray menu is built once at startup, before the host can be asked for its culture,
            // and Tauri has no in-place way to relabel it - only the macOS application menu can be
            // rebuilt here. The tray therefore keeps its pre-host language until the next launch.
            #[cfg(target_os = "macos")]
            if let Err(error) = crate::menu::setup(app) {
                logging::error(&format!(
                    "[localization] could not rebuild the application menu: {error}"
                ));
            }
        }
        Err(error) => {
            logging::warn(&format!(
                "[localization] could not parse the host localization response: {error}"
            ));
        }
    }
}

pub fn relaunch_target(appimage: Option<PathBuf>, current_exe: Option<PathBuf>) -> Option<PathBuf> {
    appimage.or(current_exe)
}

pub fn relaunch_arguments(args: impl Iterator<Item = OsString>) -> Vec<OsString> {
    args.skip(1)
        .filter(|argument| !argument.to_str().is_some_and(deep_links::is_link_argument))
        .collect()
}

pub(crate) fn relaunch(app: &AppHandle) -> bool {
    let Some(binary) = relaunch_target(
        std::env::var_os("APPIMAGE").map(PathBuf::from),
        std::env::current_exe().ok(),
    ) else {
        logging::error("[host] no executable to relaunch");
        return false;
    };

    match Command::new(&binary)
        .args(relaunch_arguments(std::env::args_os()))
        .env(RELAUNCH_MARKER_ENVIRONMENT_VARIABLE, "1")
        .spawn()
    {
        Ok(_) => {
            app.cleanup_before_exit();
            app.exit(0);
            true
        }
        Err(error) => {
            logging::error(&format!(
                "[host] could not relaunch {}: {error}",
                binary.display()
            ));
            false
        }
    }
}

fn report_failed_restart(app: &AppHandle, code: Option<i32>) {
    let state = app.state::<Arc<HostState>>();
    if let Ok(mut supervisor) = state.supervisor.lock() {
        supervisor.give_up();
    }
    host_error_window::show(
        app,
        HostErrorReport {
            kind: HostErrorKind::RestartFailed,
            attempts: 0,
            reason: Some(localization::t(keys::ERRORS_RESTART_FAILED)),
            exit: ExitStatus::from_code(code),
            log: state.log_tail.joined(),
        },
    );
}

fn spawn_exit_monitor(app: AppHandle, generation: u64) {
    std::thread::spawn(move || {
        let state = app.state::<Arc<HostState>>();
        let code = loop {
            {
                let Ok(mut slot) = state.child.lock() else {
                    return;
                };
                let Some((slot_generation, child)) = slot.as_mut() else {
                    return;
                };
                if *slot_generation != generation {
                    return;
                }
                match child.try_wait() {
                    Ok(Some(status)) => {
                        state.exited.store(true, Ordering::SeqCst);
                        loopback_secret::forget_verified_port();
                        break status.code();
                    }
                    Ok(None) => {}
                    Err(_) => {
                        state.exited.store(true, Ordering::SeqCst);
                        loopback_secret::forget_verified_port();
                        break None;
                    }
                }
            }
            std::thread::sleep(Duration::from_millis(250));
        };

        let shutdown_expected = state.shutdown_expected.load(Ordering::SeqCst);
        let action = state
            .supervisor
            .lock()
            .map(|mut supervisor| {
                supervisor.on_exit(generation, code, shutdown_expected, Instant::now())
            })
            .unwrap_or(ExitAction::Ignore);

        match action {
            ExitAction::Ignore => {
                if !shutdown_expected {
                    logging::error(&format!("[host] exited unexpectedly with code {code:?}"));
                }
            }
            ExitAction::HandledByLoop => {
                logging::warn(&format!(
                    "[host] exited with code {code:?} before it became ready"
                ));
            }
            ExitAction::Relaunch => {
                logging::info("[host] restart requested by the host; relaunching Macro Deck");
                relaunch_for_host(&app, code);
            }
            ExitAction::Recover => {
                logging::error(&format!("[host] exited unexpectedly with code {code:?}"));
                state.ready.store(false, Ordering::SeqCst);
                crate::install_integrity::host_gone();
                let app = app.clone();
                tauri::async_runtime::spawn(async move {
                    if supervise(&app, Some(Failure::Exited(code))).await {
                        crate::window::reload_main_window(&app);
                    }
                });
            }
        }
    });
}

fn relaunch_for_host(app: &AppHandle, code: Option<i32>) {
    let handle = app.clone();
    let dispatched = app.run_on_main_thread(move || {
        crate::mark_quitting(&handle);
        if !relaunch(&handle) {
            crate::clear_quitting();
            report_failed_restart(&handle, code);
        }
    });
    if let Err(error) = dispatched {
        logging::error(&format!("[host] could not dispatch the relaunch: {error}"));
        report_failed_restart(app, code);
    }
}

fn adopted_generation_is_current(state: &HostState, generation: u64) -> bool {
    state
        .supervisor
        .lock()
        .map(|supervisor| supervisor.exit_of(generation).is_none() && !supervisor.is_stopping())
        .unwrap_or(false)
}

// The bind probe fails while anything listens on the port, accepting or not, so a host that is
// alive but hung is never mistaken for gone and doubled.
fn spawn_adoption_monitor(app: AppHandle, generation: u64, port: u16) {
    std::thread::spawn(move || {
        let state = app.state::<Arc<HostState>>();
        loop {
            if !adopted_generation_is_current(&state, generation) {
                return;
            }
            if is_port_free(port) {
                break;
            }
            std::thread::sleep(ADOPTED_HOST_POLL_INTERVAL);
        }

        state.exited.store(true, Ordering::SeqCst);
        state.ready.store(false, Ordering::SeqCst);
        loopback_secret::forget_verified_port();
        crate::install_integrity::host_gone();
        if !adopted_generation_is_current(&state, generation) {
            return;
        }
        logging::warn(&format!(
            "[host] the adopted host released loopback port {port}; its exit code is unknown, relaunching Macro Deck once it had time to exit"
        ));
        std::thread::sleep(ADOPTED_HOST_EXIT_GRACE);

        let shutdown_expected = state.shutdown_expected.load(Ordering::SeqCst);
        let action = state
            .supervisor
            .lock()
            .map(|mut supervisor| supervisor.on_adopted_exit(generation, shutdown_expected))
            .unwrap_or(ExitAction::Ignore);
        if action == ExitAction::Relaunch {
            relaunch_for_host(&app, None);
        }
    });
}

#[derive(Debug, Clone, Copy)]
enum Failure {
    Exited(Option<i32>),
    Unresponsive(Duration),
}

impl Failure {
    fn exit_status(self) -> ExitStatus {
        match self {
            Failure::Exited(code) => ExitStatus::from_code(code),
            Failure::Unresponsive(_) => ExitStatus::NotRecorded,
        }
    }
}

enum AttemptOutcome {
    Ready,
    Exited(Option<i32>),
    TimedOut,
}

fn stop_requested(state: &HostState) -> bool {
    crate::is_quitting()
        || state
            .supervisor
            .lock()
            .map(|supervisor| supervisor.is_stopping())
            .unwrap_or(true)
}

fn exit_of(state: &HostState, generation: u64) -> Option<Option<i32>> {
    state
        .supervisor
        .lock()
        .ok()
        .and_then(|supervisor| supervisor.exit_of(generation))
}

async fn supervise(app: &AppHandle, first_failure: Option<Failure>) -> bool {
    let state = app.state::<Arc<HostState>>();
    let mut restarting = first_failure.is_some();
    let mut pending = first_failure;
    loop {
        if let Some(failure) = pending.take() {
            if stop_requested(&state) {
                return false;
            }
            let log = state.log_tail.joined();
            logging::error(&format!(
                "[host] attempt failed ({failure:?}); host output:\n{log}"
            ));
            kill_child(&state);

            let decision = match state.supervisor.lock() {
                Ok(mut supervisor) => supervisor.on_failure(),
                Err(_) => return false,
            };
            match decision {
                Decision::GiveUp { attempts } => {
                    logging::error(&format!(
                        "[host] giving up after {attempts} restart attempts"
                    ));
                    give_up(
                        app,
                        failure_reason(failure, &log),
                        failure.exit_status(),
                        log,
                    )
                    .await;
                    return false;
                }
                Decision::Retry { attempt, delay } => {
                    logging::warn(&format!(
                        "[host] restart attempt {attempt}/{MAX_RESTART_ATTEMPTS} in {}s",
                        delay.as_secs()
                    ));
                    tokio::time::sleep(delay).await;
                    restarting = true;
                }
            }
        }

        let (generation, port) = match launch(app, &state) {
            Ok(Some(launched)) => launched,
            Ok(None) => return false,
            Err(error) => {
                let reason = error.reason();
                logging::error(&format!("[host] could not launch the host: {reason}"));
                give_up(
                    app,
                    Some(reason),
                    ExitStatus::NotRecorded,
                    state.log_tail.joined(),
                )
                .await;
                return false;
            }
        };

        let timeout = if restarting {
            RETRY_READY_TIMEOUT
        } else {
            READY_TIMEOUT
        };
        match wait_for_ready(&state, generation, port, timeout).await {
            AttemptOutcome::Ready => {
                let running = state
                    .supervisor
                    .lock()
                    .map(|mut supervisor| {
                        let running = supervisor.on_ready(generation, Instant::now());
                        if running {
                            state.ready.store(true, Ordering::SeqCst);
                        }
                        running
                    })
                    .unwrap_or(false);
                if running {
                    crate::install_integrity::host_ready(app);
                    if restarting {
                        logging::info("[host] restart succeeded; the host is ready again");
                    }
                    adopt_host_culture(app, port).await;
                    return true;
                }
                pending = Some(Failure::Exited(exit_of(&state, generation).flatten()));
            }
            AttemptOutcome::Exited(code) => pending = Some(Failure::Exited(code)),
            AttemptOutcome::TimedOut => {
                logging::warn(&format!(
                    "[host] did not become ready within {}s; stopping this attempt",
                    timeout.as_secs()
                ));
                if !terminate_attempt(&state, generation, port).await {
                    logging::error(
                        "[host] the unresponsive host could not be stopped; not starting another one",
                    );
                    let log = state.log_tail.joined();
                    give_up(
                        app,
                        failure_reason(Failure::Unresponsive(timeout), &log),
                        ExitStatus::NotRecorded,
                        log,
                    )
                    .await;
                    return false;
                }
                pending = Some(Failure::Unresponsive(timeout));
            }
        }
    }
}

fn failure_reason(failure: Failure, log: &str) -> Option<String> {
    match failure {
        Failure::Unresponsive(timeout) => Some(localization::t_args(
            keys::HOST_ERROR_UNRESPONSIVE,
            &[("seconds", &timeout.as_secs().to_string())],
        )),
        Failure::Exited(code) => Some(exit_failure_reason(code, log)),
    }
}

async fn give_up(app: &AppHandle, reason: Option<String>, exit: ExitStatus, log: String) {
    crate::install_integrity::settle_before_error_window().await;
    let state = app.state::<Arc<HostState>>();
    let (ever_running, attempts) = match state.supervisor.lock() {
        Ok(mut supervisor) => {
            if supervisor.is_stopping() {
                return;
            }
            supervisor.give_up();
            (supervisor.ever_running(), supervisor.attempts())
        }
        Err(_) => (false, 0),
    };
    let kind = if ever_running {
        HostErrorKind::Stopped
    } else {
        HostErrorKind::NotStarted
    };
    let reason = match (kind, exit) {
        (HostErrorKind::Stopped, ExitStatus::Code(_) | ExitStatus::Unknown)
            if !detect_port_conflict(&log)
                && !matches!(
                    exit,
                    ExitStatus::Code(
                        HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE
                            | HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE
                    )
                ) =>
        {
            None
        }
        _ => reason,
    };
    host_error_window::show(
        app,
        HostErrorReport {
            kind,
            attempts,
            reason,
            exit,
            log,
        },
    );
}

async fn wait_for_ready(
    state: &HostState,
    generation: u64,
    port: u16,
    timeout: Duration,
) -> AttemptOutcome {
    let started = Instant::now();
    while Instant::now() < ready_deadline(started, state.heartbeat.last(), timeout) {
        if let Some(code) = exit_of(state, generation) {
            return AttemptOutcome::Exited(code);
        }
        if loopback_secret::prove(port, Duration::from_millis(500)).await {
            return AttemptOutcome::Ready;
        }
        tokio::time::sleep(Duration::from_millis(250)).await;
    }
    AttemptOutcome::TimedOut
}

async fn terminate_attempt(state: &HostState, generation: u64, port: u16) -> bool {
    if let Err(error) = post_shutdown(port, "unresponsive").await {
        logging::warn(&format!("[host] shutdown request failed: {error}"));
    }
    if wait_for_exit(state, generation, UNRESPONSIVE_STOP_TIMEOUT).await {
        return true;
    }
    logging::warn("[host] did not stop in time; killing the host process");
    kill_child(state);
    wait_for_exit(state, generation, FORCED_STOP_TIMEOUT).await
}

async fn wait_for_exit(state: &HostState, generation: u64, timeout: Duration) -> bool {
    let deadline = Instant::now() + timeout;
    loop {
        if exit_of(state, generation).is_some() {
            return true;
        }
        if Instant::now() >= deadline {
            return false;
        }
        tokio::time::sleep(STOP_POLL_INTERVAL).await;
    }
}

pub async fn stop(app: &AppHandle) {
    let state = app.state::<Arc<HostState>>();
    begin_stop(&state);
    if !host_to_stop(
        state.spawned(),
        state.adopted.load(Ordering::SeqCst),
        state.exited.load(Ordering::SeqCst),
    ) {
        return;
    }
    if state.stopping.swap(true, Ordering::SeqCst) {
        return;
    }
    stop_host(app, "quit", QUIT_STOP_TIMEOUT).await;
}

// A Windows session end and the macOS terminate: path destroy the event loop without request_quit.
// Blocking here keeps the process, and with it the host's job, alive until Server stopped is out.
pub fn stop_before_exit(app: &AppHandle) {
    let state = app.state::<Arc<HostState>>();
    if crate::is_quitting()
        || !host_to_stop(
            state.spawned(),
            state.adopted.load(Ordering::SeqCst),
            state.exited.load(Ordering::SeqCst),
        )
    {
        return;
    }
    logging::info("[host] exiting while the host is still running; stopping it first");
    crate::mark_quitting(app);
    state.stopping.store(true, Ordering::SeqCst);
    #[cfg(windows)]
    tauri::async_runtime::block_on(async {
        let stop = async {
            let port = begin_shutdown(app);
            stop_gracefully(app, port, SESSION_END_REASON, SESSION_END_STOP_TIMEOUT).await
        };
        if !matches!(
            tokio::time::timeout(SESSION_END_STOP_TIMEOUT, stop).await,
            Ok(true)
        ) {
            logging::warn("[host] still stopping as the session ends; the job object ends it");
        }
    });
    #[cfg(not(windows))]
    tauri::async_runtime::block_on(stop_host(app, "exit", QUIT_STOP_TIMEOUT));
}

pub fn host_to_stop(spawned: bool, adopted: bool, exited: bool) -> bool {
    (spawned || adopted) && !exited
}

pub async fn shutdown_for_update(app: &AppHandle) -> bool {
    stop_host(app, "update", UPDATE_STOP_TIMEOUT).await
}

pub async fn resume_after_failed_update(app: &AppHandle) {
    if !is_packaged() || crate::is_quitting() {
        return;
    }
    let state = app.state::<Arc<HostState>>();
    let resumed = state
        .supervisor
        .lock()
        .map(|mut supervisor| supervisor.resume())
        .unwrap_or(false);
    if !resumed {
        return;
    }
    state.shutdown_expected.store(false, Ordering::SeqCst);
    state.ready.store(false, Ordering::SeqCst);
    crate::install_integrity::host_gone();

    let answering = is_reachable(managed_port(&state), Duration::from_millis(300)).await;
    if previous_host_alive(
        state.spawned(),
        state.exited.load(Ordering::SeqCst),
        answering,
    ) {
        logging::error(
            "[host] the host could not be stopped for the update; not starting another one",
        );
        give_up(
            app,
            Some(localization::t(keys::UPDATE_HOST_STOP_FAILED)),
            ExitStatus::NotRecorded,
            state.log_tail.joined(),
        )
        .await;
        return;
    }
    logging::info("[host] restarting the host after the failed update");
    if supervise(app, None).await {
        crate::window::reload_main_window(app);
    }
}

fn previous_host_alive(spawned: bool, child_exited: bool, answering: bool) -> bool {
    if spawned {
        !child_exited
    } else {
        answering
    }
}

fn begin_stop(state: &HostState) {
    if let Ok(mut supervisor) = state.supervisor.lock() {
        supervisor.begin_stop();
    }
}

fn begin_shutdown(app: &AppHandle) -> Option<u16> {
    let state = app.state::<Arc<HostState>>();
    begin_stop(&state);
    state.shutdown_expected.store(true, Ordering::SeqCst);

    let port = managed_port(&state);
    if port.is_none() {
        logging::warn("[host] no loopback port known; skipping the shutdown request");
    }
    port
}

// The budget restarts once the host accepts, so a host that started late still gets its full shutdown.
async fn stop_gracefully(
    app: &AppHandle,
    port: Option<u16>,
    reason: &str,
    graceful_timeout: Duration,
) -> bool {
    let mut attempts = 0u32;
    let request = || {
        attempts += 1;
        let attempt = attempts;
        async move {
            let Some(port) = port else {
                return false;
            };
            match post_shutdown(port, reason).await {
                Ok(()) => {
                    if attempt > 1 {
                        logging::info(&format!(
                            "[host] the host accepted the shutdown request on attempt {attempt}"
                        ));
                    }
                    true
                }
                Err(error) => {
                    if attempt == 1 {
                        logging::warn(&format!(
                            "[host] shutdown request failed: {error}; retrying until the host accepts it"
                        ));
                    }
                    false
                }
            }
        }
    };
    request_until_stopped(request, || host_stopped(app, port), graceful_timeout).await
}

async fn request_until_stopped<Request, Requested, Stopped, Checked>(
    mut request: Request,
    mut stopped: Stopped,
    timeout: Duration,
) -> bool
where
    Request: FnMut() -> Requested,
    Requested: Future<Output = bool>,
    Stopped: FnMut() -> Checked,
    Checked: Future<Output = bool>,
{
    let mut deadline = Instant::now() + timeout;
    let mut accepted = false;
    let mut next_request = Instant::now();
    loop {
        let now = Instant::now();
        if !accepted && now >= next_request {
            let remaining = deadline.saturating_duration_since(now);
            accepted = matches!(tokio::time::timeout(remaining, request()).await, Ok(true));
            if accepted {
                deadline = Instant::now() + timeout;
            }
            next_request = Instant::now() + SHUTDOWN_RETRY_INTERVAL;
        }
        if stopped().await {
            return true;
        }
        if Instant::now() >= deadline {
            return false;
        }
        tokio::time::sleep(STOP_POLL_INTERVAL).await;
    }
}

async fn stop_host(app: &AppHandle, reason: &str, graceful_timeout: Duration) -> bool {
    let port = begin_shutdown(app);

    if stop_gracefully(app, port, reason, graceful_timeout).await {
        return true;
    }

    logging::warn("[host] did not stop in time; killing the host process");
    kill_child(&app.state::<Arc<HostState>>());
    if wait_until_stopped(app, port, FORCED_STOP_TIMEOUT).await {
        return true;
    }
    logging::error("[host] the host process is still running after the kill");
    false
}

#[cfg(test)]
pub fn covers_server_stopped_before_windows_ends_the_session(budget: Duration) -> bool {
    budget > HOST_SERVER_STOPPED_DISPATCH_WINDOW && budget < WINDOWS_END_SESSION_ALLOWANCE
}

#[cfg(test)]
pub fn covers_host_shutdown(graceful: Duration, forced: Duration) -> bool {
    graceful > HOST_SHUTDOWN_WORST_CASE && forced > Duration::ZERO
}

fn managed_port(state: &HostState) -> Option<u16> {
    resolve_managed_port(state.ui_port(), current_port())
}

pub fn resolve_managed_port(assigned: Option<u16>, port_file: Option<u16>) -> Option<u16> {
    assigned.or(port_file)
}

// Negates the pid to target its process group (the host is spawned with process_group(0), so its
// pid is also its pgid). 0 maps to None rather than -0, which would hit the bootstrapper's own group.
#[cfg(any(unix, test))]
pub fn process_group_signal_target(pid: u32) -> Option<i32> {
    if pid == 0 {
        return None;
    }
    i32::try_from(pid).ok().map(|pid| -pid)
}

fn kill_child(state: &HostState) {
    #[cfg(windows)]
    {
        if let Ok(slot) = state.job.lock() {
            if let Some(job) = slot.as_ref() {
                job.terminate();
                return;
            }
        }
    }

    if let Ok(mut slot) = state.child.lock() {
        if let Some((_, child)) = slot.as_mut() {
            #[cfg(unix)]
            {
                if let Some(target) = process_group_signal_target(child.id()) {
                    let killed = unsafe { libc::kill(target, libc::SIGKILL) } == 0;
                    if killed {
                        return;
                    }
                }
            }
            let _ = child.kill();
        }
    }
}

async fn wait_until_stopped(app: &AppHandle, port: Option<u16>, timeout: Duration) -> bool {
    let deadline = std::time::Instant::now() + timeout;
    loop {
        if host_stopped(app, port).await {
            return true;
        }
        if std::time::Instant::now() >= deadline {
            return false;
        }
        tokio::time::sleep(STOP_POLL_INTERVAL).await;
    }
}

async fn host_stopped(app: &AppHandle, port: Option<u16>) -> bool {
    let state = app.state::<Arc<HostState>>();
    let spawned = state.spawned();
    let answering = is_reachable(port, Duration::from_millis(300)).await;
    is_stopped(
        spawned,
        state.exited.load(Ordering::SeqCst),
        answering,
        binary_is_replaceable(app),
    )
}

pub fn is_stopped(
    spawned: bool,
    child_exited: bool,
    answering: bool,
    binary_replaceable: bool,
) -> bool {
    let process_gone = if spawned { child_exited } else { !answering };
    process_gone && binary_replaceable
}

fn binary_is_replaceable(app: &AppHandle) -> bool {
    if !cfg!(windows) {
        return true;
    }
    match host_binary_path(app) {
        Some(path) => std::fs::OpenOptions::new().write(true).open(path).is_ok(),
        None => true,
    }
}

async fn post_shutdown(port: u16, reason: &str) -> Result<(), reqwest::Error> {
    let client = loopback_secret::http_client(Duration::from_secs(3))?;
    let request = client.post(format!(
        "http://127.0.0.1:{port}/api/host/shutdown?reason={reason}"
    ));
    loopback_secret::authorize(request, port)
        .await
        .send()
        .await?
        .error_for_status()?;
    Ok(())
}

// Deliberately no `notes` here: the host clamps notification messages to
// 1000 chars, and the full changelog is not a notification - it stays in the
// bootstrapper's own state and reaches the UI through get_update_state / the
// update-state event instead.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct UpdateStateBody<'a> {
    version: Option<&'a str>,
    // A plain string rather than `UpdatePhase` directly: the cancel path
    // reports "cancelled" (see `report_cancelled`), a phase the in-process
    // `UpdatePhase` enum deliberately has no variant for, since cancelling
    // always leaves the state machine at `Available` so a retry is possible.
    phase: &'a str,
    published_at: Option<&'a str>,
    downloaded: Option<u64>,
    total: Option<u64>,
    // What the host actually keys its progress display off: its own progress
    // field is a plain `int`, which a raw byte count would overflow well
    // before a real installer (already past 2 GB for some platforms)
    // finished downloading. `downloaded`/`total` are still sent alongside for
    // any consumer that wants the raw counts.
    percent: Option<u8>,
    error: Option<&'a str>,
    failure: Option<UpdateFailure>,
    can_install: bool,
}

async fn post_update_state(port: u16, body: &UpdateStateBody<'_>) -> Result<(), reqwest::Error> {
    let client = loopback_secret::http_client(Duration::from_secs(3))?;
    let request = client
        .post(format!("http://127.0.0.1:{port}/api/host/update-state"))
        .json(body);
    loopback_secret::authorize(request, port)
        .await
        .send()
        .await?;
    Ok(())
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IntegrityPostResponse {
    NoSecret,
    TransportError,
    Status { code: u16, acknowledged: bool },
}

#[derive(Deserialize)]
struct IntegrityAck {
    accepted: bool,
}

pub async fn post_installation_integrity(
    app: &AppHandle,
    body: &crate::install_integrity::IntegrityReportBody,
) -> IntegrityPostResponse {
    let state = app.state::<Arc<HostState>>();
    let Some(port) = managed_port(&state) else {
        return IntegrityPostResponse::TransportError;
    };
    let Some(secret) = loopback_secret::secret_for(port).await else {
        return IntegrityPostResponse::NoSecret;
    };
    let Ok(client) = loopback_secret::http_client(Duration::from_secs(5)) else {
        return IntegrityPostResponse::TransportError;
    };
    let response = client
        .post(format!(
            "http://127.0.0.1:{port}/api/host/installation-integrity"
        ))
        .header(loopback_secret::HEADER, secret)
        .json(body)
        .send()
        .await;
    match response {
        Ok(response) => {
            let code = response.status().as_u16();
            let acknowledged = response
                .json::<IntegrityAck>()
                .await
                .is_ok_and(|ack| ack.accepted);
            IntegrityPostResponse::Status { code, acknowledged }
        }
        Err(_) => IntegrityPostResponse::TransportError,
    }
}

// A full-installation backup is not a three-second request like the other host calls here, so this one
// gets its own generous timeout.
pub const PRE_UPDATE_BACKUP_TIMEOUT: Duration = Duration::from_secs(600);

pub const QUIT_PRE_UPDATE_BACKUP_TIMEOUT: Duration = Duration::from_secs(120);

const PRE_UPDATE_BACKUP_POLL_INTERVAL: Duration = Duration::from_millis(500);
const BACKUP_STATUS_TIMEOUT: Duration = Duration::from_secs(2);

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PreUpdateBackupBody<'a> {
    version: Option<&'a str>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PreUpdateBackupResponse {
    pub success: bool,
    pub skipped: bool,
    #[serde(default)]
    pub cancelled: bool,
    pub error: Option<String>,
}

/// Turns the host's answer into a go/no-go for the update. Kept pure so the policy is testable without a
/// running host: an unreachable or unparseable answer must not read as permission to proceed.
pub fn interpret_pre_update_backup_response(
    response: Option<PreUpdateBackupResponse>,
) -> Result<(), PreUpdateBackupFailure> {
    let Some(response) = response else {
        return Err(PreUpdateBackupFailure::Failed(localization::t(
            keys::UPDATE_BACKUP_UNCONFIRMED,
        )));
    };

    if response.skipped || response.success {
        return Ok(());
    }

    if response.cancelled {
        return Err(PreUpdateBackupFailure::Cancelled);
    }

    Err(PreUpdateBackupFailure::Failed(
        response
            .error
            .unwrap_or_else(|| localization::t(keys::UPDATE_BACKUP_GENERIC_FAILURE)),
    ))
}

#[derive(Debug, PartialEq, Eq)]
pub enum PreUpdateBackupFailure {
    Failed(String),
    Cancelled,
}

pub async fn cancel_backup_operation(app: &AppHandle, operation_id: &str) {
    let state = app.state::<Arc<HostState>>();
    let Some(port) = managed_port(&state) else {
        return;
    };
    let Ok(client) = loopback_secret::http_client(BACKUP_STATUS_TIMEOUT) else {
        return;
    };
    let request = client.post(format!(
        "http://127.0.0.1:{port}/api/backups/operations/{operation_id}/cancel"
    ));
    if let Err(error) = loopback_secret::authorize(request, port).await.send().await {
        logging::warn(&format!(
            "[host] cancelling the pre-update backup failed: {error}"
        ));
    }
}

async fn post_pre_update_backup(
    port: u16,
    version: Option<&str>,
    timeout: Duration,
) -> Result<PreUpdateBackupResponse, reqwest::Error> {
    let client = loopback_secret::http_client(timeout)?;
    let request = client
        .post(format!(
            "http://127.0.0.1:{port}/api/backups/before-host-update"
        ))
        .json(&PreUpdateBackupBody { version });
    loopback_secret::authorize(request, port)
        .await
        .send()
        .await?
        .json::<PreUpdateBackupResponse>()
        .await
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BackupStatusResponse {
    pub operation_id: Option<String>,
    pub kind: String,
    pub stage: String,
    pub trigger: String,
    pub percent_complete: Option<i64>,
    pub eta_seconds: Option<i64>,
}

pub fn pre_update_backup_progress(
    bound: &mut Option<String>,
    status: &BackupStatusResponse,
) -> Option<BackupProgress> {
    let running = matches!(
        status.stage.as_str(),
        "Preparing" | "CreatingSnapshot" | "Encrypting" | "Saving" | "Validating"
    );
    if bound.is_none() && running && status.kind == "Create" && status.trigger == "BeforeHostUpdate"
    {
        bound.clone_from(&status.operation_id);
    }
    if !running || bound.is_none() || *bound != status.operation_id {
        return None;
    }
    Some(BackupProgress {
        operation_id: bound.clone()?,
        percent: status
            .percent_complete
            .map(|percent| percent.clamp(0, 100) as u8),
        eta_seconds: status
            .eta_seconds
            .and_then(|seconds| u64::try_from(seconds).ok()),
    })
}

async fn get_backup_status(port: u16) -> Result<BackupStatusResponse, reqwest::Error> {
    let client = loopback_secret::http_client(BACKUP_STATUS_TIMEOUT)?;
    let request = client.get(format!("http://127.0.0.1:{port}/api/backups/status"));
    loopback_secret::authorize(request, port)
        .await
        .send()
        .await?
        .json::<BackupStatusResponse>()
        .await
}

pub async fn create_pre_update_backup(
    app: &AppHandle,
    version: Option<&str>,
    timeout: Duration,
    on_progress: impl Fn(Option<BackupProgress>),
) -> Result<(), PreUpdateBackupFailure> {
    let state = app.state::<Arc<HostState>>();
    let Some(port) = managed_port(&state) else {
        return Err(PreUpdateBackupFailure::Failed(localization::t(
            keys::UPDATE_BACKUP_HOST_UNREACHABLE,
        )));
    };

    let post = post_pre_update_backup(port, version, timeout);
    tokio::pin!(post);
    let mut bound = None;
    let outcome = loop {
        tokio::select! {
            result = &mut post => break result,
            () = tokio::time::sleep(PRE_UPDATE_BACKUP_POLL_INTERVAL) => {
                if let Ok(status) = get_backup_status(port).await {
                    on_progress(pre_update_backup_progress(&mut bound, &status));
                }
            }
        }
    };
    on_progress(None);

    match outcome {
        Ok(response) => interpret_pre_update_backup_response(Some(response)),
        Err(error) => {
            logging::warn(&format!("[host] pre-update backup request failed: {error}"));
            interpret_pre_update_backup_response(None)
        }
    }
}

pub async fn report_update_state(app: &AppHandle, snapshot: &UpdateSnapshot) -> bool {
    report_update_state_as(app, snapshot, snapshot.phase.as_str()).await
}

// Cancelling is reported as its own phase string, distinct from the
// in-process `UpdatePhase::Available` that `record_cancelled` leaves the
// state machine at (so a retry stays possible). Without this, a cancel would
// be indistinguishable from a plain re-announcement of the same available
// version, and the host's dedupe would leave the progress-bearing
// notification stuck (issue #249).
pub async fn report_cancelled(app: &AppHandle, snapshot: &UpdateSnapshot) -> bool {
    report_update_state_as(app, snapshot, "cancelled").await
}

async fn report_update_state_as(app: &AppHandle, snapshot: &UpdateSnapshot, phase: &str) -> bool {
    let state = app.state::<Arc<HostState>>();
    let Some(port) = managed_port(&state) else {
        logging::warn("[host] no loopback port known; skipping the update-state report");
        return false;
    };

    let body = update_state_body(snapshot, phase);

    match post_update_state(port, &body).await {
        Ok(()) => true,
        Err(error) => {
            logging::warn(&format!("[host] update-state report failed: {error}"));
            false
        }
    }
}

fn update_state_body<'a>(snapshot: &'a UpdateSnapshot, phase: &'a str) -> UpdateStateBody<'a> {
    UpdateStateBody {
        version: snapshot.version.as_deref(),
        phase,
        published_at: snapshot.published_at.as_deref(),
        downloaded: snapshot
            .progress
            .as_ref()
            .map(|progress| progress.downloaded),
        total: snapshot
            .progress
            .as_ref()
            .and_then(|progress| progress.total),
        percent: snapshot
            .progress
            .as_ref()
            .and_then(|progress| progress.percent),
        error: snapshot.error.as_deref(),
        failure: snapshot.failure,
        can_install: snapshot.install_strategy.installs_in_app(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::release_notes::ReleaseNotes;
    use crate::update_channel::UpdateChannel;
    use crate::update_state::UpdateState;
    use crate::updater::UpdateInstallStrategy;

    fn backup_status(operation_id: &str, trigger: &str, stage: &str) -> BackupStatusResponse {
        BackupStatusResponse {
            operation_id: Some(operation_id.to_string()),
            kind: "Create".to_string(),
            stage: stage.to_string(),
            trigger: trigger.to_string(),
            percent_complete: Some(40),
            eta_seconds: Some(90),
        }
    }

    #[test]
    fn a_cancelled_backup_stops_the_update_as_cancelled() {
        let response = PreUpdateBackupResponse {
            success: false,
            skipped: false,
            cancelled: true,
            error: None,
        };

        assert_eq!(
            interpret_pre_update_backup_response(Some(response)),
            Err(PreUpdateBackupFailure::Cancelled)
        );
    }

    #[test]
    fn the_backup_made_for_the_update_is_followed() {
        let mut bound = None;

        let progress = pre_update_backup_progress(
            &mut bound,
            &backup_status("update", "BeforeHostUpdate", "Saving"),
        );

        assert_eq!(
            progress,
            Some(BackupProgress {
                operation_id: "update".to_string(),
                percent: Some(40),
                eta_seconds: Some(90)
            })
        );
    }

    #[test]
    fn a_scheduled_backup_holding_the_lease_is_not_shown_as_the_update_backup() {
        let mut bound = None;

        let progress = pre_update_backup_progress(
            &mut bound,
            &backup_status("scheduled", "Scheduled", "Saving"),
        );

        assert_eq!(progress, None);
        assert_eq!(bound, None);
    }

    #[test]
    fn a_later_backup_does_not_replace_the_one_already_followed() {
        let mut bound = None;
        pre_update_backup_progress(
            &mut bound,
            &backup_status("update", "BeforeHostUpdate", "Saving"),
        );

        let progress = pre_update_backup_progress(
            &mut bound,
            &backup_status("other", "BeforeHostUpdate", "Saving"),
        );

        assert_eq!(progress, None);
    }

    #[test]
    fn a_finished_backup_from_an_earlier_attempt_is_not_followed() {
        let mut bound = None;

        let progress = pre_update_backup_progress(
            &mut bound,
            &backup_status("earlier", "BeforeHostUpdate", "Completed"),
        );

        assert_eq!(progress, None);
        assert_eq!(bound, None);
    }

    fn idle_update_state() -> UpdateState {
        UpdateState::idle(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        )
    }

    #[test]
    fn a_failed_check_tells_the_host_it_was_the_check_that_failed() {
        let mut state = idle_update_state();
        state.record_available(1, "3.1.0".to_string(), ReleaseNotes::Empty, None, None);
        state.record_check_failed(2, "feed unreachable".to_string());
        let snapshot = state.snapshot();

        let body = serde_json::to_value(update_state_body(&snapshot, "failed")).unwrap();

        assert_eq!(body["phase"], "failed");
        assert_eq!(body["failure"], "check");
        assert_eq!(body["version"], "3.1.0");
    }

    #[test]
    fn a_failed_download_tells_the_host_it_was_the_install_that_failed() {
        let mut state = idle_update_state();
        state.record_available(1, "3.1.0".to_string(), ReleaseNotes::Empty, None, None);
        assert!(state.try_begin_download());
        state.record_install_failed("connection reset".to_string());
        let snapshot = state.snapshot();

        let body = serde_json::to_value(update_state_body(&snapshot, "failed")).unwrap();

        assert_eq!(body["failure"], "install");
    }

    #[test]
    fn an_up_to_date_report_carries_no_version_and_no_failure() {
        let mut state = idle_update_state();
        state.record_check_failed(1, "feed unreachable".to_string());
        state.record_up_to_date(2, None);
        let snapshot = state.snapshot();

        let body = serde_json::to_value(update_state_body(&snapshot, "upToDate")).unwrap();

        assert!(body["version"].is_null());
        assert!(body["failure"].is_null());
    }

    #[test]
    fn a_successful_backup_lets_the_update_proceed() {
        let response = PreUpdateBackupResponse {
            success: true,
            skipped: false,
            cancelled: false,
            error: None,
        };

        assert!(interpret_pre_update_backup_response(Some(response)).is_ok());
    }

    #[test]
    fn a_disabled_trigger_lets_the_update_proceed() {
        let response = PreUpdateBackupResponse {
            success: false,
            skipped: true,
            error: None,
            cancelled: false,
        };

        assert!(interpret_pre_update_backup_response(Some(response)).is_ok());
    }

    #[test]
    fn a_failed_backup_stops_the_update_and_says_why() {
        let response = PreUpdateBackupResponse {
            success: false,
            skipped: false,
            cancelled: false,
            error: Some("the disk is full".to_string()),
        };

        let error = interpret_pre_update_backup_response(Some(response)).unwrap_err();

        assert_eq!(
            error,
            PreUpdateBackupFailure::Failed("the disk is full".to_string())
        );
    }

    // Fail closed: a host that is too old to know this endpoint, or that died mid-backup, answers with
    // nothing. Treating that as permission would update over an installation that was never backed up.
    #[test]
    fn an_unanswered_request_stops_the_update() {
        assert!(interpret_pre_update_backup_response(None).is_err());
    }

    // An AppImage path only ever reaches this on Linux, and it has to stay a POSIX path to be the
    // absolute one the code looks for - Windows would read /home/u as relative and pick neither.
    #[cfg(unix)]
    #[test]
    fn autostart_prefers_the_outer_appimage_path() {
        let appimage = PathBuf::from("/home/u/MacroDeck.AppImage");
        let current_exe = PathBuf::from("/tmp/.mount_x/usr/bin/MacroDeck");
        assert_eq!(
            autostart_executable(Some(appimage.clone()), Some(current_exe), |_| true),
            Some(appimage)
        );
    }

    #[cfg(unix)]
    #[test]
    fn autostart_falls_back_when_the_appimage_path_is_gone() {
        let appimage = PathBuf::from("/home/u/MacroDeck.AppImage");
        let current_exe = PathBuf::from("/tmp/.mount_x/usr/bin/MacroDeck");
        assert_eq!(
            autostart_executable(Some(appimage), Some(current_exe.clone()), |_| false),
            Some(current_exe)
        );
    }

    #[test]
    fn autostart_ignores_a_relative_appimage_value() {
        let current_exe = PathBuf::from("/usr/bin/MacroDeck");
        assert_eq!(
            autostart_executable(
                Some(PathBuf::from("MacroDeck.AppImage")),
                Some(current_exe.clone()),
                |_| true
            ),
            Some(current_exe)
        );
    }

    #[test]
    fn autostart_uses_the_executable_without_an_appimage() {
        let current_exe = PathBuf::from("/usr/bin/MacroDeck");
        assert_eq!(
            autostart_executable(None, Some(current_exe.clone()), |_| true),
            Some(current_exe)
        );
    }

    #[test]
    fn autostart_has_no_path_without_either() {
        assert_eq!(autostart_executable(None, None, |_| true), None);
    }

    #[test]
    fn parses_valid_ports() {
        assert_eq!(parse_port("5191\n"), Some(5191));
        assert_eq!(parse_port(" 8193 "), Some(8193));
    }

    #[test]
    fn rejects_invalid_ports() {
        assert_eq!(parse_port(""), None);
        assert_eq!(parse_port("0"), None);
        assert_eq!(parse_port("-4"), None);
        assert_eq!(parse_port("70000"), None);
        assert_eq!(parse_port("abc"), None);
    }

    #[test]
    fn choose_port_reuses_free_persisted_port() {
        assert_eq!(choose_port(Some(4711), |_| true, || Some(9999)), Some(4711));
    }

    #[test]
    fn choose_port_reallocates_when_persisted_port_is_taken() {
        assert_eq!(
            choose_port(Some(4711), |_| false, || Some(9999)),
            Some(9999)
        );
    }

    #[test]
    fn choose_port_allocates_without_persisted_port() {
        assert_eq!(choose_port(None, |_| true, || Some(9999)), Some(9999));
    }

    #[test]
    fn adoption_requires_port_file_to_match_persisted_port() {
        assert_eq!(adoption_candidate(None, Some(5191), Some(62309)), None);
        assert_eq!(
            adoption_candidate(None, Some(62309), Some(62309)),
            Some(62309)
        );
    }

    #[test]
    fn adoption_rejects_missing_port_sources() {
        assert_eq!(adoption_candidate(None, None, Some(62309)), None);
        assert_eq!(adoption_candidate(None, Some(62309), None), None);
        assert_eq!(adoption_candidate(None, None, None), None);
    }

    #[test]
    fn adoption_env_override_wins() {
        assert_eq!(
            adoption_candidate(Some(5191), Some(1234), Some(62309)),
            Some(5191)
        );
        assert_eq!(adoption_candidate(Some(5191), None, None), Some(5191));
    }

    #[test]
    fn a_proven_host_of_the_same_version_is_adopted() {
        assert_eq!(
            adoption_decision(true, Some("3.0.0-beta.19"), "3.0.0-beta.19"),
            Adoption::Adopt
        );
    }

    #[test]
    fn a_proven_host_of_an_older_or_newer_version_is_replaced() {
        assert_eq!(
            adoption_decision(true, Some("3.0.0-beta.14"), "3.0.0-beta.19"),
            Adoption::Replace
        );
        assert_eq!(
            adoption_decision(true, Some("3.0.0-beta.20"), "3.0.0-beta.19"),
            Adoption::Replace
        );
    }

    #[test]
    fn a_proven_host_whose_version_cannot_be_read_is_replaced() {
        assert_eq!(
            adoption_decision(true, None, "3.0.0-beta.19"),
            Adoption::Replace
        );
    }

    #[test]
    fn an_unproven_listener_is_never_adopted_or_replaced() {
        assert_eq!(
            adoption_decision(false, Some("3.0.0-beta.19"), "3.0.0-beta.19"),
            Adoption::Ignore
        );
        assert_eq!(
            adoption_decision(false, None, "3.0.0-beta.19"),
            Adoption::Ignore
        );
    }

    #[test]
    fn trusted_status_requires_explicit_trusted_true() {
        assert!(parse_trusted_status(
            r#"{"setupComplete":true,"authenticated":true,"trusted":true,"scope":"admin","username":null}"#
        ));
        assert!(!parse_trusted_status(
            r#"{"setupComplete":true,"authenticated":false,"trusted":false,"scope":null,"username":null}"#
        ));
        assert!(!parse_trusted_status(r#"{"setupComplete":true}"#));
        assert!(!parse_trusted_status("<!doctype html><html></html>"));
        assert!(!parse_trusted_status(""));
    }

    #[test]
    fn managed_port_prefers_the_assigned_loopback_port() {
        assert_eq!(resolve_managed_port(Some(62309), Some(5191)), Some(62309));
        assert_eq!(resolve_managed_port(None, Some(5191)), Some(5191));
        assert_eq!(resolve_managed_port(None, None), None);
    }

    #[test]
    fn a_spawned_host_counts_as_stopped_only_once_the_child_exited() {
        // Regression guard for issue #131: the host keeps running (and keeps its
        // binary locked) after Kestrel stopped answering, so "not answering" must
        // never be mistaken for "stopped".
        assert!(!is_stopped(true, false, false, true));
        assert!(is_stopped(true, true, false, true));
    }

    #[test]
    fn an_adopted_host_counts_as_stopped_once_it_stops_answering() {
        assert!(!is_stopped(false, false, true, true));
        assert!(is_stopped(false, false, false, true));
    }

    #[test]
    fn quitting_stops_an_adopted_host_as_well_as_a_spawned_one() {
        assert!(host_to_stop(false, true, false));
        assert!(host_to_stop(true, false, false));
        assert!(!host_to_stop(false, false, false));
        assert!(!host_to_stop(false, true, true));
        assert!(!host_to_stop(true, false, true));
    }

    #[test]
    fn a_listening_port_is_not_free_even_when_nothing_accepts_on_it() {
        let listener = std::net::TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = listener.local_addr().unwrap().port();
        assert!(!is_port_free(port));

        drop(listener);
        assert!(is_port_free(port));
    }

    #[test]
    fn an_adopted_host_gets_its_worst_case_shutdown_before_macro_deck_relaunches() {
        assert!(ADOPTED_HOST_EXIT_GRACE > HOST_SHUTDOWN_WORST_CASE);
    }

    #[test]
    fn a_host_that_survived_the_update_stop_is_not_started_a_second_time() {
        assert!(previous_host_alive(true, false, false));
        assert!(previous_host_alive(false, false, true));
    }

    #[test]
    fn a_host_that_is_gone_after_a_failed_update_stop_is_started_again() {
        assert!(!previous_host_alive(true, true, false));
        assert!(!previous_host_alive(false, false, false));
    }

    #[test]
    fn a_locked_binary_keeps_the_host_from_counting_as_stopped() {
        assert!(!is_stopped(true, true, false, false));
        assert!(!is_stopped(false, false, false, false));
    }

    #[test]
    fn port_conflict_is_detected_case_insensitively() {
        assert!(detect_port_conflict(
            "Failed to bind to address http://0.0.0.0:8193"
        ));
        assert!(detect_port_conflict("Address already in use"));
        assert!(!detect_port_conflict("Unhandled exception"));
    }

    #[test]
    fn a_failure_without_a_port_conflict_says_the_host_did_not_start() {
        assert!(startup_failure_reason("boom").contains("did not start"));
        assert!(startup_failure_reason("").contains("did not start"));
    }

    #[test]
    fn a_port_conflict_is_explained() {
        let message = startup_failure_reason("Failed to bind to address");
        assert!(message.contains("port "));
        assert!(message.contains("is already in use"));
        assert!(message.contains("MACRO_DECK_PORT"));
    }

    #[test]
    fn a_port_conflict_names_the_port_the_host_tried() {
        let message = startup_failure_reason("Failed to bind to address http://0.0.0.0:9100.");
        assert!(message.contains("port 9100"));
    }

    #[test]
    fn a_port_conflict_falls_back_to_the_default_port_without_an_address() {
        let message = startup_failure_reason("Address already in use");
        assert!(message.contains(&format!("port {}", host_public_port())));
    }

    #[test]
    fn conflicting_port_is_parsed_from_the_bind_failure() {
        assert_eq!(
            conflicting_port_from_log("Failed to bind to address http://0.0.0.0:9100."),
            Some(9100)
        );
        assert_eq!(conflicting_port_from_log("Address already in use"), None);
        assert_eq!(conflicting_port_from_log(""), None);
    }

    #[test]
    fn conflicting_port_ignores_unrelated_addresses_in_the_tail() {
        let tail = "info: Now listening on: http://127.0.0.1:51234\n\
                    info: Public listener on 0.0.0.0:9100\n\
                    crit: Failed to bind to address http://0.0.0.0:9100.\n";

        assert_eq!(conflicting_port_from_log(tail), Some(9100));
    }

    #[test]
    fn an_unavailable_cryptography_service_is_explained_with_the_fix() {
        let message = exit_failure_reason(Some(HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE), "boom");
        assert!(message.contains("cryptography service"));
        assert!(message.contains("CNG Key Isolation"));
    }

    #[test]
    fn a_database_from_a_newer_version_is_explained_instead_of_the_log() {
        let message = exit_failure_reason(Some(HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE), "boom");
        assert!(message.contains("newer version of Macro Deck"));
        assert!(!message.contains("did not start"));
    }

    #[test]
    fn the_newer_database_exit_code_matches_the_host_constant() {
        // Keep in sync with HostExitCodes.DatabaseFromNewerVersion in the .NET host.
        assert_eq!(HOST_DATABASE_FROM_NEWER_VERSION_EXIT_CODE, 88);
    }

    #[test]
    fn any_other_exit_code_keeps_the_log_based_reason() {
        assert!(exit_failure_reason(Some(1), "boom").contains("did not start"));
        assert!(exit_failure_reason(None, "Address already in use").contains("is already in use"));
    }

    #[test]
    fn the_cryptography_exit_code_matches_the_host_constant() {
        // Keep in sync with HostExitCodes.CryptographyUnavailable in the .NET host.
        assert_eq!(HOST_CRYPTOGRAPHY_UNAVAILABLE_EXIT_CODE, 87);
    }

    #[test]
    fn the_restart_exit_code_matches_the_host_constant() {
        // Keep in sync with HostExitCodes.RestartRequested in the .NET host.
        assert_eq!(HOST_RESTART_EXIT_CODE, 86);
    }

    #[test]
    fn relaunch_prefers_the_outer_appimage_path() {
        let appimage = PathBuf::from("/home/u/MacroDeck.AppImage");
        let current_exe = PathBuf::from("/tmp/.mount_x/usr/bin/MacroDeck");
        assert_eq!(
            relaunch_target(Some(appimage.clone()), Some(current_exe)),
            Some(appimage)
        );
    }

    #[test]
    fn relaunch_forwards_everything_but_deep_links() {
        let args = [
            "MacroDeck",
            "--autostart",
            "/tmp/Default.macroDeckProfile",
            "macrodeck://store/acme.obs",
            "MACRODECK://STORE/acme.other",
            "--minimized",
        ]
        .map(OsString::from);

        assert_eq!(
            relaunch_arguments(args.into_iter()),
            [
                "--autostart",
                "/tmp/Default.macroDeckProfile",
                "--minimized"
            ]
            .map(OsString::from)
        );
    }

    #[test]
    fn relaunch_uses_the_running_binary_without_an_appimage() {
        let current_exe = PathBuf::from("/Applications/Macro Deck.app/Contents/MacOS/MacroDeck");
        assert_eq!(
            relaunch_target(None, Some(current_exe.clone())),
            Some(current_exe)
        );
        assert_eq!(relaunch_target(None, None), None);
    }

    #[test]
    fn public_port_override_accepts_only_valid_values() {
        assert_eq!(resolve_public_port(Some("45123")), 45123);
        assert_eq!(resolve_public_port(Some("0")), host_public_port());
        assert_eq!(resolve_public_port(Some("70000")), host_public_port());
        assert_eq!(resolve_public_port(Some("invalid")), host_public_port());
    }

    #[test]
    fn build_channel_defaults_keep_development_isolated_from_releases() {
        assert_eq!(default_public_port("Development"), 7193);
        assert_eq!(default_public_port("Production"), 8193);
        assert_eq!(
            loopback_port_file_name("Development"),
            "macro-deck-host-development.port"
        );
        assert_eq!(
            loopback_port_file_name("Production"),
            "macro-deck-host.port"
        );
        assert_eq!(data_root_directory_name("Development"), ".data");
        assert_eq!(data_root_directory_name("Production"), "MacroDeck");
        assert_eq!(host_public_port(), default_public_port(BUILD_CHANNEL));
        assert_eq!(
            host_data_root_directory_name(),
            data_root_directory_name(BUILD_CHANNEL)
        );
        assert_eq!(port_file_name(), loopback_port_file_name(BUILD_CHANNEL));
    }

    #[test]
    fn drain_into_tail_keeps_reading_past_output_that_is_not_utf8() {
        let tail = LogTail::new(10);

        drain_into_tail(
            std::io::Cursor::new(b"first\n\xff\xfe broken\nlast\n".to_vec()),
            &tail,
            &HostHeartbeat::default(),
        );

        let lines = tail.joined();
        assert!(lines.contains("first"));
        assert!(lines.contains("broken"));
        assert!(lines.contains("last"));
    }

    #[test]
    fn the_heartbeat_line_matches_the_host_constant() {
        assert_eq!(HOST_HEARTBEAT_LINE, "macrodeck-host-heartbeat");
    }

    #[test]
    fn a_heartbeat_line_counts_as_a_sign_of_life_and_stays_out_of_the_log() {
        let tail = LogTail::new(10);
        let heartbeat = HostHeartbeat::default();
        let before = Instant::now();

        drain_into_tail(
            std::io::Cursor::new(b"applying restore\nmacrodeck-host-heartbeat\r\n".to_vec()),
            &tail,
            &heartbeat,
        );

        assert!(heartbeat.last().is_some_and(|beat| beat >= before));
        assert_eq!(tail.joined(), "applying restore");
    }

    #[test]
    fn a_host_still_applying_a_restore_is_not_timed_out() {
        let started = Instant::now();
        let heartbeat = HostHeartbeat::default();
        heartbeat.beat(started + Duration::from_secs(55));

        let deadline = ready_deadline(started, heartbeat.last(), READY_TIMEOUT);

        assert!(deadline > started + READY_TIMEOUT + Duration::from_secs(50));
    }

    #[test]
    fn the_ready_wait_keeps_waiting_while_heartbeats_arrive_and_times_out_after_they_stop() {
        let state = HostState::new();
        let port = allocate_free_port().expect("a free loopback port");
        let timeout = Duration::from_secs(2);
        let beats_for = Duration::from_secs(3);
        let heartbeat = state.heartbeat.clone();
        let started = Instant::now();
        let beater = std::thread::spawn(move || {
            while started.elapsed() < beats_for {
                heartbeat.beat(Instant::now());
                std::thread::sleep(Duration::from_millis(100));
            }
        });

        let outcome = tauri::async_runtime::block_on(wait_for_ready(&state, 1, port, timeout));
        let elapsed = started.elapsed();
        beater.join().expect("the heartbeat thread");

        assert!(matches!(outcome, AttemptOutcome::TimedOut));
        assert!(
            elapsed >= beats_for,
            "timed out after {elapsed:?}, while heartbeats were still arriving"
        );
    }

    #[test]
    fn a_host_that_reports_nothing_still_times_out_after_the_ready_timeout() {
        let started = Instant::now();

        assert_eq!(
            ready_deadline(started, None, RETRY_READY_TIMEOUT),
            started + RETRY_READY_TIMEOUT
        );
    }

    #[test]
    fn a_heartbeat_from_before_the_attempt_does_not_shorten_its_timeout() {
        let earlier = Instant::now();
        let started = earlier + Duration::from_secs(10);

        assert_eq!(
            ready_deadline(started, Some(earlier), READY_TIMEOUT),
            started + READY_TIMEOUT
        );
    }

    #[test]
    fn process_group_signal_target_negates_the_pid() {
        assert_eq!(process_group_signal_target(35363), Some(-35363));
    }

    #[test]
    fn process_group_signal_target_refuses_pid_zero() {
        // 0 would negate to 0, signalling the bootstrapper's own process group instead of the host's.
        assert_eq!(process_group_signal_target(0), None);
    }

    #[test]
    fn the_real_timeouts_cover_the_hosts_worst_case_shutdown() {
        assert!(covers_host_shutdown(QUIT_STOP_TIMEOUT, FORCED_STOP_TIMEOUT));
        assert!(covers_host_shutdown(
            UPDATE_STOP_TIMEOUT,
            FORCED_STOP_TIMEOUT
        ));
    }

    #[test]
    fn replacing_a_host_of_another_version_waits_out_its_worst_case_shutdown() {
        assert!(REPLACED_HOST_STOP_TIMEOUT > HOST_SHUTDOWN_WORST_CASE);
    }

    #[test]
    fn the_session_end_reason_is_the_one_the_host_recognises() {
        assert_eq!(SESSION_END_REASON, "session-end");
    }

    #[test]
    fn the_session_end_budget_sends_server_stopped_before_windows_ends_the_session() {
        assert!(covers_server_stopped_before_windows_ends_the_session(
            SESSION_END_STOP_TIMEOUT
        ));
    }

    #[test]
    fn the_quit_budget_would_outlast_windows_session_end_allowance() {
        assert!(!covers_server_stopped_before_windows_ends_the_session(
            QUIT_STOP_TIMEOUT
        ));
    }

    #[test]
    fn the_old_shipped_timeouts_did_not_cover_the_hosts_worst_case_shutdown() {
        assert!(!covers_host_shutdown(
            Duration::from_secs(5),
            Duration::from_secs(5)
        ));
    }

    #[test]
    fn host_binary_names_match_the_published_launchers() {
        assert_eq!(
            host_binary_name_for("Development", true),
            "MacroDeckHostDevelopment.exe"
        );
        assert_eq!(
            host_binary_name_for("Production", true),
            "MacroDeckHost.exe"
        );
        assert_eq!(
            host_binary_name_for("Development", false),
            "Macro Deck Host"
        );
        assert_eq!(host_binary_name_for("Production", false), "Macro Deck Host");
    }

    fn serve_host_that_listens_after(delay: Duration) -> (u16, Arc<AtomicBool>) {
        let port = allocate_free_port().expect("a free loopback port");
        let exited = Arc::new(AtomicBool::new(false));
        let flag = exited.clone();
        std::thread::spawn(move || {
            use std::io::{Read, Write};
            std::thread::sleep(delay);
            let listener = std::net::TcpListener::bind(("127.0.0.1", port)).unwrap();
            for stream in listener.incoming() {
                let Ok(mut stream) = stream else {
                    continue;
                };
                let mut request = Vec::new();
                let mut buffer = [0u8; 4096];
                while !request.windows(4).any(|window| window == b"\r\n\r\n") {
                    match stream.read(&mut buffer) {
                        Ok(0) | Err(_) => break,
                        Ok(read) => request.extend_from_slice(&buffer[..read]),
                    }
                }
                let shutdown = request.starts_with(b"POST /api/host/shutdown");
                let status = if shutdown { "200 OK" } else { "404 Not Found" };
                let _ = stream.write_all(
                    format!("HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
                        .as_bytes(),
                );
                if shutdown {
                    flag.store(true, Ordering::SeqCst);
                    return;
                }
            }
        });
        (port, exited)
    }

    #[test]
    fn a_host_that_starts_listening_after_the_quit_still_gets_the_shutdown_request() {
        let (port, exited) = serve_host_that_listens_after(Duration::from_millis(700));
        let started = Instant::now();

        let stopped = tauri::async_runtime::block_on(request_until_stopped(
            || async move { post_shutdown(port, "quit").await.is_ok() },
            || {
                let exited = exited.clone();
                async move { exited.load(Ordering::SeqCst) }
            },
            Duration::from_secs(5),
        ));

        assert!(
            stopped,
            "the host was never asked to shut down once it listened"
        );
        assert!(started.elapsed() < Duration::from_secs(5));
    }

    #[test]
    fn a_refused_shutdown_request_does_not_count_as_delivered() {
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        std::thread::spawn(move || {
            use std::io::{Read, Write};
            for mut stream in listener.incoming().flatten() {
                let mut buffer = [0u8; 4096];
                let _ = stream.read(&mut buffer);
                let _ = stream.write_all(
                    b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                );
            }
        });

        let result = tauri::async_runtime::block_on(post_shutdown(port, "quit"));

        assert!(result.is_err());
    }

    #[test]
    fn a_host_that_accepts_the_request_late_still_gets_the_whole_graceful_budget() {
        let timeout = Duration::from_secs(3);
        let started = Instant::now();
        let accepted_from = Duration::from_millis(1500);
        let exits_at = Duration::from_millis(3500);

        let stopped = tauri::async_runtime::block_on(request_until_stopped(
            || async move { started.elapsed() >= accepted_from },
            || async move { started.elapsed() >= exits_at },
            timeout,
        ));

        assert!(
            stopped,
            "the host was given up on before its shutdown could finish"
        );
    }
}
