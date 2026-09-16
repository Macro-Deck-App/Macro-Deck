// Auto-update flow, replacing electron-updater: the bootstrapper checks the R2
// release feed (latest-<target>.json, signed Tauri updater artifacts) for the
// periodic check. What that check does with a newer release is decided by the
// user's update mode (issue #715, see update_mode.rs): off never contacts the
// feed at all, notify-only asks before downloading, and automatic downloads
// and then installs after a countdown the user can postpone.
// Before installing, the host is shut down via POST
// /api/host/shutdown?reason=update so the installer can replace the host
// binaries.
//
// The same feed is exposed to the desktop UI (Settings -> About) through the
// shell bridge: check_for_update reports the current/latest version without
// installing, install_update installs on demand. Neither consults the update
// mode - off means no automatic checks, not no manual updates.
//
// Which feed(s) get checked is decided by `update_channel` (issue #272): a
// stable install polls only `latest`, a beta install polls both and takes the
// higher valid SemVer, since the beta feed can lag behind latest between a
// beta and the stable release it precedes. `resolve_update` is the shared
// resolver every check path goes through.
//
// Issue #249 unifies every check/download/install path onto one
// `update_state::UpdateState`, held in a single process-wide mutex. Every
// trigger - app startup, the periodic tick, wake-from-sleep, a UI-requested
// recheck and a manual "check now" - funnels through `run_check`, which is
// the only place that talks to the feed and the only place that reports to
// the host. That state is also what makes concurrent downloads/installs
// impossible: try_begin_download/try_begin_install refuse while a download or
// install is already under way, replacing what used to be a bare
// AtomicBool. A separate, unlocked `PENDING_DOWNLOAD` slot parks the bytes of
// a completed automatic download so installing it later never re-downloads -
// it deliberately lives outside the state mutex because snapshot() is read on
// every progress tick and the payload can be ~100 MB.
//
// Native dialogs are the fallback channel, not the primary one: when the host
// is reachable, it is told about every state transition over
// /api/host/update-state and the desktop UI drives the rest through
// get_update_state/the update-state event. A dialog only fires when that
// report failed - the host is down, or was never brought up - so the user has
// at least one way to hear about an update.

use std::sync::atomic::{AtomicI64, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

use semver::Version;
use serde::Serialize;
use tauri::{AppHandle, Emitter};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons};
use tauri_plugin_opener::OpenerExt;
use tauri_plugin_updater::{Error as UpdaterError, RemoteRelease, Update, UpdaterExt};

use crate::host;
use crate::install_state;
use crate::localization::{self, keys};
use crate::logging;
use crate::post_update_changelog;
use crate::update_channel::{self, UpdateChannel};
use crate::update_mode::{self, UpdateMode};
use crate::update_state::{
    self, AutoInstallTick, CheckTrigger, DownloadProgress, UpdatePhase, UpdateSnapshot, UpdateState,
};

const RELEASE_VERSION: &str = env!("MACRODECK_RELEASE_VERSION");

pub(crate) fn current_version_for(baked: &str, package_version: &str) -> String {
    let trimmed = baked.trim();
    if trimmed.is_empty() {
        package_version.to_string()
    } else {
        trimmed.to_string()
    }
}

pub fn current_version(app: &AppHandle) -> String {
    current_version_for(RELEASE_VERSION, &app.package_info().version.to_string())
}

fn remote_is_newer(current: &str, remote: &str) -> bool {
    match (Version::parse(current), Version::parse(remote)) {
        (Ok(current), Ok(remote)) => remote > current,
        _ => current != remote,
    }
}

pub fn version_comparator(current: Version, remote: RemoteRelease) -> bool {
    remote_is_newer(
        &current_version_for(RELEASE_VERSION, &current.to_string()),
        &remote.version.to_string(),
    )
}

pub const DOWNLOAD_PAGE_URL: &str = "https://macro-deck.app/download";

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum UpdateInstallStrategy {
    InApp,
    ExternalDownload,
    Apt,
}

impl UpdateInstallStrategy {
    pub(crate) fn installs_in_app(self) -> bool {
        self == Self::InApp
    }
}

const APT_REPOSITORY_HOST: &str = "packages.macro-deck.app";

fn install_strategy_for(target_os: &str, apt_managed: bool) -> UpdateInstallStrategy {
    match target_os {
        "linux" if apt_managed => UpdateInstallStrategy::Apt,
        "linux" => UpdateInstallStrategy::ExternalDownload,
        _ => UpdateInstallStrategy::InApp,
    }
}

pub(crate) fn install_strategy() -> UpdateInstallStrategy {
    let os = std::env::consts::OS;
    install_strategy_for(os, os == "linux" && installed_from_apt_repository())
}

fn installed_from_apt_repository() -> bool {
    if std::env::var_os("APPIMAGE").is_some() {
        return false;
    }
    let mut files = vec![std::path::PathBuf::from("/etc/apt/sources.list")];
    if let Ok(entries) = std::fs::read_dir("/etc/apt/sources.list.d") {
        files.extend(entries.flatten().map(|entry| entry.path()));
    }
    files.iter().any(|path| {
        std::fs::read_to_string(path).is_ok_and(|text| source_file_uses_repo(path, &text))
    })
}

// apt itself reads only these two extensions, so backups such as .list.distUpgrade are not sources.
fn source_file_uses_repo(path: &std::path::Path, text: &str) -> bool {
    match path.extension().and_then(|extension| extension.to_str()) {
        Some("list") => list_uses_repo(text),
        Some("sources") => sources_uses_repo(text),
        _ => false,
    }
}

fn list_uses_repo(text: &str) -> bool {
    text.lines().map(str::trim_start).any(|line| {
        line.split_whitespace().next() == Some("deb") && line.contains(APT_REPOSITORY_HOST)
    })
}

fn sources_uses_repo(text: &str) -> bool {
    let mut stanzas = vec![Vec::new()];
    for line in text.lines().map(str::trim) {
        if line.is_empty() {
            stanzas.push(Vec::new());
        } else if !line.starts_with('#') {
            stanzas.last_mut().expect("starts non-empty").push(line);
        }
    }
    stanzas.iter().any(|fields| {
        fields
            .iter()
            .any(|field| field.contains(APT_REPOSITORY_HOST))
            && !fields.iter().any(|field| disables_stanza(field))
    })
}

fn disables_stanza(field: &str) -> bool {
    field.split_once(':').is_some_and(|(key, value)| {
        key.trim().eq_ignore_ascii_case("Enabled")
            && matches!(
                value.trim().to_ascii_lowercase().as_str(),
                "no" | "false" | "off" | "disable"
            )
    })
}

// The decision seam for #715: this has to run BEFORE resolve_update, so that
// Off never contacts the feed at all. `Skip` is deliberately absent from
// `CheckAction`, the type `check_once` takes: an Off tick cannot reach the feed
// without failing to compile.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum CheckAction {
    ExternalNotify,
    ConfirmThenInstall,
    DownloadThenInstall,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum PeriodicAction {
    Skip,
    Check(CheckAction),
}

pub(crate) fn periodic_action(mode: UpdateMode, strategy: UpdateInstallStrategy) -> PeriodicAction {
    match (mode, strategy) {
        (UpdateMode::Off, _) => PeriodicAction::Skip,
        (
            UpdateMode::NotifyOnly | UpdateMode::Automatic,
            UpdateInstallStrategy::ExternalDownload | UpdateInstallStrategy::Apt,
        ) => PeriodicAction::Check(CheckAction::ExternalNotify),
        (UpdateMode::NotifyOnly, UpdateInstallStrategy::InApp) => {
            PeriodicAction::Check(CheckAction::ConfirmThenInstall)
        }
        (UpdateMode::Automatic, UpdateInstallStrategy::InApp) => {
            PeriodicAction::Check(CheckAction::DownloadThenInstall)
        }
    }
}

fn external_download_refusal(strategy: UpdateInstallStrategy) -> Option<String> {
    match strategy {
        UpdateInstallStrategy::InApp => None,
        UpdateInstallStrategy::ExternalDownload => Some(localization::t_args(
            keys::UPDATE_LINUX_NOT_SUPPORTED,
            &[("downloadPageUrl", DOWNLOAD_PAGE_URL)],
        )),
        UpdateInstallStrategy::Apt => Some(localization::t(keys::UPDATE_APT_MANAGED)),
    }
}

fn not_installed_refusal(state: install_state::InstallState) -> Option<String> {
    if state.is_installed() {
        None
    } else {
        Some(localization::t(keys::UPDATE_NOT_INSTALLED))
    }
}

fn published_date(update: &Update) -> Option<String> {
    update
        .date
        .map(|d| format!("{:04}-{:02}-{:02}", d.year(), u8::from(d.month()), d.day()))
}

// --- Channel feed resolution (issue #272) -----------------------------------

pub(crate) struct FeedUrls {
    pub stable: String,
    pub beta: String,
}

fn split_query_or_fragment(url: &str) -> (&str, &str) {
    match url.find(['?', '#']) {
        Some(index) => url.split_at(index),
        None => (url, ""),
    }
}

pub(crate) fn feed_urls_for(configured: &str) -> Option<FeedUrls> {
    let (path, suffix) = split_query_or_fragment(configured);
    let slash = path.rfind('/')?;
    let (prefix, segment) = path.split_at(slash + 1);
    let rest = segment
        .strip_prefix("latest-")
        .or_else(|| segment.strip_prefix("beta-"))?;
    Some(FeedUrls {
        stable: format!("{prefix}latest-{rest}{suffix}"),
        beta: format!("{prefix}beta-{rest}{suffix}"),
    })
}

fn configured_endpoint(app: &AppHandle) -> Option<String> {
    app.config()
        .plugins
        .0
        .get("updater")?
        .get("endpoints")?
        .as_array()?
        .first()?
        .as_str()
        .map(str::to_string)
}

pub(crate) fn feeds_for(
    configured: Option<&str>,
    channel: UpdateChannel,
) -> Vec<(UpdateChannel, Option<String>)> {
    match configured.and_then(feed_urls_for) {
        Some(feeds) => match channel {
            UpdateChannel::Stable => vec![(UpdateChannel::Stable, Some(feeds.stable))],
            UpdateChannel::Beta => vec![
                (UpdateChannel::Stable, Some(feeds.stable)),
                (UpdateChannel::Beta, Some(feeds.beta)),
            ],
        },
        None => vec![(channel, None)],
    }
}

fn has_usable_signature(signature: &str) -> bool {
    !signature.trim().is_empty()
}

fn best_candidate_index(candidates: &[(UpdateChannel, String)]) -> Option<usize> {
    candidates
        .iter()
        .enumerate()
        .max_by_key(|(_, (channel, version))| {
            (
                Version::parse(version).ok(),
                matches!(channel, UpdateChannel::Stable) as u8,
            )
        })
        .map(|(index, _)| index)
}

fn partial_check_note(unusable: &[UpdateChannel]) -> Option<String> {
    if unusable.is_empty() {
        return None;
    }
    let names: Vec<&str> = unusable.iter().map(|channel| channel.as_str()).collect();
    let joined = names.join(" and ");
    Some(localization::t_plural(
        keys::UPDATE_PARTIAL_CHECK_FAILED,
        names.len() as i64,
        &[("names", &joined)],
    ))
}

enum FeedOutcome {
    Checked(Option<Box<Update>>),
    NoCandidate,
    Unusable(String),
}

fn classify_feed_error(
    error: &UpdaterError,
    feed: UpdateChannel,
    installed: UpdateChannel,
) -> FeedOutcome {
    match error {
        UpdaterError::TargetNotFound(_) | UpdaterError::TargetsNotFound(_) => {
            FeedOutcome::NoCandidate
        }
        UpdaterError::ReleaseNotFound if feed != installed => FeedOutcome::NoCandidate,
        error => FeedOutcome::Unusable(error.to_string()),
    }
}

async fn check_feed(
    app: &AppHandle,
    feed: UpdateChannel,
    installed: UpdateChannel,
    endpoint: Option<&str>,
) -> FeedOutcome {
    let mut builder = app.updater_builder();
    if let Some(endpoint) = endpoint {
        let url: tauri::Url = match endpoint.parse() {
            Ok(url) => url,
            Err(error) => return FeedOutcome::Unusable(format!("invalid feed url: {error}")),
        };
        builder = match builder.endpoints(vec![url]) {
            Ok(builder) => builder,
            Err(error) => return FeedOutcome::Unusable(error.to_string()),
        };
    }
    let updater = match builder.build() {
        Ok(updater) => updater,
        Err(error) => return FeedOutcome::Unusable(error.to_string()),
    };
    match updater.check().await {
        Ok(update) => FeedOutcome::Checked(update.map(Box::new)),
        Err(error) => classify_feed_error(&error, feed, installed),
    }
}

pub(crate) struct ResolvedUpdate {
    pub channel: UpdateChannel,
    pub update: Option<Update>,
    pub partial_check: Option<String>,
}

pub(crate) async fn resolve_update(app: &AppHandle) -> Result<ResolvedUpdate, String> {
    let channel = update_channel::current(app);
    let configured = configured_endpoint(app);
    let feeds = feeds_for(configured.as_deref(), channel);

    let mut candidates: Vec<(UpdateChannel, Update)> = Vec::new();
    let mut unusable: Vec<UpdateChannel> = Vec::new();
    let mut answered = false;

    for (feed_channel, endpoint) in feeds {
        match check_feed(app, feed_channel, channel, endpoint.as_deref()).await {
            FeedOutcome::Checked(Some(update)) if has_usable_signature(&update.signature) => {
                answered = true;
                candidates.push((feed_channel, *update));
            }
            FeedOutcome::Checked(_) => answered = true,
            FeedOutcome::NoCandidate => {}
            FeedOutcome::Unusable(error) => {
                logging::warn(&format!(
                    "[updater] {} feed could not be checked: {error}",
                    feed_channel.as_str()
                ));
                unusable.push(feed_channel);
            }
        }
    }

    if candidates.is_empty() {
        if answered || unusable.is_empty() {
            return Ok(ResolvedUpdate {
                channel,
                update: None,
                partial_check: partial_check_note(&unusable),
            });
        }
        // Nothing answered and something failed: this must not read as "up to
        // date".
        let reasons: Vec<&str> = unusable.iter().map(|c| c.as_str()).collect();
        return Err(localization::t_args(
            keys::UPDATE_ALL_FEEDS_FAILED,
            &[("reasons", &reasons.join(", "))],
        ));
    }

    let entries: Vec<(UpdateChannel, String)> = candidates
        .iter()
        .map(|(channel, update)| (*channel, update.version.clone()))
        .collect();
    let index = best_candidate_index(&entries).expect("candidates is non-empty");
    let (_, update) = candidates
        .into_iter()
        .nth(index)
        .expect("index came from the same slice");

    Ok(ResolvedUpdate {
        channel,
        update: Some(update),
        partial_check: partial_check_note(&unusable),
    })
}

const UPDATE_PROGRESS_EVENT: &str = "update-progress";

const UPDATE_STATE_EVENT: &str = "update-state";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateCheckResult {
    pub supported: bool,
    pub current_version: String,
    pub available: bool,
    pub version: Option<String>,
    pub notes: Option<String>,
    pub error: Option<String>,
    pub install_strategy: UpdateInstallStrategy,
    pub download_url: &'static str,
    pub published_at: Option<String>,
    pub channel: UpdateChannel,
    pub beta_installed: bool,
    pub partial_check: Option<String>,
}

impl From<&UpdateSnapshot> for UpdateCheckResult {
    fn from(snapshot: &UpdateSnapshot) -> Self {
        Self {
            supported: snapshot.supported,
            current_version: snapshot.current_version.clone(),
            available: snapshot.version.is_some(),
            version: snapshot.version.clone(),
            notes: snapshot.notes.clone(),
            error: snapshot.error.clone(),
            install_strategy: snapshot.install_strategy,
            download_url: snapshot.download_url,
            published_at: snapshot.published_at.clone(),
            channel: snapshot.channel,
            beta_installed: snapshot.beta_installed,
            partial_check: snapshot.partial_check.clone(),
        }
    }
}

static UPDATE_STATE: OnceLock<Mutex<UpdateState>> = OnceLock::new();

fn initial_state(app: &AppHandle) -> UpdateState {
    let current = current_version(app);
    let channel = update_channel::current(app);
    let strategy = install_strategy();

    if !host::is_packaged() {
        return UpdateState::unsupported(current, channel, strategy, DOWNLOAD_PAGE_URL);
    }
    if let Some(reason) = not_installed_refusal(install_state::current()) {
        logging::warn(&format!("[updater] updates are unsupported here: {reason}"));
        return UpdateState::unsupported(current, channel, strategy, DOWNLOAD_PAGE_URL);
    }
    UpdateState::idle(current, channel, strategy, DOWNLOAD_PAGE_URL)
}

fn state_cell(app: &AppHandle) -> &'static Mutex<UpdateState> {
    UPDATE_STATE.get_or_init(|| Mutex::new(initial_state(app)))
}

fn lock_state(app: &AppHandle) -> std::sync::MutexGuard<'static, UpdateState> {
    state_cell(app).lock().unwrap_or_else(|e| e.into_inner())
}

fn emit_state(app: &AppHandle) {
    let snapshot = lock_state(app).snapshot();
    let _ = app.emit(UPDATE_STATE_EVENT, snapshot);
}

// Reports the newly-available version to the host, if it has not already
// been successfully reported. Returns whether the host now knows (or already
// knew) about it - `run_check` uses that to decide whether a native dialog is
// still owed as a fallback; `report_state` just needs the report attempted.
async fn report_availability(app: &AppHandle, snapshot: &UpdateSnapshot) -> bool {
    let signal = lock_state(app).take_availability_signal();
    let Some(version) = signal else {
        return true;
    };

    let reported = host::report_update_state(app, snapshot).await;
    if reported {
        lock_state(app).confirm_availability_signalled(&version);
    }
    reported
}

// Reports the current phase to the host - the counterpart to `emit_state`
// (the webview event) that used to be missing for every phase but
// `available` (issue #249 blocker #1). Availability is still deduped per
// version and only consumed on a successful report; every other phase,
// including a progress tick, is reported unconditionally. A host that is
// unreachable or down never blocks or panics the caller - failures are only
// logged, inside `host::report_update_state`.
async fn report_state(app: &AppHandle) {
    let snapshot = lock_state(app).snapshot();
    if snapshot.phase == UpdatePhase::Available {
        report_availability(app, &snapshot).await;
    } else {
        let _ = host::report_update_state(app, &snapshot).await;
    }
}

async fn emit_state_and_report(app: &AppHandle) {
    emit_state(app);
    report_state(app).await;
}

// Fire-and-forget host report from a synchronous context: the download
// progress callback is a plain `FnMut` (required by `tauri_plugin_updater`)
// and cannot `.await`. Spawned rather than awaited so a slow or unreachable
// host never stalls the download loop.
fn spawn_report_state(app: &AppHandle) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        report_state(&app).await;
    });
}

fn now_secs() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.as_secs())
        .unwrap_or(0)
}

struct PendingDownload {
    version: String,
    update: Update,
    bytes: Vec<u8>,
}

static PENDING_DOWNLOAD: Mutex<Option<PendingDownload>> = Mutex::new(None);

fn park_pending_download(version: String, update: Update, bytes: Vec<u8>) {
    let mut slot = PENDING_DOWNLOAD.lock().unwrap_or_else(|e| e.into_inner());
    *slot = Some(PendingDownload {
        version,
        update,
        bytes,
    });
}

fn pending_version_matches(pending: &str, resolved: &str) -> bool {
    pending == resolved
}

// The generic core of `take_matching_pending_download`, pulled out so the
// version-matching behavior can be tested directly: `tauri_plugin_updater::Update`
// has private fields and cannot be constructed outside that crate, so a real
// `PendingDownload` can never be built in a unit test here.
fn take_if_version_matches<T>(
    slot: &mut Option<T>,
    version_of: impl Fn(&T) -> &str,
    resolved_version: &str,
) -> Option<T> {
    match slot.take() {
        Some(item) if pending_version_matches(version_of(&item), resolved_version) => Some(item),
        _ => None,
    }
}

// A pending download is only reused when it is still the version the feed
// currently resolves to - a stale one (superseded by a newer release since it
// finished) is dropped rather than installed silently.
fn take_matching_pending_download(resolved_version: &str) -> Option<PendingDownload> {
    let mut slot = PENDING_DOWNLOAD.lock().unwrap_or_else(|e| e.into_inner());
    take_if_version_matches(
        &mut slot,
        |pending| pending.version.as_str(),
        resolved_version,
    )
}

fn pending_download_version() -> Option<String> {
    let slot = PENDING_DOWNLOAD.lock().unwrap_or_else(|e| e.into_inner());
    slot.as_ref().map(|pending| pending.version.clone())
}

fn clear_pending_download() {
    let mut slot = PENDING_DOWNLOAD.lock().unwrap_or_else(|e| e.into_inner());
    *slot = None;
}

// A parked download is only worth keeping while it is still the version the
// feed currently resolves to - once a different release is found, the parked
// bytes can never be installed correctly and must not linger in memory (up to
// ~100 MB) for the rest of the process lifetime (issue #249).
fn clear_pending_download_if_stale(resolved_version: &str) {
    let mut slot = PENDING_DOWNLOAD.lock().unwrap_or_else(|e| e.into_inner());
    if let Some(pending) = slot.as_ref() {
        if !pending_version_matches(&pending.version, resolved_version) {
            *slot = None;
        }
    }
}

enum DownloadOutcome {
    Cancelled,
    Failed(String),
}

static DOWNLOAD_CANCEL: Mutex<Option<tokio::sync::oneshot::Sender<()>>> = Mutex::new(None);

#[tauri::command]
pub async fn cancel_update_download(_app: AppHandle) {
    let sender = {
        let mut slot = DOWNLOAD_CANCEL.lock().unwrap_or_else(|e| e.into_inner());
        slot.take()
    };
    if let Some(sender) = sender {
        let _ = sender.send(());
    }
}

async fn download_with_progress(
    app: &AppHandle,
    update: &Update,
) -> Result<Vec<u8>, DownloadOutcome> {
    let (cancel_tx, cancel_rx) = tokio::sync::oneshot::channel();
    {
        let mut slot = DOWNLOAD_CANCEL.lock().unwrap_or_else(|e| e.into_inner());
        *slot = Some(cancel_tx);
    }

    let downloaded = Arc::new(AtomicU64::new(0));
    let last_bucket = Arc::new(AtomicI64::new(-1));
    let progress_app = app.clone();
    let download = update.download(
        move |chunk_len, content_len| {
            let total =
                downloaded.fetch_add(chunk_len as u64, Ordering::Relaxed) + chunk_len as u64;
            let (percent, bucket) = {
                let mut guard = lock_state(&progress_app);
                guard.record_progress(total, content_len)
            };
            if last_bucket.swap(bucket, Ordering::Relaxed) != bucket {
                let _ = progress_app.emit(
                    UPDATE_PROGRESS_EVENT,
                    DownloadProgress {
                        downloaded: total,
                        total: content_len,
                        percent,
                    },
                );
                emit_state(&progress_app);
                spawn_report_state(&progress_app);
            }
        },
        || {},
    );

    let outcome = tokio::select! {
        result = download => match result {
            Ok(bytes) => Ok(bytes),
            Err(error) => Err(DownloadOutcome::Failed(localization::t_args(
                keys::UPDATE_DOWNLOAD_FAILED,
                &[("error", &error.to_string())],
            ))),
        },
        _ = cancel_rx => Err(DownloadOutcome::Cancelled),
    };

    let mut slot = DOWNLOAD_CANCEL.lock().unwrap_or_else(|e| e.into_inner());
    *slot = None;
    outcome
}

pub fn spawn_periodic_check(app: AppHandle) {
    if !host::is_packaged() || !install_state::current().is_installed() {
        return;
    }
    tauri::async_runtime::spawn(async move {
        let mut interval = tokio::time::interval(Duration::from_secs(update_state::TICK_SECS));
        interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        loop {
            interval.tick().await;
            let now = now_secs();
            let (last_check_at, last_tick_at) = {
                let guard = lock_state(&app);
                (guard.last_check_at, guard.last_tick_at)
            };
            let decision = update_state::tick_decision(
                last_check_at,
                last_tick_at,
                now,
                update_state::CHECK_INTERVAL_SECS,
                update_state::TICK_SECS,
            );
            {
                let mut guard = lock_state(&app);
                guard.last_tick_at = now;
            }
            if let Some(trigger) = decision {
                run_check(&app, trigger).await;
            }
        }
    });
}

// The one funnel every trigger goes through. Manual bypasses the update mode
// entirely (today's "check now" semantics: off means no automatic checks, not
// no manual ones); every other trigger is throttled by the same
// mode+strategy decision the periodic tick always used, so Startup, Periodic,
// Wake and Reconnect can never diverge in what they do about a found update.
pub(crate) async fn run_check(app: &AppHandle, trigger: CheckTrigger) {
    let mode = update_mode::current(app);
    let action = if trigger == CheckTrigger::Manual {
        None
    } else {
        match periodic_action(mode, install_strategy()) {
            PeriodicAction::Skip => return,
            PeriodicAction::Check(action) => Some(action),
        }
    };

    let parked_version = pending_download_version();
    let was_parked = {
        let mut guard = lock_state(app);
        if !guard.supported {
            return;
        }
        if action.is_some()
            && guard.skips_automatic_check(mode == UpdateMode::Automatic, parked_version.is_some())
        {
            return;
        }
        let was_parked = guard.phase == UpdatePhase::Downloaded
            && parked_version.is_some()
            && parked_version == guard.version;
        if !guard.try_begin_check() {
            return;
        }
        was_parked
    };
    emit_state_and_report(app).await;

    let now = now_secs();
    match resolve_update(app).await {
        Ok(ResolvedUpdate {
            channel,
            update: Some(update),
            partial_check,
        }) => {
            let version = update.version.clone();
            if was_parked && parked_version.as_deref() == Some(version.as_str()) {
                restore_parked_download(app, now, partial_check);
                return;
            }
            let notes = update.body.clone();
            let published_at = published_date(&update);
            // A parked automatic download only ever matters for the version
            // it was downloaded for; once the feed resolves to something
            // else, it can never be installed correctly.
            clear_pending_download_if_stale(&version);
            {
                let mut guard = lock_state(app);
                guard.channel = channel;
                guard.record_available(now, version.clone(), notes, published_at, partial_check);
            }
            emit_state(app);
            logging::info(&format!("[updater] update available: {version}"));

            let reported = report_and_signal(app).await;

            if let Some(action) = action {
                run_action(app, action, update, reported).await;
            }
        }
        Ok(ResolvedUpdate {
            channel,
            update: None,
            partial_check,
        }) => {
            clear_pending_download();
            {
                let mut guard = lock_state(app);
                guard.channel = channel;
                guard.record_up_to_date(now, partial_check);
            }
            emit_state_and_report(app).await;
        }
        Err(error) => {
            // Every feed for the current channel was unusable (transport,
            // non-2xx, unparseable manifest). A feed simply not existing yet
            // for this platform/channel is handled inside `resolve_update` and
            // never reaches here.
            logging::warn(&format!("[updater] check failed: {error}"));
            if was_parked {
                restore_parked_download(app, now, None);
                return;
            }
            {
                let mut guard = lock_state(app);
                guard.record_check_failed(now, error);
            }
            emit_state_and_report(app).await;
        }
    }
}

// Reports the newly-available version to the host, if it has not already
// been successfully reported. Returns whether the host now knows (or already
// knew) about it - the caller uses that to decide whether a native dialog is
// still owed as a fallback.
async fn report_and_signal(app: &AppHandle) -> bool {
    let snapshot = lock_state(app).snapshot();
    report_availability(app, &snapshot).await
}

// Only the webview is told: the host already announced this download, and
// reporting it again would raise its notification a second time.
fn restore_parked_download(app: &AppHandle, now: u64, partial_check: Option<String>) {
    if !lock_state(app).restore_downloaded(now, partial_check) {
        return;
    }
    emit_state(app);
    arm_auto_install(app);
}

async fn run_action(app: &AppHandle, action: CheckAction, update: Update, reported: bool) {
    match action {
        CheckAction::ExternalNotify => {
            if !reported {
                notify_external_download(app, &update);
            }
        }
        CheckAction::ConfirmThenInstall => {
            if !reported {
                confirm_then_download_and_install(app, update).await;
            }
        }
        CheckAction::DownloadThenInstall => {
            download_then_install(app, update).await;
        }
    }
}

#[tauri::command]
pub async fn check_for_update(app: AppHandle) -> UpdateCheckResult {
    run_check(&app, CheckTrigger::Manual).await;
    UpdateCheckResult::from(&lock_state(&app).snapshot())
}

#[tauri::command]
pub fn get_update_state(app: AppHandle) -> UpdateSnapshot {
    lock_state(&app).snapshot()
}

#[tauri::command]
pub fn request_update_check(app: AppHandle) {
    let now = now_secs();
    let should_run = {
        let guard = lock_state(&app);
        match guard.last_check_at {
            Some(last) => now.saturating_sub(last) >= update_state::MIN_CHECK_GAP_SECS,
            None => true,
        }
    };
    if should_run {
        tauri::async_runtime::spawn(async move { run_check(&app, CheckTrigger::Reconnect).await });
    }
}

#[tauri::command]
pub fn postpone_automatic_install(app: AppHandle) -> bool {
    let postponed = lock_state(&app).postpone_auto_install();
    if postponed {
        logging::info("[updater] automatic install postponed until the next start");
        emit_state(&app);
    }
    postponed
}

pub fn update_mode_changed(app: &AppHandle, mode: UpdateMode) {
    if mode == UpdateMode::Automatic {
        if pending_download_version().is_some() {
            arm_auto_install(app);
        }
    } else if lock_state(app).cancel_auto_install() {
        emit_state(app);
    }
}

fn arm_auto_install(app: &AppHandle) {
    if update_mode::current(app) != UpdateMode::Automatic {
        return;
    }
    let Some(token) = lock_state(app).schedule_auto_install(now_secs()) else {
        return;
    };
    emit_state(app);
    let app = app.clone();
    tauri::async_runtime::spawn(async move { run_auto_install_countdown(app, token).await });
}

async fn run_auto_install_countdown(app: AppHandle, mut token: u64) {
    loop {
        tokio::time::sleep(Duration::from_secs(1)).await;
        let automatic = update_mode::current(&app) == UpdateMode::Automatic;
        let tick = lock_state(&app).auto_install_tick(now_secs(), token, automatic);
        match tick {
            AutoInstallTick::Wait => {}
            AutoInstallTick::Exit => {
                if !automatic {
                    emit_state(&app);
                }
                return;
            }
            AutoInstallTick::Rearm(next) => {
                token = next;
                emit_state(&app);
            }
            AutoInstallTick::Claim => {
                install_claimed(&app).await;
                return;
            }
        }
    }
}

async fn install_claimed(app: &AppHandle) {
    let known_version = lock_state(app).version.clone();
    let pending = known_version
        .as_deref()
        .and_then(take_matching_pending_download);
    emit_state_and_report(app).await;
    let Some(pending) = pending else {
        let message = localization::t_args(
            keys::UPDATE_INSTALL_FAILED,
            &[("error", &localization::t(keys::UPDATE_NO_UPDATE_AVAILABLE))],
        );
        lock_state(app).record_install_failed(message);
        emit_state_and_report(app).await;
        return;
    };
    if let Err(error) = perform_install(app, pending.update, pending.bytes).await {
        logging::error(&format!("[updater] automatic install failed: {error}"));
    }
}

#[tauri::command]
pub async fn install_update(app: AppHandle) -> Result<(), String> {
    let result = run_install(&app).await;
    if let Err(error) = &result {
        logging::error(&format!("[updater] install failed: {error}"));
    }
    result
}

enum ParkedInstall {
    Started(Box<PendingDownload>),
    Busy,
    NotParked,
}

// The parked bytes are only taken once the install slot is won, under the state
// lock, so losing a race against the countdown never throws the download away.
fn begin_parked_install(app: &AppHandle, version: &str) -> ParkedInstall {
    let mut guard = lock_state(app);
    if pending_download_version().as_deref() != Some(version) {
        return ParkedInstall::NotParked;
    }
    if !guard.try_begin_install() {
        return ParkedInstall::Busy;
    }
    match take_matching_pending_download(version) {
        Some(pending) => ParkedInstall::Started(Box::new(pending)),
        None => {
            guard.record_install_failed(localization::t_args(
                keys::UPDATE_INSTALL_FAILED,
                &[("error", &localization::t(keys::UPDATE_NO_UPDATE_AVAILABLE))],
            ));
            ParkedInstall::Busy
        }
    }
}

async fn install_parked(app: &AppHandle, version: &str) -> Option<Result<(), String>> {
    match begin_parked_install(app, version) {
        ParkedInstall::Started(pending) => {
            emit_state_and_report(app).await;
            Some(perform_install(app, pending.update, pending.bytes).await)
        }
        ParkedInstall::Busy => {
            emit_state(app);
            Some(Err(localization::t(keys::UPDATE_ALREADY_IN_PROGRESS)))
        }
        ParkedInstall::NotParked => None,
    }
}

// Shared tail of `run_install`'s two paths (a reused pending download and a
// fresh one): claims the install slot and hands off to `perform_install`.
async fn install_downloaded(app: &AppHandle, update: Update, bytes: Vec<u8>) -> Result<(), String> {
    {
        let mut guard = lock_state(app);
        if !guard.try_begin_install() {
            return Err(localization::t(keys::UPDATE_ALREADY_IN_PROGRESS));
        }
    }
    emit_state_and_report(app).await;
    perform_install(app, update, bytes).await
}

async fn run_install(app: &AppHandle) -> Result<(), String> {
    if let Some(reason) = external_download_refusal(install_strategy()) {
        return Err(reason);
    }
    if !host::is_packaged() {
        return Err(localization::t(keys::UPDATE_NOT_AVAILABLE_IN_BUILD));
    }
    if let Some(reason) = not_installed_refusal(install_state::current()) {
        return Err(reason);
    }

    // A completed automatic download can be installed straight away, without
    // a network round trip: the version to match against is the one already
    // known from the last check, not one freshly re-resolved from the feed.
    // Without this, installing an already-downloaded update failed offline
    // (issue #249).
    let known_version = lock_state(app).version.clone();
    if let Some(version) = known_version {
        if let Some(result) = install_parked(app, &version).await {
            return result;
        }
    }

    let resolved = resolve_update(app)
        .await
        .map_err(|error| localization::t_args(keys::UPDATE_CHECK_FAILED, &[("error", &error)]))?;
    let update = resolved
        .update
        .ok_or_else(|| localization::t(keys::UPDATE_NO_UPDATE_AVAILABLE))?;

    if let Some(result) = install_parked(app, &update.version).await {
        return result;
    }
    let (update, bytes) = {
        {
            let mut guard = lock_state(app);
            if !guard.try_begin_download() {
                return Err(localization::t(keys::UPDATE_ALREADY_IN_PROGRESS));
            }
        }
        emit_state_and_report(app).await;
        match download_with_progress(app, &update).await {
            Ok(bytes) => {
                {
                    let mut guard = lock_state(app);
                    guard.record_downloaded();
                }
                emit_state_and_report(app).await;
                (update, bytes)
            }
            Err(DownloadOutcome::Cancelled) => {
                let snapshot = {
                    let mut guard = lock_state(app);
                    guard.record_cancelled();
                    guard.snapshot()
                };
                emit_state(app);
                host::report_cancelled(app, &snapshot).await;
                clear_pending_download();
                // Cancelling is the user's own doing, not a failure - it
                // must not be surfaced as an install error.
                return Ok(());
            }
            Err(DownloadOutcome::Failed(error)) => {
                {
                    let mut guard = lock_state(app);
                    guard.record_install_failed(error.clone());
                }
                emit_state_and_report(app).await;
                clear_pending_download();
                return Err(error);
            }
        }
    };

    install_downloaded(app, update, bytes).await
}

async fn download_then_install(app: &AppHandle, update: Update) {
    {
        let mut guard = lock_state(app);
        if !guard.try_begin_download() {
            return;
        }
    }
    emit_state_and_report(app).await;

    let bytes = match download_with_progress(app, &update).await {
        Ok(bytes) => bytes,
        Err(DownloadOutcome::Cancelled) => {
            let snapshot = {
                let mut guard = lock_state(app);
                guard.record_cancelled();
                guard.snapshot()
            };
            emit_state(app);
            host::report_cancelled(app, &snapshot).await;
            clear_pending_download();
            return;
        }
        Err(DownloadOutcome::Failed(error)) => {
            logging::error(&format!("[updater] download failed: {error}"));
            {
                let mut guard = lock_state(app);
                guard.record_install_failed(error);
            }
            emit_state_and_report(app).await;
            clear_pending_download();
            return;
        }
    };

    park_pending_download(update.version.clone(), update, bytes);
    {
        let mut guard = lock_state(app);
        guard.record_downloaded();
    }
    emit_state_and_report(app).await;
    arm_auto_install(app);
}

async fn confirm_then_download_and_install(app: &AppHandle, update: Update) {
    let download_and_install = app
        .dialog()
        .message(localization::t_args(
            keys::UPDATE_CONFIRM_DOWNLOAD,
            &[("version", &update.version)],
        ))
        .title(localization::t(keys::UPDATE_AVAILABLE_TITLE))
        .buttons(MessageDialogButtons::OkCancelCustom(
            localization::t(keys::UPDATE_DOWNLOAD_AND_INSTALL),
            localization::t(keys::UPDATE_LATER),
        ))
        .blocking_show();
    if !download_and_install {
        return;
    }

    {
        let mut guard = lock_state(app);
        if !guard.try_begin_download() {
            return;
        }
    }
    emit_state_and_report(app).await;

    let bytes = match download_with_progress(app, &update).await {
        Ok(bytes) => bytes,
        Err(DownloadOutcome::Cancelled) => {
            let snapshot = {
                let mut guard = lock_state(app);
                guard.record_cancelled();
                guard.snapshot()
            };
            emit_state(app);
            host::report_cancelled(app, &snapshot).await;
            clear_pending_download();
            return;
        }
        Err(DownloadOutcome::Failed(error)) => {
            logging::error(&format!("[updater] download failed: {error}"));
            {
                let mut guard = lock_state(app);
                guard.record_install_failed(error);
            }
            emit_state_and_report(app).await;
            clear_pending_download();
            return;
        }
    };

    {
        let mut guard = lock_state(app);
        guard.record_downloaded();
    }
    emit_state_and_report(app).await;

    if let Err(error) = install_downloaded(app, update, bytes).await {
        logging::error(&format!("[updater] install failed: {error}"));
    }
}

fn notify_external_download(app: &AppHandle, update: &Update) {
    if install_strategy() == UpdateInstallStrategy::Apt {
        app.dialog()
            .message(localization::t_args(
                keys::UPDATE_APT_AVAILABLE,
                &[("version", &update.version)],
            ))
            .title(localization::t(keys::UPDATE_AVAILABLE_TITLE))
            .blocking_show();
        return;
    }
    let open = app
        .dialog()
        .message(localization::t_args(
            keys::UPDATE_EXTERNAL_AVAILABLE,
            &[("version", &update.version)],
        ))
        .title(localization::t(keys::UPDATE_AVAILABLE_TITLE))
        .buttons(MessageDialogButtons::OkCancelCustom(
            localization::t(keys::UPDATE_OPEN_DOWNLOAD_PAGE),
            localization::t(keys::UPDATE_LATER),
        ))
        .blocking_show();
    if open {
        let _ = app.opener().open_url(DOWNLOAD_PAGE_URL, None::<&str>);
    }
}

async fn perform_install(app: &AppHandle, update: Update, bytes: Vec<u8>) -> Result<(), String> {
    // The backup has to happen while the host is still running, and its failure has to stop the update
    // rather than be logged and ignored: the whole point is that the installation can be recovered if the
    // update goes wrong.
    if let Err(error) = host::create_pre_update_backup(app, Some(&update.version)).await {
        let message = localization::t_args(keys::UPDATE_BACKUP_FAILED, &[("error", &error)]);
        {
            let mut guard = lock_state(app);
            guard.record_install_failed(message.clone());
        }
        emit_state_and_report(app).await;
        return Err(message);
    }

    // A host that is still alive locks its own binary, so the installer would
    // fail halfway through and leave a broken install behind (issue #131).
    // Refusing here keeps the current version intact and says why.
    if !host::shutdown_for_update(app).await {
        let message = localization::t(keys::UPDATE_HOST_STOP_FAILED);
        {
            let mut guard = lock_state(app);
            guard.record_install_failed(message.clone());
        }
        emit_state_and_report(app).await;
        return Err(message);
    }
    // Only now: the host is down and the process is about to be replaced, so
    // the exit handler must not try to stop the host a second time. Marking it
    // earlier would leave the flag set on the error path above.
    crate::mark_quitting(app);
    // Written before install: on Windows the plugin exits the process as soon
    // as the installer is launched, so there is no later point to do it.
    post_update_changelog::remember(
        app,
        &update.version,
        update.body.as_deref(),
        published_date(&update).as_deref(),
    );
    match update.install(bytes) {
        Ok(()) => app.restart(),
        Err(error) => {
            post_update_changelog::forget(app);
            let message = localization::t_args(
                keys::UPDATE_INSTALL_FAILED,
                &[("error", &error.to_string())],
            );
            {
                let mut guard = lock_state(app);
                guard.record_install_failed(message.clone());
            }
            // The host was already shut down for this install a moment ago
            // (see above), so this report will typically fail - harmless,
            // and logged as a warning rather than surfaced here.
            emit_state_and_report(app).await;
            Err(message)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::update_state::UpdatePhase;
    use std::path::Path;

    fn snapshot(
        phase: UpdatePhase,
        current_version: &str,
        channel: UpdateChannel,
    ) -> UpdateSnapshot {
        UpdateSnapshot {
            phase,
            supported: phase != UpdatePhase::Unsupported,
            current_version: current_version.to_string(),
            version: None,
            notes: None,
            published_at: None,
            channel,
            beta_installed: update_channel::is_prerelease(current_version),
            install_strategy: install_strategy(),
            download_url: DOWNLOAD_PAGE_URL,
            partial_check: None,
            error: None,
            failure: None,
            progress: None,
            last_checked_at: None,
            auto_install_at: None,
        }
    }

    #[test]
    fn unsupported_result_reports_no_update() {
        let snap = snapshot(UpdatePhase::Unsupported, "3.0.0", UpdateChannel::Stable);
        let result = UpdateCheckResult::from(&snap);
        assert!(!result.supported);
        assert!(!result.available);
        assert_eq!(result.current_version, "3.0.0");
        assert!(result.version.is_none());
        assert!(result.error.is_none());
        assert_eq!(result.download_url, DOWNLOAD_PAGE_URL);
        assert!(result.published_at.is_none());
        assert_eq!(result.channel, UpdateChannel::Stable);
        assert!(!result.beta_installed);
        assert!(result.partial_check.is_none());
    }

    #[test]
    fn up_to_date_result_is_supported_without_a_version() {
        let snap = snapshot(UpdatePhase::UpToDate, "3.0.0", UpdateChannel::Stable);
        let result = UpdateCheckResult::from(&snap);
        assert!(result.supported);
        assert!(!result.available);
        assert!(result.version.is_none());
        assert!(result.error.is_none());
        assert_eq!(result.download_url, DOWNLOAD_PAGE_URL);
        assert!(result.published_at.is_none());
    }

    #[test]
    fn up_to_date_result_carries_a_partial_check_note() {
        let mut snap = snapshot(UpdatePhase::UpToDate, "3.0.0", UpdateChannel::Beta);
        snap.partial_check = Some("note".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert_eq!(result.partial_check.as_deref(), Some("note"));
    }

    #[test]
    fn available_result_carries_version_and_notes() {
        let mut snap = snapshot(UpdatePhase::Available, "3.0.0", UpdateChannel::Stable);
        snap.version = Some("3.1.0".to_string());
        snap.notes = Some("Bug fixes".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert!(result.supported);
        assert!(result.available);
        assert_eq!(result.version.as_deref(), Some("3.1.0"));
        assert_eq!(result.notes.as_deref(), Some("Bug fixes"));
        assert!(result.error.is_none());
        assert_eq!(result.download_url, DOWNLOAD_PAGE_URL);
    }

    #[test]
    fn available_result_carries_the_publish_date() {
        let mut snap = snapshot(UpdatePhase::Available, "3.0.0", UpdateChannel::Stable);
        snap.version = Some("3.1.0".to_string());
        snap.published_at = Some("2026-01-15".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert_eq!(result.published_at.as_deref(), Some("2026-01-15"));
    }

    #[test]
    fn available_result_carries_the_channel_and_beta_installed_flag() {
        let mut snap = snapshot(UpdatePhase::Available, "3.0.0-beta.1", UpdateChannel::Beta);
        snap.version = Some("3.0.0-beta.2".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert_eq!(result.channel, UpdateChannel::Beta);
        assert!(result.beta_installed);
    }

    #[test]
    fn failed_result_carries_the_error() {
        let mut snap = snapshot(UpdatePhase::Failed, "3.0.0", UpdateChannel::Stable);
        snap.error = Some("offline".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert!(result.supported);
        assert!(!result.available);
        assert!(result.version.is_none());
        assert_eq!(result.error.as_deref(), Some("offline"));
        assert_eq!(result.download_url, DOWNLOAD_PAGE_URL);
        assert!(result.published_at.is_none());
    }

    #[test]
    fn a_download_in_flight_is_still_reported_as_available_by_the_projection() {
        // A manual check while a download is in flight must not read as "no
        // update" just because the phase moved past Available.
        let mut snap = snapshot(UpdatePhase::Downloading, "3.0.0", UpdateChannel::Stable);
        snap.version = Some("3.1.0".to_string());
        let result = UpdateCheckResult::from(&snap);
        assert!(result.available);
        assert_eq!(result.version.as_deref(), Some("3.1.0"));
    }

    #[test]
    fn install_strategy_is_external_download_on_linux() {
        assert_eq!(
            install_strategy_for("linux", false),
            UpdateInstallStrategy::ExternalDownload
        );
    }

    #[test]
    fn install_strategy_is_apt_on_linux_installed_from_the_apt_repository() {
        assert_eq!(
            install_strategy_for("linux", true),
            UpdateInstallStrategy::Apt
        );
    }

    #[test]
    fn install_strategy_is_in_app_on_windows_and_macos() {
        assert_eq!(
            install_strategy_for("windows", false),
            UpdateInstallStrategy::InApp
        );
        assert_eq!(
            install_strategy_for("macos", true),
            UpdateInstallStrategy::InApp
        );
    }

    #[test]
    fn an_unknown_target_keeps_the_in_app_strategy() {
        assert_eq!(
            install_strategy_for("freebsd", false),
            UpdateInstallStrategy::InApp
        );
    }

    #[test]
    fn only_the_in_app_strategy_can_install() {
        assert!(UpdateInstallStrategy::InApp.installs_in_app());
        assert!(!UpdateInstallStrategy::ExternalDownload.installs_in_app());
        assert!(!UpdateInstallStrategy::Apt.installs_in_app());
    }

    const README_SOURCE: &str = "Types: deb\nURIs: https://packages.macro-deck.app\nSuites: stable\nComponents: main\nArchitectures: amd64\nSigned-By: /etc/apt/keyrings/macro-deck.asc\n";

    #[test]
    fn the_documented_deb822_source_is_the_apt_repository() {
        assert!(source_file_uses_repo(
            Path::new("/etc/apt/sources.list.d/macro-deck.sources"),
            README_SOURCE
        ));
    }

    #[test]
    fn a_one_line_list_entry_is_the_apt_repository() {
        assert!(source_file_uses_repo(
            Path::new("/etc/apt/sources.list.d/macro-deck.list"),
            "deb\t[signed-by=/etc/apt/keyrings/macro-deck.asc] https://packages.macro-deck.app stable main\n"
        ));
    }

    #[test]
    fn a_commented_out_list_entry_is_not_a_source() {
        assert!(!list_uses_repo(
            "# deb https://packages.macro-deck.app stable main # disabled on upgrade\n"
        ));
    }

    #[test]
    fn a_disabled_deb822_stanza_is_not_a_source() {
        for value in ["no", "False", "off", "disable"] {
            let text = format!("{README_SOURCE}Enabled: {value}\n");
            assert!(!sources_uses_repo(&text), "Enabled: {value}");
        }
    }

    #[test]
    fn a_disabled_stanza_does_not_hide_an_enabled_one() {
        let text = format!("Enabled: no\n{README_SOURCE}\n{README_SOURCE}");
        assert!(sources_uses_repo(&text));
    }

    #[test]
    fn a_release_upgrade_backup_is_not_a_source() {
        assert!(!source_file_uses_repo(
            Path::new("/etc/apt/sources.list.d/macro-deck.sources.distUpgrade"),
            README_SOURCE
        ));
    }

    #[test]
    fn other_repositories_are_not_the_apt_repository() {
        assert!(!sources_uses_repo(
            "Types: deb\nURIs: http://deb.debian.org/debian\nSuites: trixie\nComponents: main\n"
        ));
        assert!(!list_uses_repo(
            "deb http://archive.ubuntu.com/ubuntu noble main\n"
        ));
    }

    #[test]
    fn an_apt_install_is_refused_with_the_apt_command_instead_of_the_download_page() {
        let reason = external_download_refusal(UpdateInstallStrategy::Apt)
            .expect("an apt install must refuse the in-app install");
        assert!(reason.contains("sudo apt update && sudo apt upgrade"));
        assert!(!reason.contains(DOWNLOAD_PAGE_URL));
    }

    #[test]
    fn install_update_is_refused_under_the_external_download_strategy() {
        let reason = external_download_refusal(UpdateInstallStrategy::ExternalDownload)
            .expect("external download strategy must refuse the in-app install");
        assert!(reason.contains(DOWNLOAD_PAGE_URL));
        assert!(!reason.contains("restart"));
        assert!(!reason.contains("Restart"));
    }

    #[test]
    fn in_app_install_has_no_refusal() {
        assert!(external_download_refusal(UpdateInstallStrategy::InApp).is_none());
    }

    #[test]
    fn an_installed_bundle_has_no_not_installed_refusal() {
        assert!(not_installed_refusal(install_state::InstallState::Installed).is_none());
    }

    #[test]
    fn a_mounted_image_or_translocated_bundle_is_refused() {
        for state in [
            install_state::InstallState::MountedImage,
            install_state::InstallState::Translocated,
        ] {
            let reason = not_installed_refusal(state)
                .unwrap_or_else(|| panic!("{state:?} must refuse the in-place install"));
            assert!(reason.contains("Applications"));
        }
    }

    #[test]
    fn the_download_page_is_an_https_url_on_the_macro_deck_domain() {
        assert!(DOWNLOAD_PAGE_URL.starts_with("https://macro-deck.app/"));
    }

    #[test]
    fn the_install_strategy_serializes_as_camel_case() {
        assert_eq!(
            serde_json::to_value(UpdateInstallStrategy::InApp).unwrap(),
            "inApp"
        );
        assert_eq!(
            serde_json::to_value(UpdateInstallStrategy::ExternalDownload).unwrap(),
            "externalDownload"
        );
        assert_eq!(
            serde_json::to_value(UpdateInstallStrategy::Apt).unwrap(),
            "apt"
        );
    }

    #[test]
    fn the_baked_release_version_wins_over_the_package_version() {
        assert_eq!(
            current_version_for("3.0.0-beta.42", "3.0.0"),
            "3.0.0-beta.42"
        );
    }

    #[test]
    fn the_package_version_is_used_without_a_baked_version() {
        assert_eq!(current_version_for("", "3.0.0"), "3.0.0");
        assert_eq!(current_version_for("   ", "3.0.0"), "3.0.0");
    }

    #[test]
    fn a_newer_beta_on_the_feed_is_an_update_for_a_beta_rpm() {
        assert!(remote_is_newer("3.0.0-beta.42", "3.0.0-beta.43"));
    }

    #[test]
    fn a_stable_release_is_an_update_for_a_beta() {
        assert!(remote_is_newer("3.0.0-beta.42", "3.0.0"));
    }

    #[test]
    fn the_same_version_is_not_an_update() {
        assert!(!remote_is_newer("3.0.0", "3.0.0"));
    }

    #[test]
    fn an_older_version_on_the_feed_is_not_an_update() {
        assert!(!remote_is_newer("3.1.0", "3.0.0"));
    }

    #[test]
    fn a_malformed_feed_version_is_treated_as_an_update() {
        assert!(remote_is_newer("not-a-version", "also-not-a-version"));
        assert!(!remote_is_newer("not-a-version", "not-a-version"));
    }

    #[test]
    fn feed_urls_derive_beta_from_latest() {
        let feeds = feed_urls_for("https://updater.macro-deck.app/releases/latest-{{target}}.json")
            .unwrap();
        assert_eq!(
            feeds.stable,
            "https://updater.macro-deck.app/releases/latest-{{target}}.json"
        );
        assert_eq!(
            feeds.beta,
            "https://updater.macro-deck.app/releases/beta-{{target}}.json"
        );
    }

    #[test]
    fn feed_urls_derive_latest_from_beta() {
        let feeds =
            feed_urls_for("https://updater.macro-deck.app/releases/beta-{{target}}.json").unwrap();
        assert_eq!(
            feeds.stable,
            "https://updater.macro-deck.app/releases/latest-{{target}}.json"
        );
        assert_eq!(
            feeds.beta,
            "https://updater.macro-deck.app/releases/beta-{{target}}.json"
        );
    }

    #[test]
    fn feed_urls_are_none_for_the_dev_endpoint() {
        assert!(feed_urls_for(
            "https://updater.macro-deck.app/releases/development-{{target}}.json"
        )
        .is_none());
    }

    #[test]
    fn feed_urls_preserve_the_query_and_fragment_untouched() {
        let feeds = feed_urls_for("https://x/latest-{{target}}.json?v=1#frag").unwrap();
        assert_eq!(feeds.stable, "https://x/latest-{{target}}.json?v=1#frag");
        assert_eq!(feeds.beta, "https://x/beta-{{target}}.json?v=1#frag");
    }

    #[test]
    fn feed_urls_only_rewrite_the_file_name_not_the_directory() {
        // A directory that happens to contain "latest-" must not fool the
        // rewrite; only the last path segment is ever inspected.
        assert!(feed_urls_for("https://x/latest-feeds/nightly.json").is_none());
    }

    #[test]
    fn feed_urls_are_none_for_garbage() {
        assert!(feed_urls_for("not a url at all").is_none());
        assert!(feed_urls_for("").is_none());
    }

    #[test]
    fn feeds_for_stable_channel_is_a_single_feed() {
        let feeds = feeds_for(
            Some("https://x/latest-{{target}}.json"),
            UpdateChannel::Stable,
        );
        assert_eq!(
            feeds,
            vec![(
                UpdateChannel::Stable,
                Some("https://x/latest-{{target}}.json".to_string())
            )]
        );
    }

    #[test]
    fn feeds_for_beta_channel_is_both_feeds_and_they_are_disjoint() {
        let feeds = feeds_for(
            Some("https://x/latest-{{target}}.json"),
            UpdateChannel::Beta,
        );
        assert_eq!(
            feeds,
            vec![
                (
                    UpdateChannel::Stable,
                    Some("https://x/latest-{{target}}.json".to_string())
                ),
                (
                    UpdateChannel::Beta,
                    Some("https://x/beta-{{target}}.json".to_string())
                ),
            ]
        );
        assert_ne!(feeds[0].1, feeds[1].1);
    }

    #[test]
    fn feeds_for_an_unrecognised_endpoint_falls_back_to_the_configured_list_under_both_channels() {
        assert_eq!(
            feeds_for(
                Some("https://x/development-{{target}}.json"),
                UpdateChannel::Stable
            ),
            vec![(UpdateChannel::Stable, None)]
        );
        assert_eq!(
            feeds_for(
                Some("https://x/development-{{target}}.json"),
                UpdateChannel::Beta
            ),
            vec![(UpdateChannel::Beta, None)]
        );
    }

    #[test]
    fn feeds_for_no_configured_endpoint_falls_back_too() {
        assert_eq!(
            feeds_for(None, UpdateChannel::Stable),
            vec![(UpdateChannel::Stable, None)]
        );
        assert_eq!(
            feeds_for(None, UpdateChannel::Beta),
            vec![(UpdateChannel::Beta, None)]
        );
    }

    fn candidates(pairs: &[(UpdateChannel, &str)]) -> Vec<(UpdateChannel, String)> {
        pairs.iter().map(|(c, v)| (*c, v.to_string())).collect()
    }

    #[test]
    fn stable_outranks_a_matching_prerelease() {
        for set in [
            candidates(&[
                (UpdateChannel::Beta, "3.1.0-beta.5"),
                (UpdateChannel::Stable, "3.1.0"),
            ]),
            candidates(&[
                (UpdateChannel::Stable, "3.1.0"),
                (UpdateChannel::Beta, "3.1.0-beta.5"),
            ]),
        ] {
            let winner = &set[best_candidate_index(&set).unwrap()];
            assert_eq!(winner.0, UpdateChannel::Stable);
        }
    }

    #[test]
    fn a_newer_prerelease_outranks_an_older_stable() {
        for set in [
            candidates(&[
                (UpdateChannel::Stable, "3.1.0"),
                (UpdateChannel::Beta, "3.2.0-beta.1"),
            ]),
            candidates(&[
                (UpdateChannel::Beta, "3.2.0-beta.1"),
                (UpdateChannel::Stable, "3.1.0"),
            ]),
        ] {
            let winner = &set[best_candidate_index(&set).unwrap()];
            assert_eq!(winner.1, "3.2.0-beta.1");
        }
    }

    #[test]
    fn a_stable_release_newer_than_the_latest_beta_wins() {
        // The exact acceptance criterion from issue #272: the beta feed
        // lagging behind latest must never hide a newer stable release.
        for set in [
            candidates(&[
                (UpdateChannel::Beta, "3.0.0-beta.9"),
                (UpdateChannel::Stable, "3.1.0"),
            ]),
            candidates(&[
                (UpdateChannel::Stable, "3.1.0"),
                (UpdateChannel::Beta, "3.0.0-beta.9"),
            ]),
        ] {
            let winner = &set[best_candidate_index(&set).unwrap()];
            assert_eq!(winner.0, UpdateChannel::Stable);
            assert_eq!(winner.1, "3.1.0");
        }
    }

    #[test]
    fn stable_wins_an_exact_version_tie() {
        for set in [
            candidates(&[
                (UpdateChannel::Beta, "3.1.0"),
                (UpdateChannel::Stable, "3.1.0"),
            ]),
            candidates(&[
                (UpdateChannel::Stable, "3.1.0"),
                (UpdateChannel::Beta, "3.1.0"),
            ]),
        ] {
            let winner = &set[best_candidate_index(&set).unwrap()];
            assert_eq!(winner.0, UpdateChannel::Stable);
        }
    }

    #[test]
    fn a_malformed_version_never_masks_a_real_release() {
        for set in [
            candidates(&[
                (UpdateChannel::Beta, "not-a-version"),
                (UpdateChannel::Stable, "3.1.0"),
            ]),
            candidates(&[
                (UpdateChannel::Stable, "3.1.0"),
                (UpdateChannel::Beta, "not-a-version"),
            ]),
        ] {
            let winner = &set[best_candidate_index(&set).unwrap()];
            assert_eq!(winner.1, "3.1.0");
        }
    }

    #[test]
    fn a_lone_malformed_version_is_still_offered() {
        let only = candidates(&[(UpdateChannel::Beta, "not-a-version")]);
        assert_eq!(best_candidate_index(&only), Some(0));
    }

    #[test]
    fn no_candidates_ranks_to_none() {
        assert_eq!(best_candidate_index(&[]), None);
    }

    #[test]
    fn a_blank_signature_is_not_usable() {
        assert!(!has_usable_signature(""));
        assert!(!has_usable_signature("   "));
    }

    #[test]
    fn a_real_signature_is_usable() {
        assert!(has_usable_signature(
            "untrusted comment: minisign\nRWTdummySignatureContent=="
        ));
    }

    #[test]
    fn partial_check_note_is_none_when_nothing_is_unusable() {
        assert!(partial_check_note(&[]).is_none());
    }

    #[test]
    fn partial_check_note_names_a_single_unusable_feed() {
        let note = partial_check_note(&[UpdateChannel::Beta]).unwrap();
        assert!(note.contains("beta"));
    }

    #[test]
    fn partial_check_note_names_both_unusable_feeds() {
        let note = partial_check_note(&[UpdateChannel::Stable, UpdateChannel::Beta]).unwrap();
        assert!(note.contains("stable"));
        assert!(note.contains("beta"));
    }

    #[test]
    fn a_manifest_without_this_platform_is_no_candidate_on_either_feed() {
        for feed in [UpdateChannel::Stable, UpdateChannel::Beta] {
            for installed in [UpdateChannel::Stable, UpdateChannel::Beta] {
                assert!(matches!(
                    classify_feed_error(
                        &UpdaterError::TargetsNotFound(Vec::new()),
                        feed,
                        installed
                    ),
                    FeedOutcome::NoCandidate
                ));
                assert!(matches!(
                    classify_feed_error(
                        &UpdaterError::TargetNotFound(String::new()),
                        feed,
                        installed
                    ),
                    FeedOutcome::NoCandidate
                ));
            }
        }
    }

    #[test]
    fn a_missing_beta_feed_is_no_candidate_on_a_stable_install() {
        assert!(matches!(
            classify_feed_error(
                &UpdaterError::ReleaseNotFound,
                UpdateChannel::Beta,
                UpdateChannel::Stable
            ),
            FeedOutcome::NoCandidate
        ));
    }

    #[test]
    fn a_missing_stable_feed_is_no_candidate_on_a_beta_install() {
        assert!(matches!(
            classify_feed_error(
                &UpdaterError::ReleaseNotFound,
                UpdateChannel::Stable,
                UpdateChannel::Beta
            ),
            FeedOutcome::NoCandidate
        ));
    }

    #[test]
    fn a_missing_own_channel_feed_is_unusable() {
        for channel in [UpdateChannel::Stable, UpdateChannel::Beta] {
            assert!(matches!(
                classify_feed_error(&UpdaterError::ReleaseNotFound, channel, channel),
                FeedOutcome::Unusable(_)
            ));
        }
    }

    #[test]
    fn off_skips_the_periodic_check_under_both_strategies() {
        // The regression #715 is about: no feed contact at all.
        assert_eq!(
            periodic_action(UpdateMode::Off, UpdateInstallStrategy::InApp),
            PeriodicAction::Skip
        );
        assert_eq!(
            periodic_action(UpdateMode::Off, UpdateInstallStrategy::ExternalDownload),
            PeriodicAction::Skip
        );
    }

    #[test]
    fn notify_only_confirms_before_downloading_in_app() {
        assert_eq!(
            periodic_action(UpdateMode::NotifyOnly, UpdateInstallStrategy::InApp),
            PeriodicAction::Check(CheckAction::ConfirmThenInstall)
        );
    }

    #[test]
    fn automatic_downloads_then_installs_in_app() {
        assert_eq!(
            periodic_action(UpdateMode::Automatic, UpdateInstallStrategy::InApp),
            PeriodicAction::Check(CheckAction::DownloadThenInstall)
        );
    }

    #[test]
    fn notify_only_and_automatic_both_collapse_to_external_notify_on_external_download() {
        // Linux never downloads in-app, regardless of the chosen mode.
        assert_eq!(
            periodic_action(
                UpdateMode::NotifyOnly,
                UpdateInstallStrategy::ExternalDownload
            ),
            PeriodicAction::Check(CheckAction::ExternalNotify)
        );
        assert_eq!(
            periodic_action(
                UpdateMode::Automatic,
                UpdateInstallStrategy::ExternalDownload
            ),
            PeriodicAction::Check(CheckAction::ExternalNotify)
        );
    }

    #[test]
    fn an_apt_install_is_only_ever_notified_and_off_stays_off() {
        for mode in [UpdateMode::NotifyOnly, UpdateMode::Automatic] {
            assert_eq!(
                periodic_action(mode, UpdateInstallStrategy::Apt),
                PeriodicAction::Check(CheckAction::ExternalNotify)
            );
        }
        assert_eq!(
            periodic_action(UpdateMode::Off, UpdateInstallStrategy::Apt),
            PeriodicAction::Skip
        );
    }

    #[test]
    fn every_automatic_trigger_shares_the_same_action_for_a_given_mode_and_strategy() {
        // Startup, Periodic, Wake and Reconnect must never diverge in what
        // they do about a found update - only Manual (which bypasses this
        // function entirely) is allowed to differ.
        for mode in [
            UpdateMode::Off,
            UpdateMode::NotifyOnly,
            UpdateMode::Automatic,
        ] {
            for strategy in [
                UpdateInstallStrategy::InApp,
                UpdateInstallStrategy::ExternalDownload,
            ] {
                let expected = periodic_action(mode, strategy);
                for trigger in [
                    CheckTrigger::Startup,
                    CheckTrigger::Periodic,
                    CheckTrigger::Wake,
                    CheckTrigger::Reconnect,
                ] {
                    assert_ne!(trigger, CheckTrigger::Manual);
                    assert_eq!(periodic_action(mode, strategy), expected);
                }
            }
        }
    }

    #[test]
    fn a_pending_download_for_a_different_version_is_not_reused() {
        assert!(!pending_version_matches("3.1.0", "3.2.0"));
        assert!(pending_version_matches("3.1.0", "3.1.0"));
    }

    #[test]
    fn take_if_version_matches_returns_the_payload_only_for_its_own_version() {
        let mut slot = Some(("3.1.0".to_string(), vec![9_u8, 9, 9]));
        let miss = take_if_version_matches(&mut slot, |(version, _)| version.as_str(), "3.2.0");
        assert!(
            miss.is_none(),
            "a mismatched version must not hand back the parked bytes"
        );
        assert!(
            slot.is_none(),
            "a mismatched lookup still consumes the slot rather than leaving it stale"
        );

        let mut slot = Some(("3.1.0".to_string(), vec![9_u8, 9, 9]));
        let hit = take_if_version_matches(&mut slot, |(version, _)| version.as_str(), "3.1.0");
        assert_eq!(
            hit.map(|(_, bytes)| bytes),
            Some(vec![9_u8, 9, 9]),
            "a matching version must hand back exactly the parked bytes"
        );
        assert!(slot.is_none(), "a hit consumes the slot");
    }
}
