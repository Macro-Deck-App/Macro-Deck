// .NET host lifecycle: the packaged bootstrapper is the installed entry point,
// spawns the sibling MacroDeckHost binary, waits until it answers and stops it
// again on quit (graceful shutdown endpoint first, kill as fallback).

use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU16, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::{Deserialize, Serialize};
use tauri::AppHandle;
use tauri::Manager;

use crate::localization::{self, keys};
use crate::logging::{self, LogTail};
use crate::update_state::UpdateSnapshot;
use crate::updater::UpdateInstallStrategy;

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

// Mirrors the host's own configured HostOptions.ShutdownTimeout (not the shorter per-plugin grace
// inside it), so the two budgets cannot silently drift apart.
#[cfg(test)]
pub const HOST_SHUTDOWN_WORST_CASE: Duration = Duration::from_secs(30);

pub const HOST_RESTART_EXIT_CODE: i32 = 86;

pub const RELAUNCH_MARKER_ENVIRONMENT_VARIABLE: &str = "MACRODECK_RELAUNCH";

pub const RELAUNCH_SETTLE_DELAY: Duration = Duration::from_millis(1500);

const STOP_POLL_INTERVAL: Duration = Duration::from_millis(250);

pub struct HostState {
    child: Mutex<Option<Child>>,
    #[cfg(windows)]
    job: Mutex<Option<crate::host_job::HostJob>>,
    pub exited: AtomicBool,
    pub ready: AtomicBool,
    pub shutdown_expected: AtomicBool,
    pub log_tail: Arc<LogTail>,
    stopping: AtomicBool,
    ui_port: AtomicU16,
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
            log_tail: Arc::new(LogTail::new(40)),
            stopping: AtomicBool::new(false),
            ui_port: AtomicU16::new(0),
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

fn read_persisted_port(config_dir: &Path) -> Option<u16> {
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

async fn is_trusted_loopback(port: u16, timeout: Duration) -> bool {
    let Ok(client) = reqwest::Client::builder().timeout(timeout).build() else {
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
    let Ok(client) = reqwest::Client::builder().timeout(timeout).build() else {
        return false;
    };
    match client
        .get(format!("http://127.0.0.1:{port}/api/system/version"))
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

pub fn startup_error_message(log: &str) -> String {
    let port = conflicting_port_from_log(log)
        .unwrap_or_else(public_port)
        .to_string();

    if detect_port_conflict(log) {
        if log.is_empty() {
            localization::t_args(keys::ERRORS_PORT_IN_USE, &[("port", &port)])
        } else {
            localization::t_args(
                keys::ERRORS_PORT_IN_USE_WITH_LOG,
                &[("port", &port), ("log", log)],
            )
        }
    } else if log.is_empty() {
        localization::t(keys::ERRORS_HOST_DID_NOT_START)
    } else {
        localization::t_args(keys::ERRORS_HOST_DID_NOT_START_WITH_LOG, &[("log", log)])
    }
}

fn pipe_to_tail<R: std::io::Read + Send + 'static>(reader: R, tail: Arc<LogTail>) {
    std::thread::spawn(move || {
        let reader = BufReader::new(reader);
        for line in reader.lines().map_while(Result::ok) {
            tail.push_chunk(&crate::redact::redact(&line));
        }
    });
}

pub async fn ensure_running(app: &AppHandle) -> bool {
    let state = app.state::<Arc<HostState>>();
    if !is_packaged() {
        state.ready.store(true, Ordering::SeqCst);
        return true;
    }

    // None only when the home directory is unresolvable. The port file tolerates that and falls
    // back to the process cwd; the bundle extraction below deliberately does not.
    let config_dir = app.path().app_config_dir().ok();
    let port_file_dir = config_dir.as_deref().unwrap_or(Path::new("."));

    // Reuse an already-running host (e.g. restart of the UI only), but only
    // one this bootstrapper assigned the port to, and only when the port is
    // really the trusted loopback listener - never a foreign host and never
    // the public LAN listener, where the UI would load but stay unauthorized
    // (window shown, no profiles).
    if let Some(port) = adoption_candidate(
        env_port_override(),
        port_file_port(),
        read_persisted_port(port_file_dir),
    ) {
        if is_trusted_loopback(port, Duration::from_secs(1)).await {
            logging::info(&format!(
                "[host] adopting running host on loopback port {port}"
            ));
            state.ui_port.store(port, Ordering::SeqCst);
            state.ready.store(true, Ordering::SeqCst);
            adopt_host_culture(app, port).await;
            return true;
        }
        if is_reachable(Some(port), Duration::from_millis(500)).await {
            logging::warn(&format!(
                "[host] port {port} answers but is not a trusted loopback listener; starting our own host"
            ));
        }
    }

    let Some(port) = acquire_loopback_port(port_file_dir) else {
        crate::window::show_error_dialog(
            app,
            &localization::t(keys::ERRORS_COULD_NOT_START_TITLE),
            &localization::t(keys::ERRORS_NO_FREE_PORT),
        );
        return false;
    };

    let Some(binary) = host_binary_path(app) else {
        let candidates = host_binary_candidates(app)
            .iter()
            .map(|path| path.display().to_string())
            .collect::<Vec<_>>()
            .join("\n");
        crate::window::show_error_dialog(
            app,
            &localization::t(keys::ERRORS_COULD_NOT_START_TITLE),
            &localization::t_args(keys::ERRORS_HOST_BINARY_MISSING, &[("paths", &candidates)]),
        );
        return false;
    };

    logging::info(&format!(
        "[host] starting {} on loopback port {port}",
        binary.display()
    ));
    let mut command = Command::new(&binary);
    command
        .current_dir(binary.parent().unwrap_or(Path::new(".")))
        .env("MACRODECK_HOST_PORT", port.to_string())
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some(dir) = &config_dir {
        command.env("DOTNET_BUNDLE_EXTRACT_BASE_DIR", dir.join("host-runtime"));
    }
    if let Some(exe) = autostart_executable(
        std::env::var_os("APPIMAGE").map(PathBuf::from),
        std::env::current_exe().ok(),
        |path| path.is_file(),
    ) {
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

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(error) => {
            crate::window::show_error_dialog(
                app,
                &localization::t(keys::ERRORS_COULD_NOT_START_TITLE),
                &localization::t_args(
                    keys::ERRORS_HOST_PROCESS_COULD_NOT_START,
                    &[("error", &error.to_string())],
                ),
            );
            return false;
        }
    };

    if let Some(stdout) = child.stdout.take() {
        pipe_to_tail(stdout, state.log_tail.clone());
    }
    if let Some(stderr) = child.stderr.take() {
        pipe_to_tail(stderr, state.log_tail.clone());
    }

    #[cfg(windows)]
    {
        match crate::host_job::HostJob::create_and_assign(&child) {
            Some(job) => {
                if let Ok(mut slot) = state.job.lock() {
                    *slot = Some(job);
                }
            }
            None => logging::warn(
                "[host] falling back to a plain process kill; the job object was not set up",
            ),
        }
    }

    if let Ok(mut slot) = state.child.lock() {
        *slot = Some(child);
    }

    spawn_exit_monitor(app.clone());

    let ready = wait_for_ready(app, port, Duration::from_secs(60)).await;
    state.ui_port.store(port, Ordering::SeqCst);
    state.ready.store(ready, Ordering::SeqCst);
    if ready {
        adopt_host_culture(app, port).await;
    }
    ready
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

    let Ok(client) = reqwest::Client::builder()
        .timeout(Duration::from_secs(3))
        .build()
    else {
        return;
    };
    let response = match client
        .get(format!("http://127.0.0.1:{port}/api/localization"))
        .send()
        .await
    {
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExitDisposition {
    Ignore,
    Restart,
    ShowError,
}

pub fn exit_disposition(
    code: Option<i32>,
    shutdown_expected: bool,
    ready: bool,
) -> ExitDisposition {
    if shutdown_expected {
        return ExitDisposition::Ignore;
    }
    if !ready {
        return ExitDisposition::Ignore;
    }
    if code == Some(HOST_RESTART_EXIT_CODE) {
        return ExitDisposition::Restart;
    }
    ExitDisposition::ShowError
}

pub fn relaunch_target(appimage: Option<PathBuf>, current_exe: Option<PathBuf>) -> Option<PathBuf> {
    appimage.or(current_exe)
}

fn relaunch(app: &AppHandle) -> bool {
    let Some(binary) = relaunch_target(
        std::env::var_os("APPIMAGE").map(PathBuf::from),
        std::env::current_exe().ok(),
    ) else {
        logging::error("[host] no executable to relaunch");
        return false;
    };

    match Command::new(&binary)
        .args(std::env::args_os().skip(1))
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

fn report_failed_restart(app: &AppHandle) {
    crate::window::show_error_dialog(
        app,
        &localization::t(keys::ERRORS_COULD_NOT_RESTART_TITLE),
        &localization::t(keys::ERRORS_RESTART_FAILED),
    );
}

fn spawn_exit_monitor(app: AppHandle) {
    std::thread::spawn(move || {
        let state = app.state::<Arc<HostState>>();
        let code = loop {
            {
                let Ok(mut slot) = state.child.lock() else {
                    return;
                };
                let Some(child) = slot.as_mut() else {
                    return;
                };
                match child.try_wait() {
                    Ok(Some(status)) => break status.code(),
                    Ok(None) => {}
                    Err(_) => break None,
                }
            }
            std::thread::sleep(Duration::from_millis(250));
        };

        state.exited.store(true, Ordering::SeqCst);
        let disposition = exit_disposition(
            code,
            state.shutdown_expected.load(Ordering::SeqCst),
            state.ready.load(Ordering::SeqCst),
        );

        match disposition {
            ExitDisposition::Ignore => {
                if !state.shutdown_expected.load(Ordering::SeqCst) {
                    logging::error(&format!("[host] exited unexpectedly with code {code:?}"));
                }
            }
            ExitDisposition::Restart => {
                logging::info("[host] restart requested by the host; relaunching Macro Deck");
                let handle = app.clone();
                let dispatched = app.run_on_main_thread(move || {
                    crate::mark_quitting(&handle);
                    if !relaunch(&handle) {
                        crate::clear_quitting();
                        report_failed_restart(&handle);
                    }
                });
                if let Err(error) = dispatched {
                    logging::error(&format!("[host] could not dispatch the relaunch: {error}"));
                    report_failed_restart(&app);
                }
            }
            ExitDisposition::ShowError => {
                logging::error(&format!("[host] exited unexpectedly with code {code:?}"));
                crate::window::show_error_dialog(
                    &app,
                    &localization::t(keys::ERRORS_HOST_STOPPED_TITLE),
                    &localization::t_args(
                        keys::ERRORS_HOST_STOPPED_UNEXPECTEDLY,
                        &[
                            (
                                "code",
                                &code
                                    .map(|c| c.to_string())
                                    .unwrap_or_else(|| "unknown".to_string()),
                            ),
                            ("log", &state.log_tail.joined()),
                        ],
                    ),
                );
                app.exit(1);
            }
        }
    });
}

async fn wait_for_ready(app: &AppHandle, port: u16, timeout: Duration) -> bool {
    let state = app.state::<Arc<HostState>>();
    let deadline = std::time::Instant::now() + timeout;
    while std::time::Instant::now() < deadline {
        if state.exited.load(Ordering::SeqCst) {
            show_startup_error(app);
            return false;
        }
        if is_reachable(Some(port), Duration::from_millis(500)).await {
            return true;
        }
        tokio::time::sleep(Duration::from_millis(250)).await;
    }
    show_startup_error(app);
    false
}

fn show_startup_error(app: &AppHandle) {
    let state = app.state::<Arc<HostState>>();
    let message = startup_error_message(&state.log_tail.joined());
    crate::window::show_error_dialog(
        app,
        &localization::t(keys::ERRORS_COULD_NOT_START_TITLE),
        &message,
    );
}

pub async fn stop(app: &AppHandle) {
    let state = app.state::<Arc<HostState>>();
    if !state.spawned() || state.exited.load(Ordering::SeqCst) {
        return;
    }
    if state.stopping.swap(true, Ordering::SeqCst) {
        return;
    }
    stop_host(app, "quit", QUIT_STOP_TIMEOUT).await;
}

pub async fn shutdown_for_update(app: &AppHandle) -> bool {
    stop_host(app, "update", UPDATE_STOP_TIMEOUT).await
}

async fn stop_host(app: &AppHandle, reason: &str, graceful_timeout: Duration) -> bool {
    let state = app.state::<Arc<HostState>>();
    state.shutdown_expected.store(true, Ordering::SeqCst);

    let port = managed_port(&state);
    match port {
        Some(port) => {
            if let Err(error) = post_shutdown(port, reason).await {
                logging::warn(&format!("[host] shutdown request failed: {error}"));
            }
        }
        None => logging::warn("[host] no loopback port known; skipping the shutdown request"),
    }

    if wait_until_stopped(app, port, graceful_timeout).await {
        return true;
    }

    logging::warn("[host] did not stop in time; killing the host process");
    kill_child(&state);
    if wait_until_stopped(app, port, FORCED_STOP_TIMEOUT).await {
        return true;
    }
    logging::error("[host] the host process is still running after the kill");
    false
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
        if let Some(child) = slot.as_mut() {
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
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(3))
        .build()?;
    client
        .post(format!(
            "http://127.0.0.1:{port}/api/host/shutdown?reason={reason}"
        ))
        .send()
        .await?;
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
    can_install: bool,
}

async fn post_update_state(port: u16, body: &UpdateStateBody<'_>) -> Result<(), reqwest::Error> {
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(3))
        .build()?;
    client
        .post(format!("http://127.0.0.1:{port}/api/host/update-state"))
        .json(body)
        .send()
        .await?;
    Ok(())
}

// A full-installation backup is not a three-second request like the other host calls here, so this one
// gets its own generous timeout.
const PRE_UPDATE_BACKUP_TIMEOUT: Duration = Duration::from_secs(600);

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
    pub error: Option<String>,
}

/// Turns the host's answer into a go/no-go for the update. Kept pure so the policy is testable without a
/// running host: an unreachable or unparseable answer must not read as permission to proceed.
pub fn interpret_pre_update_backup_response(
    response: Option<PreUpdateBackupResponse>,
) -> Result<(), String> {
    let Some(response) = response else {
        return Err(localization::t(keys::UPDATE_BACKUP_UNCONFIRMED));
    };

    if response.skipped {
        return Ok(());
    }

    if response.success {
        return Ok(());
    }

    Err(response
        .error
        .unwrap_or_else(|| localization::t(keys::UPDATE_BACKUP_GENERIC_FAILURE)))
}

async fn post_pre_update_backup(
    port: u16,
    version: Option<&str>,
) -> Result<PreUpdateBackupResponse, reqwest::Error> {
    let client = reqwest::Client::builder()
        .timeout(PRE_UPDATE_BACKUP_TIMEOUT)
        .build()?;
    client
        .post(format!(
            "http://127.0.0.1:{port}/api/backups/before-host-update"
        ))
        .json(&PreUpdateBackupBody { version })
        .send()
        .await?
        .json::<PreUpdateBackupResponse>()
        .await
}

pub async fn create_pre_update_backup(
    app: &AppHandle,
    version: Option<&str>,
) -> Result<(), String> {
    let state = app.state::<Arc<HostState>>();
    let Some(port) = managed_port(&state) else {
        return Err(localization::t(keys::UPDATE_BACKUP_HOST_UNREACHABLE));
    };

    match post_pre_update_backup(port, version).await {
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

    let body = UpdateStateBody {
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
        can_install: snapshot.install_strategy != UpdateInstallStrategy::ExternalDownload,
    };

    match post_update_state(port, &body).await {
        Ok(()) => true,
        Err(error) => {
            logging::warn(&format!("[host] update-state report failed: {error}"));
            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_successful_backup_lets_the_update_proceed() {
        let response = PreUpdateBackupResponse {
            success: true,
            skipped: false,
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
        };

        assert!(interpret_pre_update_backup_response(Some(response)).is_ok());
    }

    #[test]
    fn a_failed_backup_stops_the_update_and_says_why() {
        let response = PreUpdateBackupResponse {
            success: false,
            skipped: false,
            error: Some("the disk is full".to_string()),
        };

        let error = interpret_pre_update_backup_response(Some(response)).unwrap_err();

        assert!(error.contains("the disk is full"));
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
    fn startup_error_includes_host_output() {
        let message = startup_error_message("boom");
        assert!(message.contains("did not start"));
        assert!(message.contains("Host output:\nboom"));
        assert!(!startup_error_message("").contains("Host output"));
    }

    #[test]
    fn startup_error_explains_port_conflict() {
        let message = startup_error_message("Failed to bind to address");
        assert!(message.contains("port "));
        assert!(message.contains("is already in use"));
        assert!(message.contains("MACRO_DECK_PORT"));
    }

    #[test]
    fn startup_error_names_the_port_the_host_tried() {
        let message = startup_error_message("Failed to bind to address http://0.0.0.0:9100.");
        assert!(message.contains("port 9100"));
    }

    #[test]
    fn startup_error_falls_back_to_the_default_port_without_an_address() {
        let message = startup_error_message("Address already in use");
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
    fn the_restart_exit_code_matches_the_host_constant() {
        // Keep in sync with HostExitCodes.RestartRequested in the .NET host.
        assert_eq!(HOST_RESTART_EXIT_CODE, 86);
    }

    #[test]
    fn an_expected_shutdown_is_ignored() {
        assert_eq!(
            exit_disposition(Some(0), true, true),
            ExitDisposition::Ignore
        );
        assert_eq!(
            exit_disposition(Some(HOST_RESTART_EXIT_CODE), true, true),
            ExitDisposition::Ignore
        );
    }

    #[test]
    fn the_restart_exit_code_relaunches_once_the_host_was_ready() {
        assert_eq!(
            exit_disposition(Some(HOST_RESTART_EXIT_CODE), false, true),
            ExitDisposition::Restart
        );
    }

    #[test]
    fn an_exit_before_readiness_is_left_to_the_startup_path() {
        assert_eq!(
            exit_disposition(Some(HOST_RESTART_EXIT_CODE), false, false),
            ExitDisposition::Ignore
        );
        assert_eq!(
            exit_disposition(Some(1), false, false),
            ExitDisposition::Ignore
        );
    }

    #[test]
    fn a_crash_after_readiness_is_reported() {
        assert_eq!(
            exit_disposition(Some(1), false, true),
            ExitDisposition::ShowError
        );
        assert_eq!(
            exit_disposition(None, false, true),
            ExitDisposition::ShowError
        );
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
}
