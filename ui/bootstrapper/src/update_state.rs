// The single state machine behind the updater (issue #249): before this
// module, "is a download running", "was this version already reported to the
// host" and "was the user already asked about this version" were three
// separate statics with no shared ownership, so the periodic check and the
// manual Settings -> About check could each think they were the only one
// acting. Every entry point - startup, the periodic tick, wake-from-sleep,
// a UI-requested recheck and the manual check - now goes through one
// `UpdateState`, and `snapshot()` is the only shape either the webview or the
// host ever sees.
//
// Deliberately pure: no `AppHandle`, no network, no Tauri runtime. updater.rs
// owns the mutex, the actual feed requests and the download; this module only
// owns what a state transition is allowed to do.

use serde::Serialize;

use crate::update_channel::{self, UpdateChannel};
use crate::updater::UpdateInstallStrategy;

pub(crate) const TICK_SECS: u64 = 60;

pub(crate) const CHECK_INTERVAL_SECS: u64 = 6 * 60 * 60;

pub(crate) const MIN_CHECK_GAP_SECS: u64 = 15 * 60;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum CheckTrigger {
    Startup,
    Periodic,
    Wake,
    Manual,
    Reconnect,
}

// The periodic loop ticks every `tick_secs`; a gap much larger than that
// between two ticks means the process itself was suspended (laptop sleep,
// container pause), not that six hours quietly passed one 60-second sleep at
// a time. A plain `tokio::time::sleep(6h)` never notices that at all - the
// wall clock keeps advancing all the way through the sleep, so it wakes up on
// schedule instead of catching up. `now` is the only impure input, and it is
// a parameter rather than read in here, so the whole decision stays testable
// without a clock.
pub(crate) fn tick_decision(
    last_check_at: Option<u64>,
    last_tick_at: u64,
    now: u64,
    interval_secs: u64,
    tick_secs: u64,
) -> Option<CheckTrigger> {
    let Some(last_check_at) = last_check_at else {
        return Some(CheckTrigger::Startup);
    };

    let wake_gap = 5 * tick_secs;
    let clock_went_backwards = now < last_tick_at;
    let stalled = !clock_went_backwards && now - last_tick_at > wake_gap;

    // A backwards clock jump has to check unconditionally: every elapsed-time
    // comparison below it is meaningless once the clock has moved back, so
    // debouncing on `now - last_check_at` would suppress checks until the clock
    // caught up again.
    if clock_went_backwards {
        return Some(CheckTrigger::Wake);
    }

    if stalled {
        return if now.saturating_sub(last_check_at) < MIN_CHECK_GAP_SECS {
            None
        } else {
            Some(CheckTrigger::Wake)
        };
    }

    if now.saturating_sub(last_check_at) >= interval_secs {
        return Some(CheckTrigger::Periodic);
    }

    None
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) enum UpdatePhase {
    Unsupported,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Downloaded,
    Installing,
    Failed,
}

impl UpdatePhase {
    // The phase string reported to the host over /api/host/update-state. Kept
    // as an explicit mapping (rather than round-tripping through
    // `serde_json::to_value`) so it stays a plain, allocation-free `&str` -
    // `host::report_cancelled` needs to send a "cancelled" string alongside
    // this that has no corresponding variant here (cancelling always leaves
    // the in-process phase at `Available` so a retry is possible), so this
    // could never be "the" serde representation of the enum anyway.
    pub(crate) fn as_str(self) -> &'static str {
        match self {
            UpdatePhase::Unsupported => "unsupported",
            UpdatePhase::Idle => "idle",
            UpdatePhase::Checking => "checking",
            UpdatePhase::UpToDate => "upToDate",
            UpdatePhase::Available => "available",
            UpdatePhase::Downloading => "downloading",
            UpdatePhase::Downloaded => "downloaded",
            UpdatePhase::Installing => "installing",
            UpdatePhase::Failed => "failed",
        }
    }
}

// Which attempt the `Failed` phase came from. The phase alone cannot say: a
// feed that could not be reached and a download that died both land on
// `Failed`, and the UI has to word them differently.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) enum UpdateFailure {
    Check,
    Install,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DownloadProgress {
    pub(crate) downloaded: u64,
    pub(crate) total: Option<u64>,
    pub(crate) percent: Option<u8>,
}

// A total of zero never happens on a real release artifact, but a
// content-length header of "0" must not be read as "already 100% done";
// falling back to a megabyte bucket keeps the progress event throttled
// either way.
pub(crate) fn compute_progress(downloaded: u64, content_len: Option<u64>) -> (Option<u8>, i64) {
    match content_len {
        Some(total) if total > 0 => {
            let percent = (downloaded.min(total) * 100 / total) as u8;
            (Some(percent), i64::from(percent))
        }
        _ => (None, (downloaded / (1024 * 1024)) as i64),
    }
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct UpdateSnapshot {
    pub(crate) phase: UpdatePhase,
    pub(crate) supported: bool,
    pub(crate) current_version: String,
    pub(crate) version: Option<String>,
    pub(crate) notes: Option<String>,
    pub(crate) published_at: Option<String>,
    pub(crate) channel: UpdateChannel,
    pub(crate) beta_installed: bool,
    pub(crate) install_strategy: UpdateInstallStrategy,
    pub(crate) download_url: &'static str,
    pub(crate) partial_check: Option<String>,
    pub(crate) error: Option<String>,
    pub(crate) failure: Option<UpdateFailure>,
    pub(crate) progress: Option<DownloadProgress>,
    pub(crate) last_checked_at: Option<u64>,
}

pub(crate) struct UpdateState {
    pub(crate) phase: UpdatePhase,
    pub(crate) supported: bool,
    pub(crate) current_version: String,
    pub(crate) version: Option<String>,
    pub(crate) notes: Option<String>,
    pub(crate) published_at: Option<String>,
    pub(crate) channel: UpdateChannel,
    pub(crate) beta_installed: bool,
    pub(crate) install_strategy: UpdateInstallStrategy,
    pub(crate) download_url: &'static str,
    pub(crate) partial_check: Option<String>,
    pub(crate) error: Option<String>,
    pub(crate) failure: Option<UpdateFailure>,
    pub(crate) progress: Option<DownloadProgress>,
    // Consumed only once the host has actually been told, so a host that is
    // down or unreachable never permanently loses this version's signal - the
    // next check simply offers it again.
    signalled_version: Option<String>,
    pub(crate) last_check_at: Option<u64>,
    pub(crate) last_tick_at: u64,
}

impl UpdateState {
    fn new(
        current_version: String,
        channel: UpdateChannel,
        install_strategy: UpdateInstallStrategy,
        download_url: &'static str,
        supported: bool,
    ) -> Self {
        let beta_installed = update_channel::is_prerelease(&current_version);
        Self {
            phase: if supported {
                UpdatePhase::Idle
            } else {
                UpdatePhase::Unsupported
            },
            supported,
            current_version,
            version: None,
            notes: None,
            published_at: None,
            channel,
            beta_installed,
            install_strategy,
            download_url,
            partial_check: None,
            error: None,
            failure: None,
            progress: None,
            signalled_version: None,
            last_check_at: None,
            last_tick_at: 0,
        }
    }

    pub(crate) fn unsupported(
        current_version: String,
        channel: UpdateChannel,
        install_strategy: UpdateInstallStrategy,
        download_url: &'static str,
    ) -> Self {
        Self::new(
            current_version,
            channel,
            install_strategy,
            download_url,
            false,
        )
    }

    pub(crate) fn idle(
        current_version: String,
        channel: UpdateChannel,
        install_strategy: UpdateInstallStrategy,
        download_url: &'static str,
    ) -> Self {
        Self::new(
            current_version,
            channel,
            install_strategy,
            download_url,
            true,
        )
    }

    pub(crate) fn try_begin_check(&mut self) -> bool {
        if matches!(
            self.phase,
            UpdatePhase::Checking | UpdatePhase::Downloading | UpdatePhase::Installing
        ) {
            return false;
        }
        self.phase = UpdatePhase::Checking;
        true
    }

    pub(crate) fn record_available(
        &mut self,
        now: u64,
        version: String,
        notes: Option<String>,
        published_at: Option<String>,
        partial_check: Option<String>,
    ) {
        self.last_check_at = Some(now);
        self.phase = UpdatePhase::Available;
        self.version = Some(version);
        self.notes = notes;
        self.published_at = published_at;
        self.partial_check = partial_check;
        self.error = None;
        self.failure = None;
        // A completed or failed download from a previous check must not leak
        // its progress into this one - a fresh Available must never carry a
        // stale 100%.
        self.progress = None;
    }

    pub(crate) fn record_up_to_date(&mut self, now: u64, partial_check: Option<String>) {
        self.last_check_at = Some(now);
        self.phase = UpdatePhase::UpToDate;
        self.version = None;
        self.notes = None;
        self.published_at = None;
        self.partial_check = partial_check;
        self.error = None;
        self.failure = None;
    }

    pub(crate) fn record_check_failed(&mut self, now: u64, error: String) {
        self.last_check_at = Some(now);
        self.phase = UpdatePhase::Failed;
        self.error = Some(error);
        self.failure = Some(UpdateFailure::Check);
    }

    // The single replacement for the old `UPDATE_IN_PROGRESS` static: both the
    // manual install command and the automatic download path have to go
    // through this, or two downloads could race each other.
    pub(crate) fn try_begin_download(&mut self) -> bool {
        if matches!(
            self.phase,
            UpdatePhase::Downloading | UpdatePhase::Installing
        ) {
            return false;
        }
        self.phase = UpdatePhase::Downloading;
        self.progress = None;
        self.error = None;
        self.failure = None;
        true
    }

    pub(crate) fn record_progress(
        &mut self,
        downloaded: u64,
        total: Option<u64>,
    ) -> (Option<u8>, i64) {
        let (percent, bucket) = compute_progress(downloaded, total);
        self.progress = Some(DownloadProgress {
            downloaded,
            total,
            percent,
        });
        (percent, bucket)
    }

    pub(crate) fn record_downloaded(&mut self) {
        self.phase = UpdatePhase::Downloaded;
    }

    pub(crate) fn try_begin_install(&mut self) -> bool {
        if matches!(
            self.phase,
            UpdatePhase::Downloading | UpdatePhase::Installing
        ) {
            return false;
        }
        self.phase = UpdatePhase::Installing;
        true
    }

    // Used for both a failed download and a failed install: either way the
    // attempt did not produce an installed update, and `Failed` (rather than
    // going back to `Available`) is what tells the UI there is something to
    // show the user, not just a quiet retry point.
    pub(crate) fn record_install_failed(&mut self, error: String) {
        self.phase = UpdatePhase::Failed;
        self.error = Some(error);
        self.failure = Some(UpdateFailure::Install);
    }

    pub(crate) fn record_cancelled(&mut self) {
        self.phase = UpdatePhase::Available;
        self.progress = None;
    }

    pub(crate) fn take_availability_signal(&self) -> Option<String> {
        let version = self.version.as_ref()?;
        if self.signalled_version.as_deref() == Some(version.as_str()) {
            None
        } else {
            Some(version.clone())
        }
    }

    pub(crate) fn confirm_availability_signalled(&mut self, version: &str) {
        self.signalled_version = Some(version.to_string());
    }

    pub(crate) fn snapshot(&self) -> UpdateSnapshot {
        UpdateSnapshot {
            phase: self.phase,
            supported: self.supported,
            current_version: self.current_version.clone(),
            version: self.version.clone(),
            notes: self.notes.clone(),
            published_at: self.published_at.clone(),
            channel: self.channel,
            beta_installed: self.beta_installed,
            install_strategy: self.install_strategy,
            download_url: self.download_url,
            partial_check: self.partial_check.clone(),
            error: self.error.clone(),
            failure: self.failure,
            progress: self.progress.clone(),
            last_checked_at: self.last_check_at,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn idle_state() -> UpdateState {
        UpdateState::idle(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        )
    }

    #[test]
    fn never_checked_is_a_startup_check() {
        assert_eq!(
            tick_decision(None, 0, 1_000, CHECK_INTERVAL_SECS, TICK_SECS),
            Some(CheckTrigger::Startup)
        );
    }

    #[test]
    fn inside_the_interval_nothing_happens() {
        let last_check = 10_000;
        assert_eq!(
            tick_decision(
                Some(last_check),
                last_check,
                last_check + 10,
                CHECK_INTERVAL_SECS,
                TICK_SECS
            ),
            None
        );
    }

    #[test]
    fn the_interval_elapsing_is_a_periodic_check() {
        let last_check = 10_000;
        assert_eq!(
            tick_decision(
                Some(last_check),
                last_check + CHECK_INTERVAL_SECS,
                last_check + CHECK_INTERVAL_SECS,
                CHECK_INTERVAL_SECS,
                TICK_SECS
            ),
            Some(CheckTrigger::Periodic)
        );
    }

    #[test]
    fn a_tick_gap_far_larger_than_the_tick_is_a_wake() {
        // The machine slept for three hours: long enough that the tick gap is
        // unmistakably a suspend, but still short of the six-hour interval, so
        // only the wake rule can produce a check here.
        let last_check = 10_000;
        let last_tick = last_check + 100;
        let now = last_tick + 3 * 60 * 60;
        assert_eq!(
            tick_decision(
                Some(last_check),
                last_tick,
                now,
                CHECK_INTERVAL_SECS,
                TICK_SECS
            ),
            Some(CheckTrigger::Wake)
        );
    }

    #[test]
    fn a_wake_gap_shortly_after_the_last_check_is_suppressed() {
        let now = 10_000;
        let last_check = now - 60;
        let last_tick = last_check + 1;
        assert_eq!(
            tick_decision(
                Some(last_check),
                last_tick,
                now,
                CHECK_INTERVAL_SECS,
                TICK_SECS
            ),
            None
        );
    }

    #[test]
    fn a_backwards_clock_jump_wakes_rather_than_wedging_the_scheduler() {
        let last_check = 100_000;
        let last_tick = 50_000;
        let now = 10_000;
        assert_eq!(
            tick_decision(
                Some(last_check),
                last_tick,
                now,
                CHECK_INTERVAL_SECS,
                TICK_SECS
            ),
            Some(CheckTrigger::Wake)
        );
    }

    #[test]
    fn a_repeat_of_the_same_version_does_not_signal_again_but_a_newer_one_does() {
        let mut state = idle_state();

        state.record_available(1, "3.1.0".to_string(), None, None, None);
        let first = state.take_availability_signal();
        assert_eq!(first.as_deref(), Some("3.1.0"));
        state.confirm_availability_signalled(first.as_deref().unwrap());

        state.record_available(2, "3.1.0".to_string(), None, None, None);
        assert!(
            state.take_availability_signal().is_none(),
            "a repeat of the same version must not signal again"
        );

        state.record_available(3, "3.2.0".to_string(), None, None, None);
        assert_eq!(
            state.take_availability_signal().as_deref(),
            Some("3.2.0"),
            "a newer version must signal again"
        );
    }

    #[test]
    fn dedupe_ignores_changed_notes_and_publish_date_for_the_same_version() {
        let mut state = idle_state();
        state.record_available(
            1,
            "3.1.0".to_string(),
            Some("first notes".to_string()),
            Some("2026-01-01".to_string()),
            None,
        );
        state.confirm_availability_signalled("3.1.0");

        state.record_available(
            2,
            "3.1.0".to_string(),
            Some("revised notes".to_string()),
            Some("2026-01-02".to_string()),
            None,
        );
        assert!(state.take_availability_signal().is_none());
    }

    #[test]
    fn a_progress_tick_only_touches_progress_and_never_signals() {
        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);
        state.confirm_availability_signalled("3.1.0");

        state.record_progress(50, Some(100));

        assert!(state.progress.is_some());
        assert!(state.take_availability_signal().is_none());
    }

    #[test]
    fn a_failed_report_leaves_the_signal_available_for_the_next_check() {
        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);

        assert_eq!(state.take_availability_signal().as_deref(), Some("3.1.0"));
        // No confirm_availability_signalled call: simulates a failed report.
        assert_eq!(
            state.take_availability_signal().as_deref(),
            Some("3.1.0"),
            "a failed report must leave the gate open for the next check"
        );
    }

    #[test]
    fn a_download_is_refused_while_already_downloading_and_state_is_unchanged() {
        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);
        assert!(state.try_begin_download());
        state.record_progress(10, Some(100));

        assert!(!state.try_begin_download());
        assert_eq!(state.phase, UpdatePhase::Downloading);
        assert!(state.progress.is_some());
    }

    #[test]
    fn an_install_is_refused_while_already_installing() {
        let mut state = idle_state();
        state.record_downloaded();
        assert!(state.try_begin_install());
        assert!(!state.try_begin_install());
    }

    #[test]
    fn an_install_from_downloaded_succeeds_exactly_once() {
        let mut state = idle_state();
        state.record_downloaded();
        assert!(state.try_begin_install());
        assert_eq!(state.phase, UpdatePhase::Installing);
        assert!(!state.try_begin_install());
    }

    #[test]
    fn cancelling_a_download_returns_to_available_and_allows_a_retry() {
        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);
        assert!(state.try_begin_download());
        state.record_progress(10, Some(100));

        state.record_cancelled();

        assert_eq!(state.phase, UpdatePhase::Available);
        assert!(state.progress.is_none());
        assert!(
            state.try_begin_download(),
            "a cancelled download must be restartable"
        );
    }

    #[test]
    fn unsupported_state_has_no_error_and_reports_nothing_available() {
        let state = UpdateState::unsupported(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        );
        let snapshot = state.snapshot();

        assert!(!snapshot.supported);
        assert!(snapshot.error.is_none());
        assert!(snapshot.version.is_none());
        assert_eq!(snapshot.phase, UpdatePhase::Unsupported);
        assert_ne!(
            snapshot.phase,
            UpdatePhase::Failed,
            "an unsupported build must never be reported as a failed check"
        );
    }

    #[test]
    fn beta_installed_reflects_a_prerelease_current_version() {
        let state = UpdateState::idle(
            "3.0.0-beta.5".to_string(),
            UpdateChannel::Beta,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        );
        assert!(state.beta_installed);

        let state = UpdateState::idle(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        );
        assert!(!state.beta_installed);
    }

    #[test]
    fn a_check_is_refused_while_checking_downloading_or_installing() {
        let mut state = idle_state();
        assert!(state.try_begin_check());
        assert!(!state.try_begin_check());

        state.record_available(1, "3.1.0".to_string(), None, None, None);
        assert!(state.try_begin_download());
        assert!(!state.try_begin_check());

        state.record_downloaded();
        assert!(state.try_begin_install());
        assert!(!state.try_begin_check());
    }

    #[test]
    fn progress_reports_percent_when_total_is_known() {
        assert_eq!(compute_progress(0, Some(200)), (Some(0), 0));
        assert_eq!(compute_progress(100, Some(200)), (Some(50), 50));
        assert_eq!(compute_progress(200, Some(200)), (Some(100), 100));
    }

    #[test]
    fn progress_clamps_overshoot_and_ignores_a_zero_total() {
        assert_eq!(compute_progress(300, Some(200)), (Some(100), 100));
        assert_eq!(compute_progress(123, Some(0)), (None, 0));
    }

    #[test]
    fn progress_buckets_by_megabyte_without_a_total() {
        assert_eq!(compute_progress(0, None), (None, 0));
        assert_eq!(compute_progress(1024 * 1024, None), (None, 1));
        assert_eq!(compute_progress(5 * 1024 * 1024 + 1, None), (None, 5));
    }

    #[test]
    fn the_phase_serializes_as_camel_case() {
        assert_eq!(
            serde_json::to_value(UpdatePhase::UpToDate).unwrap(),
            "upToDate"
        );
        assert_eq!(
            serde_json::to_value(UpdatePhase::Unsupported).unwrap(),
            "unsupported"
        );
    }

    #[test]
    fn as_str_matches_the_serde_representation_for_every_variant() {
        for phase in [
            UpdatePhase::Unsupported,
            UpdatePhase::Idle,
            UpdatePhase::Checking,
            UpdatePhase::UpToDate,
            UpdatePhase::Available,
            UpdatePhase::Downloading,
            UpdatePhase::Downloaded,
            UpdatePhase::Installing,
            UpdatePhase::Failed,
        ] {
            let serialized = serde_json::to_value(phase).unwrap();
            assert_eq!(serialized.as_str(), Some(phase.as_str()));
        }
    }

    #[test]
    fn a_failed_check_and_a_failed_download_are_distinguishable() {
        let mut state = idle_state();
        state.record_check_failed(1, "feed unreachable".to_string());
        let snapshot = state.snapshot();
        assert_eq!(snapshot.phase, UpdatePhase::Failed);
        assert_eq!(snapshot.failure, Some(UpdateFailure::Check));
        assert!(
            snapshot.version.is_none(),
            "a check that never resolved a release must not offer one"
        );

        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);
        assert!(state.try_begin_download());
        state.record_install_failed("connection reset".to_string());
        let snapshot = state.snapshot();
        assert_eq!(snapshot.phase, UpdatePhase::Failed);
        assert_eq!(snapshot.failure, Some(UpdateFailure::Install));
        assert_eq!(snapshot.version.as_deref(), Some("3.1.0"));
    }

    #[test]
    fn a_later_successful_check_clears_the_previous_failure() {
        let mut state = idle_state();
        state.record_check_failed(1, "feed unreachable".to_string());
        state.record_up_to_date(2, None);
        assert!(state.snapshot().failure.is_none());

        state.record_check_failed(3, "feed unreachable".to_string());
        state.record_available(4, "3.1.0".to_string(), None, None, None);
        assert!(state.snapshot().failure.is_none());
    }

    #[test]
    fn a_fresh_available_never_carries_a_previous_downloads_progress() {
        let mut state = idle_state();
        state.record_available(1, "3.1.0".to_string(), None, None, None);
        assert!(state.try_begin_download());
        state.record_progress(100, Some(100));
        state.record_downloaded();

        // A later check finds the same (still uninstalled) version available
        // again - it must not report the previous download's 100% progress.
        state.record_available(2, "3.1.0".to_string(), None, None, None);

        assert!(state.progress.is_none());
    }
}
