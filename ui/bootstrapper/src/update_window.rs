use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

use serde::Serialize;
use tauri::http::{header, Method, Request, Response, StatusCode};
use tauri::window::Color;
use tauri::{AppHandle, Manager, Theme, UriSchemeContext, WebviewUrl, WebviewWindowBuilder, Wry};
use tauri_plugin_opener::OpenerExt;

use crate::appearance::{self, Appearance, ThemeMode};
use crate::localization::{self, keys};
use crate::logging;
use crate::update_state::{UpdateFailure, UpdatePhase, UpdateSnapshot};
use crate::updater::{self, UpdateInstallStrategy};

pub const UPDATE_WINDOW: &str = "update";
pub const SCHEME: &str = "macrodeck-update";

const PAGE_HTML: &str = include_str!("update-window.html");
const PAGE_CSS: &str = include_str!("update-window.css");
const PAGE_JS: &str = include_str!("update-window.js");

const CONTENT_SECURITY_POLICY: &str =
    "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'";

static OFFERED_VERSION: Mutex<Option<String>> = Mutex::new(None);
static ACTION_ERROR: Mutex<Option<(String, String)>> = Mutex::new(None);
static BUILDING: AtomicBool = AtomicBool::new(false);
static INSTALLING: AtomicBool = AtomicBool::new(false);

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) enum ActionId {
    Later,
    Install,
    Cancel,
    DownloadPage,
}

impl ActionId {
    fn parse(path: &str) -> Option<Self> {
        match path {
            "/actions/later" => Some(ActionId::Later),
            "/actions/install" => Some(ActionId::Install),
            "/actions/cancel" => Some(ActionId::Cancel),
            "/actions/download-page" => Some(ActionId::DownloadPage),
            _ => None,
        }
    }
}

#[derive(Clone, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct WindowAction {
    pub id: ActionId,
    pub label: String,
    pub primary: bool,
}

#[derive(Clone, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) enum StatusKind {
    Info,
    Error,
}

#[derive(Clone, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct WindowStatus {
    pub kind: StatusKind,
    pub text: String,
}

#[derive(Clone, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct WindowView {
    pub lang: String,
    pub theme: Option<&'static str>,
    pub accent: String,
    pub title: String,
    pub heading: Option<String>,
    pub current_version: String,
    pub changelog_heading: String,
    pub notes: Option<String>,
    pub no_changelog: String,
    pub status: Option<WindowStatus>,
    pub downloading: bool,
    pub progress_percent: Option<u8>,
    pub actions: Vec<WindowAction>,
}

fn action(id: ActionId, key: &str, primary: bool) -> WindowAction {
    WindowAction {
        id,
        label: localization::t(key),
        primary,
    }
}

fn info(text: String) -> Option<WindowStatus> {
    Some(WindowStatus {
        kind: StatusKind::Info,
        text,
    })
}

fn error(text: String) -> Option<WindowStatus> {
    Some(WindowStatus {
        kind: StatusKind::Error,
        text,
    })
}

pub(crate) fn offered_version(snapshot: &UpdateSnapshot) -> Option<&str> {
    if !snapshot.supported {
        return None;
    }
    match snapshot.phase {
        UpdatePhase::Idle | UpdatePhase::UpToDate | UpdatePhase::Unsupported => None,
        _ => snapshot.version.as_deref(),
    }
}

fn offer_actions(strategy: UpdateInstallStrategy) -> Vec<WindowAction> {
    let later = action(ActionId::Later, keys::UPDATE_LATER, false);
    match strategy {
        UpdateInstallStrategy::InApp => vec![
            later,
            action(ActionId::Install, keys::UPDATE_DOWNLOAD_AND_INSTALL, true),
        ],
        UpdateInstallStrategy::ExternalDownload => vec![
            later,
            action(
                ActionId::DownloadPage,
                keys::UPDATE_OPEN_DOWNLOAD_PAGE,
                true,
            ),
        ],
        UpdateInstallStrategy::Apt => vec![later],
    }
}

fn offer_status(strategy: UpdateInstallStrategy, version: Option<&str>) -> Option<WindowStatus> {
    let key = match strategy {
        UpdateInstallStrategy::InApp => return None,
        UpdateInstallStrategy::ExternalDownload => keys::UPDATE_EXTERNAL_AVAILABLE,
        UpdateInstallStrategy::Apt => keys::UPDATE_APT_AVAILABLE,
    };
    version
        .map(|version| localization::t_args(key, &[("version", version)]))
        .and_then(info)
}

pub(crate) fn view(snapshot: &UpdateSnapshot, action_error: Option<&str>) -> WindowView {
    let version = snapshot.version.as_deref();
    let strategy = snapshot.install_strategy;
    let later = || action(ActionId::Later, keys::UPDATE_LATER, false);
    let action_error = || action_error.map(str::to_string).and_then(error);
    let (status, actions) = match snapshot.phase {
        UpdatePhase::Available => (
            action_error().or_else(|| offer_status(strategy, version)),
            offer_actions(strategy),
        ),
        UpdatePhase::Downloading => {
            let text = match snapshot
                .progress
                .as_ref()
                .and_then(|progress| progress.percent)
            {
                Some(percent) => localization::t_args(
                    keys::UPDATE_WINDOW_DOWNLOADING_STATUS,
                    &[("percent", &percent.to_string())],
                ),
                None => localization::t(keys::UPDATE_WINDOW_PREPARING_STATUS),
            };
            (
                info(text),
                vec![action(
                    ActionId::Cancel,
                    keys::UPDATE_WINDOW_CANCEL_DOWNLOAD,
                    false,
                )],
            )
        }
        UpdatePhase::Downloaded => (
            action_error().or_else(|| info(localization::t(keys::UPDATE_WINDOW_READY_STATUS))),
            vec![
                later(),
                action(ActionId::Install, keys::UPDATE_WINDOW_RESTART_NOW, true),
            ],
        ),
        UpdatePhase::Installing => (
            info(localization::t(keys::UPDATE_WINDOW_INSTALLING_STATUS)),
            Vec::new(),
        ),
        UpdatePhase::Failed
            if strategy == UpdateInstallStrategy::InApp
                && snapshot.failure != Some(UpdateFailure::Check) =>
        {
            (
                snapshot.error.clone().and_then(error).or_else(action_error),
                vec![
                    later(),
                    action(ActionId::Install, keys::UPDATE_WINDOW_TRY_AGAIN, true),
                ],
            )
        }
        UpdatePhase::Failed => (offer_status(strategy, version), offer_actions(strategy)),
        UpdatePhase::Checking
        | UpdatePhase::Idle
        | UpdatePhase::UpToDate
        | UpdatePhase::Unsupported => (None, vec![later()]),
    };

    WindowView {
        lang: localization::culture(),
        theme: None,
        accent: Appearance::default().accent_color,
        title: localization::t(keys::UPDATE_AVAILABLE_TITLE),
        heading: version.map(|version| {
            localization::t_args(keys::UPDATE_WINDOW_VERSION_HEADING, &[("version", version)])
        }),
        current_version: localization::t_args(
            keys::UPDATE_WINDOW_CURRENT_VERSION,
            &[("version", &snapshot.current_version)],
        ),
        changelog_heading: localization::t(keys::UPDATE_WINDOW_CHANGELOG_HEADING),
        notes: snapshot
            .notes
            .as_deref()
            .map(str::trim)
            .filter(|notes| !notes.is_empty())
            .map(str::to_string),
        no_changelog: localization::t(keys::UPDATE_WINDOW_NO_CHANGELOG),
        status,
        downloading: snapshot.phase == UpdatePhase::Downloading,
        progress_percent: snapshot
            .progress
            .as_ref()
            .and_then(|progress| progress.percent),
        actions,
    }
}

pub(crate) fn themed(view: WindowView, appearance: &Appearance) -> WindowView {
    WindowView {
        theme: match appearance.theme_mode {
            ThemeMode::Light => Some("light"),
            ThemeMode::Dark => Some("dark"),
            ThemeMode::System => None,
        },
        accent: appearance.accent_color.clone(),
        ..view
    }
}

fn window_theme(appearance: &Appearance) -> Option<Theme> {
    match appearance.theme_mode {
        ThemeMode::Light => Some(Theme::Light),
        ThemeMode::Dark => Some(Theme::Dark),
        ThemeMode::System => None,
    }
}

pub(crate) fn claim_offer(offered: &mut Option<String>, version: &str) -> bool {
    if offered.as_deref() == Some(version) {
        return false;
    }
    *offered = Some(version.to_string());
    true
}

pub fn offer(app: &AppHandle, version: &str) {
    let claimed = claim_offer(
        &mut OFFERED_VERSION.lock().unwrap_or_else(|e| e.into_inner()),
        version,
    );
    if claimed {
        *ACTION_ERROR.lock().unwrap_or_else(|e| e.into_inner()) = None;
        logging::info(&format!("[update-window] offering {version}"));
        show(app);
    }
}

fn page_url() -> tauri::Url {
    let raw = if cfg!(any(windows, target_os = "android")) {
        format!("http://{SCHEME}.localhost/")
    } else {
        format!("{SCHEME}://localhost/")
    };
    raw.parse()
        .expect("the update window URL is a constant and always parses")
}

pub(crate) fn is_own_page(url: &tauri::Url) -> bool {
    let own = page_url();
    url.scheme() == own.scheme() && url.host_str() == own.host_str()
}

fn focus(app: &AppHandle) {
    let handle = app.clone();
    let result = app.run_on_main_thread(move || {
        if let Some(window) = handle.get_webview_window(UPDATE_WINDOW) {
            let _ = window.show();
            let _ = window.unminimize();
            let _ = window.set_focus();
        }
    });
    if let Err(error) = result {
        logging::error(&format!(
            "[update-window] could not show the window: {error}"
        ));
    }
}

// Built from an async task like the main window: building inside a main-thread
// closure can deadlock WebView2.
pub fn show(app: &AppHandle) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        if app.get_webview_window(UPDATE_WINDOW).is_some() {
            focus(&app);
            return;
        }
        if BUILDING.swap(true, Ordering::SeqCst) {
            return;
        }
        let built = build(&app);
        BUILDING.store(false, Ordering::SeqCst);
        if built {
            paint_background(&app);
            focus(&app);
        } else if app.get_webview_window(UPDATE_WINDOW).is_none() {
            *OFFERED_VERSION.lock().unwrap_or_else(|e| e.into_inner()) = None;
        }
    });
}

fn build(app: &AppHandle) -> bool {
    let opener = app.clone();
    let builder =
        WebviewWindowBuilder::new(app, UPDATE_WINDOW, WebviewUrl::CustomProtocol(page_url()))
            .title(localization::t(keys::UPDATE_AVAILABLE_TITLE))
            .inner_size(560.0, 640.0)
            .min_inner_size(420.0, 420.0)
            .center()
            .focused(true)
            .zoom_hotkeys_enabled(false)
            // Release-note links must open in the browser, never inside this window.
            .on_navigation(move |url| {
                if is_own_page(url) {
                    return true;
                }
                if url.scheme() == "https" {
                    let _ = opener.opener().open_url(url.as_str(), None::<&str>);
                }
                false
            });

    let theme = window_theme(&appearance::current(app));
    let builder = match theme {
        Some(theme) => builder
            .theme(Some(theme))
            .background_color(background_for(theme)),
        None => builder,
    };

    #[cfg(target_os = "macos")]
    let builder = builder.title_bar_style(tauri::TitleBarStyle::Transparent);

    match builder.build() {
        Ok(window) => {
            let handle = app.clone();
            window.on_window_event(move |event| {
                if let tauri::WindowEvent::ThemeChanged(_) = event {
                    paint_background(&handle);
                }
            });
            true
        }
        Err(error) => {
            logging::error(&format!(
                "[update-window] could not create the window: {error}"
            ));
            false
        }
    }
}

// Must match --color-bg-primary in update-window.css, which the transparent macOS
// title bar shows through to.
fn background_for(theme: Theme) -> Color {
    match theme {
        Theme::Light => Color(0xff, 0xff, 0xff, 0xff),
        _ => Color(0x12, 0x12, 0x12, 0xff),
    }
}

pub fn apply_appearance(app: &AppHandle) {
    if app.get_webview_window(UPDATE_WINDOW).is_none() {
        return;
    }
    let theme = window_theme(&appearance::current(app));
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        if let Some(window) = handle.get_webview_window(UPDATE_WINDOW) {
            let _ = window.set_theme(theme);
        }
    });
    paint_background(app);
}

fn paint_background(app: &AppHandle) {
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        if let Some(window) = handle.get_webview_window(UPDATE_WINDOW) {
            let theme = window.theme().unwrap_or(Theme::Dark);
            let _ = window.set_background_color(Some(background_for(theme)));
        }
    });
}

pub fn close(app: &AppHandle) {
    if app.get_webview_window(UPDATE_WINDOW).is_none() {
        return;
    }
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        if let Some(window) = handle.get_webview_window(UPDATE_WINDOW) {
            let _ = window.close();
        }
    });
}

fn run_action(app: &AppHandle, id: ActionId) {
    match id {
        ActionId::Later => {
            updater::postpone_automatic_install(app.clone());
            close(app);
        }
        ActionId::Install => {
            // A second click while the first attempt still resolves the feed is ignored, not
            // reported back as an install already in progress.
            if INSTALLING.swap(true, Ordering::SeqCst) {
                return;
            }
            let app = app.clone();
            tauri::async_runtime::spawn(async move {
                let version = updater::snapshot(&app).version;
                let result = updater::install_update(app).await;
                INSTALLING.store(false, Ordering::SeqCst);
                let mut stored = ACTION_ERROR.lock().unwrap_or_else(|e| e.into_inner());
                *stored = match (result, version) {
                    (Err(error), Some(version)) => Some((version, error)),
                    _ => None,
                };
            });
        }
        ActionId::Cancel => {
            let app = app.clone();
            tauri::async_runtime::spawn(async move { updater::cancel_update_download(app).await });
        }
        ActionId::DownloadPage => {
            let _ = app
                .opener()
                .open_url(updater::DOWNLOAD_PAGE_URL, None::<&str>);
            close(app);
        }
    }
}

pub(crate) fn current_action_error(
    stored: &mut Option<(String, String)>,
    snapshot: &UpdateSnapshot,
) -> Option<String> {
    if matches!(
        snapshot.phase,
        UpdatePhase::Downloading | UpdatePhase::Installing
    ) {
        *stored = None;
    }
    match stored {
        Some((version, error)) if snapshot.version.as_deref() == Some(version.as_str()) => {
            Some(error.clone())
        }
        _ => None,
    }
}

fn respond(status: StatusCode, content_type: &str, body: Vec<u8>) -> Response<Vec<u8>> {
    Response::builder()
        .status(status)
        .header(header::CONTENT_TYPE, content_type)
        .header(header::CONTENT_SECURITY_POLICY, CONTENT_SECURITY_POLICY)
        .header(header::CACHE_CONTROL, "no-store")
        .body(body)
        .unwrap_or_else(|_| Response::new(Vec::new()))
}

fn empty(status: StatusCode) -> Response<Vec<u8>> {
    respond(status, "text/plain", Vec::new())
}

// Only this window may drive it: the scheme is reachable from every webview,
// including the main window's plugin content.
pub fn handle_request(
    context: UriSchemeContext<'_, Wry>,
    request: Request<Vec<u8>>,
) -> Response<Vec<u8>> {
    if context.webview_label() != UPDATE_WINDOW {
        return empty(StatusCode::FORBIDDEN);
    }
    let app = context.app_handle().clone();
    let path = request.uri().path();

    if request.method() == Method::POST {
        return match ActionId::parse(path) {
            Some(id) => {
                run_action(&app, id);
                empty(StatusCode::NO_CONTENT)
            }
            None => empty(StatusCode::NOT_FOUND),
        };
    }
    if request.method() != Method::GET {
        return empty(StatusCode::METHOD_NOT_ALLOWED);
    }
    match path {
        "/" | "/index.html" => {
            respond(StatusCode::OK, "text/html; charset=utf-8", PAGE_HTML.into())
        }
        "/update-window.css" => respond(StatusCode::OK, "text/css; charset=utf-8", PAGE_CSS.into()),
        "/update-window.js" => respond(
            StatusCode::OK,
            "text/javascript; charset=utf-8",
            PAGE_JS.into(),
        ),
        "/state" => {
            let snapshot = updater::snapshot(&app);
            let action_error = current_action_error(
                &mut ACTION_ERROR.lock().unwrap_or_else(|e| e.into_inner()),
                &snapshot,
            );
            let view = themed(
                view(&snapshot, action_error.as_deref()),
                &appearance::current(&app),
            );
            match serde_json::to_vec(&view) {
                Ok(body) => respond(StatusCode::OK, "application/json", body),
                Err(_) => empty(StatusCode::INTERNAL_SERVER_ERROR),
            }
        }
        _ => empty(StatusCode::NOT_FOUND),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::update_channel::UpdateChannel;
    use crate::update_state::UpdateState;

    fn state(strategy: UpdateInstallStrategy) -> UpdateState {
        let mut state = UpdateState::idle(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            strategy,
            "https://macro-deck.app/download",
        );
        state.record_available(
            1,
            "3.1.0".to_string(),
            Some("## Fixes\n* One".into()),
            None,
            None,
        );
        state
    }

    fn action_ids(view: &WindowView) -> Vec<ActionId> {
        view.actions.iter().map(|action| action.id).collect()
    }

    fn primary(view: &WindowView) -> Option<ActionId> {
        view.actions
            .iter()
            .find(|action| action.primary)
            .map(|action| action.id)
    }

    #[test]
    fn an_available_update_shows_its_changelog_and_offers_download_or_later() {
        let view = view(&state(UpdateInstallStrategy::InApp).snapshot(), None);

        assert_eq!(view.heading.as_deref(), Some("Macro Deck 3.1.0"));
        assert!(view.current_version.contains("3.0.0"));
        assert_eq!(view.notes.as_deref(), Some("## Fixes\n* One"));
        assert_eq!(action_ids(&view), [ActionId::Later, ActionId::Install]);
        assert_eq!(primary(&view), Some(ActionId::Install));
        assert!(view.status.is_none());
    }

    #[test]
    fn a_downloaded_update_offers_restart_now_or_later() {
        let mut state = state(UpdateInstallStrategy::InApp);
        state.record_downloaded();
        let view = view(&state.snapshot(), None);

        assert_eq!(action_ids(&view), [ActionId::Later, ActionId::Install]);
        let install = view
            .actions
            .iter()
            .find(|action| action.id == ActionId::Install)
            .unwrap();
        assert_eq!(
            install.label,
            localization::t(keys::UPDATE_WINDOW_RESTART_NOW)
        );
        assert_eq!(
            view.status.map(|status| status.kind),
            Some(StatusKind::Info)
        );
    }

    #[test]
    fn a_running_download_shows_progress_and_can_only_be_cancelled() {
        let mut state = state(UpdateInstallStrategy::InApp);
        assert!(state.try_begin_download());
        state.record_progress(50, Some(200));
        let view = view(&state.snapshot(), None);

        assert!(view.downloading);
        assert_eq!(view.progress_percent, Some(25));
        assert!(view.status.as_ref().unwrap().text.contains("25"));
        assert_eq!(action_ids(&view), [ActionId::Cancel]);
    }

    #[test]
    fn a_failed_install_is_shown_as_an_error_with_a_retry() {
        let mut state = state(UpdateInstallStrategy::InApp);
        state.record_install_failed("disk full".to_string());
        let view = view(&state.snapshot(), None);

        assert_eq!(
            view.status,
            Some(WindowStatus {
                kind: StatusKind::Error,
                text: "disk full".to_string()
            })
        );
        assert_eq!(action_ids(&view), [ActionId::Later, ActionId::Install]);
    }

    #[test]
    fn an_install_that_failed_before_changing_state_still_tells_the_user_why() {
        let view = view(
            &state(UpdateInstallStrategy::InApp).snapshot(),
            Some("feed unreachable"),
        );

        assert_eq!(
            view.status,
            Some(WindowStatus {
                kind: StatusKind::Error,
                text: "feed unreachable".to_string()
            })
        );
    }

    #[test]
    fn an_install_can_only_be_started_where_the_app_installs_updates_itself() {
        let external = view(
            &state(UpdateInstallStrategy::ExternalDownload).snapshot(),
            None,
        );
        assert_eq!(
            action_ids(&external),
            [ActionId::Later, ActionId::DownloadPage]
        );

        let apt = view(&state(UpdateInstallStrategy::Apt).snapshot(), None);
        assert_eq!(action_ids(&apt), [ActionId::Later]);
        assert!(apt.status.unwrap().text.contains("apt"));
    }

    #[test]
    fn the_tray_offers_a_known_update_until_a_check_finds_none() {
        let mut state = state(UpdateInstallStrategy::InApp);
        assert_eq!(offered_version(&state.snapshot()), Some("3.1.0"));

        state.record_downloaded();
        assert_eq!(offered_version(&state.snapshot()), Some("3.1.0"));

        state.record_check_failed(2, "offline".to_string());
        assert_eq!(
            offered_version(&state.snapshot()),
            Some("3.1.0"),
            "a failed re-check says nothing new about the update"
        );

        state.record_up_to_date(3, None);
        assert_eq!(offered_version(&state.snapshot()), None);
    }

    #[test]
    fn an_unsupported_install_never_offers_an_update() {
        let mut state = UpdateState::unsupported(
            "3.0.0".to_string(),
            UpdateChannel::Stable,
            UpdateInstallStrategy::InApp,
            "https://macro-deck.app/download",
        );
        state.version = Some("3.1.0".to_string());
        assert_eq!(offered_version(&state.snapshot()), None);
    }

    #[test]
    fn the_window_opens_on_its_own_only_once_per_version() {
        let mut offered = None;

        assert!(claim_offer(&mut offered, "3.1.0"));
        assert!(
            !claim_offer(&mut offered, "3.1.0"),
            "Later must not be undone by the next check"
        );
        assert!(
            claim_offer(&mut offered, "3.2.0"),
            "a newer release is offered again"
        );
    }

    #[test]
    fn only_the_window_own_page_may_load_inside_it() {
        assert!(is_own_page(&page_url()));
        assert!(is_own_page(&page_url().join("state").unwrap()));
        assert!(!is_own_page(
            &"https://github.com/Macro-Deck-App/Macro-Deck/pull/1"
                .parse()
                .unwrap()
        ));
        assert!(!is_own_page(
            &"http://127.0.0.1:5291/admin".parse().unwrap()
        ));
    }

    #[test]
    fn no_install_is_offered_while_the_update_is_not_ready_for_one() {
        let mut state = state(UpdateInstallStrategy::InApp);
        assert!(state.try_begin_check());
        assert_eq!(
            action_ids(&view(&state.snapshot(), None)),
            [ActionId::Later]
        );

        state.record_up_to_date(2, None);
        assert_eq!(
            action_ids(&view(&state.snapshot(), None)),
            [ActionId::Later]
        );
    }

    #[test]
    fn a_failed_check_on_linux_still_points_to_the_download_page() {
        let mut state = state(UpdateInstallStrategy::ExternalDownload);
        state.record_check_failed(2, "offline".to_string());

        assert_eq!(
            action_ids(&view(&state.snapshot(), None)),
            [ActionId::Later, ActionId::DownloadPage]
        );
    }

    #[test]
    fn an_install_error_only_shows_for_its_own_version_until_an_install_starts() {
        let mut state = state(UpdateInstallStrategy::InApp);
        let mut stored = Some(("3.1.0".to_string(), "feed unreachable".to_string()));
        assert_eq!(
            current_action_error(&mut stored, &state.snapshot()).as_deref(),
            Some("feed unreachable")
        );

        state.record_available(2, "3.2.0".to_string(), None, None, None);
        assert_eq!(current_action_error(&mut stored, &state.snapshot()), None);

        let mut stored = Some(("3.2.0".to_string(), "feed unreachable".to_string()));
        assert!(state.try_begin_download());
        assert_eq!(current_action_error(&mut stored, &state.snapshot()), None);
        assert_eq!(stored, None);
    }

    #[test]
    fn the_title_bar_colour_matches_the_page_background_in_both_themes() {
        let hex = |color: Color| format!("#{:02x}{:02x}{:02x}", color.0, color.1, color.2);
        let css = PAGE_CSS.replace(' ', "");
        let (dark, light) = css
            .split_once("@media(prefers-color-scheme:light)")
            .unwrap();

        assert!(dark.contains(&format!(
            "--color-bg-primary:{};",
            hex(background_for(Theme::Dark))
        )));
        assert!(light.contains(&format!(
            "--color-bg-primary:{};",
            hex(background_for(Theme::Light))
        )));
    }

    #[test]
    fn the_window_follows_the_theme_and_accent_chosen_in_macro_deck() {
        let base = view(&state(UpdateInstallStrategy::InApp).snapshot(), None);

        let dark = themed(base.clone(), &appearance::parse("dark", "#FF5722").unwrap());
        assert_eq!(dark.theme, Some("dark"));
        assert_eq!(dark.accent, "#ff5722");

        let system = themed(base, &Appearance::default());
        assert_eq!(
            system.theme, None,
            "system leaves the choice to the operating system"
        );
    }

    #[test]
    fn a_failed_recheck_keeps_offering_the_known_update() {
        let mut state = state(UpdateInstallStrategy::InApp);
        state.record_check_failed(2, "offline".to_string());
        let view = view(&state.snapshot(), None);

        assert_eq!(action_ids(&view), [ActionId::Later, ActionId::Install]);
        let install = view
            .actions
            .iter()
            .find(|action| action.id == ActionId::Install)
            .unwrap();
        assert_eq!(
            install.label,
            localization::t(keys::UPDATE_DOWNLOAD_AND_INSTALL)
        );
    }
}
