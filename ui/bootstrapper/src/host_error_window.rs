use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

use tauri::http::{header, Request, Response, StatusCode};
use tauri::webview::NewWindowResponse;
use tauri::{AppHandle, Manager, Url, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_opener::OpenerExt;

use crate::localization::{self, keys};
use crate::logging;

pub const HOST_ERROR_WINDOW: &str = "host-error";

pub const SCHEME: &str = "macrodeck-error";

pub const ISSUES_URL: &str = "https://github.com/Macro-Deck-App/Macro-Deck/issues";

const TEMPLATE: &str = include_str!("host-error.html");

// The page renders untrusted host output, so it gets no network, no frames and no IPC access; the
// inline script only copies the details.
const CONTENT_SECURITY_POLICY: &str =
    "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'";

static REPORT: Mutex<Option<HostErrorReport>> = Mutex::new(None);

static RELAUNCH_FAILED: AtomicBool = AtomicBool::new(false);

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum HostErrorKind {
    Stopped,
    NotStarted,
    RestartFailed,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExitStatus {
    NotRecorded,
    Unknown,
    Code(i32),
}

impl ExitStatus {
    pub fn from_code(code: Option<i32>) -> Self {
        code.map_or(ExitStatus::Unknown, ExitStatus::Code)
    }
}

#[derive(Debug, Clone)]
pub struct HostErrorReport {
    pub kind: HostErrorKind,
    pub attempts: u32,
    pub reason: Option<String>,
    pub exit: ExitStatus,
    pub log: String,
}

#[derive(Debug, Clone, Default)]
pub struct HostErrorTexts {
    pub lang: String,
    pub title: String,
    pub heading: String,
    pub notice: Option<String>,
    pub attempts: Option<String>,
    pub reason: Option<String>,
    pub help: String,
    pub details_label: String,
    pub details: String,
    pub copy: String,
    pub copied: String,
    pub report_issue: String,
    pub discord: String,
    pub restart: String,
    pub quit: String,
}

pub fn show(app: &AppHandle, report: HostErrorReport) {
    if let Ok(mut slot) = REPORT.lock() {
        *slot = Some(report);
    }
    RELAUNCH_FAILED.store(false, Ordering::SeqCst);
    open_off_main_thread(app);
}

pub fn focus(app: &AppHandle) -> bool {
    let Some(window) = app.get_webview_window(HOST_ERROR_WINDOW) else {
        return false;
    };
    let _ = window.show();
    let _ = window.unminimize();
    let _ = window.set_focus();
    true
}

// Building a webview window from the main thread can deadlock on Windows, and callers here include
// run_on_main_thread closures, so the window is always created from the async runtime.
fn open_off_main_thread(app: &AppHandle) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move { open(&app) });
}

fn open(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(HOST_ERROR_WINDOW) {
        if let Err(error) = window.reload() {
            logging::error(&format!(
                "[host-error] could not refresh the window: {error}"
            ));
        }
        focus(app);
        return;
    }

    #[cfg(target_os = "macos")]
    let _ = app.set_activation_policy(tauri::ActivationPolicy::Regular);

    let title = localization::t(keys::HOST_ERROR_WINDOW_TITLE);
    let handle = app.clone();
    let built = WebviewWindowBuilder::new(
        app,
        HOST_ERROR_WINDOW,
        WebviewUrl::CustomProtocol(page_url()),
    )
    .title(&title)
    .inner_size(760.0, 620.0)
    .min_inner_size(520.0, 420.0)
    .center()
    .focused(true)
    .on_navigation(move |url| handle_navigation(&handle, url))
    .on_new_window(|_, _| NewWindowResponse::Deny)
    .build();

    match built {
        Ok(window) => {
            let app = app.clone();
            window.on_window_event(move |event| {
                if let tauri::WindowEvent::CloseRequested { .. } = event {
                    quit(&app);
                }
            });
        }
        Err(error) => {
            logging::error(&format!("[host-error] could not open the window: {error}"));
            let details = current_report()
                .map(|report| texts(&report, false).details)
                .unwrap_or_default();
            crate::window::show_error_dialog(app, &title, &details);
            quit(app);
        }
    }
}

fn current_report() -> Option<HostErrorReport> {
    REPORT.lock().ok().and_then(|slot| slot.clone())
}

// Custom schemes are served as http://<scheme>.localhost on Windows (WebView2) and as
// <scheme>://localhost elsewhere; Tauri does not rewrite the URL for us.
pub fn page_url() -> Url {
    let raw = if cfg!(windows) {
        "http://macrodeck-error.localhost/"
    } else {
        "macrodeck-error://localhost/"
    };
    Url::parse(raw).expect("the error page URL is a valid constant")
}

fn is_own_origin(url: &Url) -> bool {
    match url.scheme() {
        SCHEME => url.host_str() == Some("localhost"),
        "http" | "https" => url.host_str() == Some("macrodeck-error.localhost"),
        _ => false,
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NavigationAction {
    Load,
    Open(&'static str),
    Restart,
    Quit,
    Block,
}

pub fn navigation_action(url: &Url) -> NavigationAction {
    if is_own_origin(url) {
        return match url.path() {
            "/" => NavigationAction::Load,
            "/action/restart" => NavigationAction::Restart,
            "/action/quit" => NavigationAction::Quit,
            _ => NavigationAction::Block,
        };
    }
    [ISSUES_URL, crate::menu::DISCORD_URL]
        .into_iter()
        .find(|target| Url::parse(target).is_ok_and(|target| target == *url))
        .map_or(NavigationAction::Block, NavigationAction::Open)
}

fn handle_navigation(app: &AppHandle, url: &Url) -> bool {
    match navigation_action(url) {
        NavigationAction::Load => true,
        NavigationAction::Open(target) => {
            if let Err(error) = app.opener().open_url(target, None::<&str>) {
                logging::warn(&format!("[host-error] could not open {target}: {error}"));
            }
            false
        }
        NavigationAction::Restart => {
            restart(app);
            false
        }
        NavigationAction::Quit => {
            quit(app);
            false
        }
        NavigationAction::Block => {
            logging::warn(&format!("[host-error] blocked navigation to {url}"));
            false
        }
    }
}

fn restart(app: &AppHandle) {
    logging::info("[host-error] restarting Macro Deck on request");
    let handle = app.clone();
    let dispatched = app.run_on_main_thread(move || {
        crate::mark_quitting(&handle);
        if !crate::host::relaunch(&handle) {
            crate::clear_quitting();
            RELAUNCH_FAILED.store(true, Ordering::SeqCst);
            open_off_main_thread(&handle);
        }
    });
    if let Err(error) = dispatched {
        logging::error(&format!(
            "[host-error] could not dispatch the restart: {error}"
        ));
    }
}

fn quit(app: &AppHandle) {
    crate::mark_quitting(app);
    app.exit(1);
}

pub fn respond(webview_label: &str, request: &Request<Vec<u8>>) -> Response<Vec<u8>> {
    let report = (webview_label == HOST_ERROR_WINDOW && request.uri().path() == "/")
        .then(current_report)
        .flatten();
    let Some(report) = report else {
        return Response::builder()
            .status(StatusCode::NOT_FOUND)
            .body(Vec::new())
            .unwrap_or_default();
    };

    let html = render(&texts(&report, RELAUNCH_FAILED.load(Ordering::SeqCst)));
    Response::builder()
        .status(StatusCode::OK)
        .header(header::CONTENT_TYPE, "text/html; charset=utf-8")
        .header(header::CONTENT_SECURITY_POLICY, CONTENT_SECURITY_POLICY)
        .header(header::CACHE_CONTROL, "no-store")
        .body(html.into_bytes())
        .unwrap_or_default()
}

fn texts(report: &HostErrorReport, relaunch_failed: bool) -> HostErrorTexts {
    let heading = match report.kind {
        HostErrorKind::Stopped => localization::t(keys::HOST_ERROR_HEADING_STOPPED),
        HostErrorKind::NotStarted => localization::t(keys::HOST_ERROR_HEADING_NOT_STARTED),
        HostErrorKind::RestartFailed => localization::t(keys::ERRORS_COULD_NOT_RESTART_TITLE),
    };
    let attempts = (report.attempts > 0)
        .then(|| localization::t_plural(keys::HOST_ERROR_ATTEMPTS, report.attempts.into(), &[]));
    let exit = match report.exit {
        ExitStatus::NotRecorded => None,
        ExitStatus::Unknown => Some(localization::t(keys::HOST_ERROR_EXIT_CODE_UNKNOWN)),
        ExitStatus::Code(code) => Some(localization::t_args(
            keys::HOST_ERROR_EXIT_CODE,
            &[("code", &code.to_string())],
        )),
    };

    HostErrorTexts {
        lang: localization::culture(),
        title: localization::t(keys::HOST_ERROR_WINDOW_TITLE),
        heading,
        notice: relaunch_failed.then(|| localization::t(keys::HOST_ERROR_RESTART_FAILED)),
        attempts,
        reason: report.reason.clone(),
        help: localization::t(keys::HOST_ERROR_HELP),
        details_label: localization::t(keys::HOST_ERROR_DETAILS_LABEL),
        details: details_text(exit.as_deref(), report.reason.as_deref(), &report.log),
        copy: localization::t(keys::HOST_ERROR_COPY_DETAILS),
        copied: localization::t(keys::HOST_ERROR_COPIED),
        report_issue: localization::t(keys::HOST_ERROR_REPORT_ISSUE),
        discord: localization::t(keys::MENU_DISCORD),
        restart: localization::t(keys::HOST_ERROR_RESTART_MACRO_DECK),
        quit: localization::t(keys::TRAY_QUIT),
    }
}

pub fn details_text(exit: Option<&str>, reason: Option<&str>, log: &str) -> String {
    [exit, reason, Some(log.trim())]
        .into_iter()
        .flatten()
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>()
        .join("\n\n")
}

pub fn render(texts: &HostErrorTexts) -> String {
    let paragraph = |class: &str, text: &Option<String>| {
        text.as_deref()
            .map(|text| format!("<p class=\"{class}\">{}</p>", escape(text)))
            .unwrap_or_default()
    };

    let mut html = String::with_capacity(TEMPLATE.len() + texts.details.len());
    let mut rest = TEMPLATE;
    while let Some(start) = rest.find("{{") {
        let Some(length) = rest[start..].find("}}") else {
            break;
        };
        html.push_str(&rest[..start]);
        let name = &rest[start + 2..start + length];
        let value = match name {
            "lang" => escape(&texts.lang),
            "title" => escape(&texts.title),
            "heading" => escape(&texts.heading),
            "notice" => paragraph("notice", &texts.notice),
            "attempts" => paragraph("", &texts.attempts),
            "reason" => paragraph("", &texts.reason),
            "help" => escape(&texts.help),
            "details_label" => escape(&texts.details_label),
            "details" => escape(&texts.details),
            "copy" => escape(&texts.copy),
            "copied" => escape(&texts.copied),
            "issues_url" => escape(ISSUES_URL),
            "report_issue" => escape(&texts.report_issue),
            "discord_url" => escape(crate::menu::DISCORD_URL),
            "discord" => escape(&texts.discord),
            "restart" => escape(&texts.restart),
            "quit" => escape(&texts.quit),
            _ => String::new(),
        };
        html.push_str(&value);
        rest = &rest[start + length + 2..];
    }
    html.push_str(rest);
    html
}

fn escape(text: &str) -> String {
    let mut escaped = String::with_capacity(text.len());
    for character in text.chars() {
        match character {
            '&' => escaped.push_str("&amp;"),
            '<' => escaped.push_str("&lt;"),
            '>' => escaped.push_str("&gt;"),
            '"' => escaped.push_str("&quot;"),
            '\'' => escaped.push_str("&#39;"),
            _ => escaped.push(character),
        }
    }
    escaped
}

#[cfg(test)]
mod tests {
    use super::*;

    fn url(raw: &str) -> Url {
        Url::parse(raw).unwrap()
    }

    fn sample_texts() -> HostErrorTexts {
        HostErrorTexts {
            lang: "en".into(),
            title: "Macro Deck stopped".into(),
            heading: "Macro Deck could not keep its host running".into(),
            attempts: Some("Macro Deck tried 3 restarts".into()),
            help: "Copy the details".into(),
            details_label: "Details".into(),
            details: details_text(Some("Exit code: 3"), None, "boom"),
            copy: "Copy details".into(),
            copied: "Copied".into(),
            report_issue: "Report the problem on GitHub".into(),
            discord: "Macro Deck on Discord".into(),
            restart: "Restart Macro Deck".into(),
            quit: "Quit".into(),
            ..Default::default()
        }
    }

    #[test]
    fn the_page_itself_loads_on_every_platform() {
        assert_eq!(
            navigation_action(&url("macrodeck-error://localhost/")),
            NavigationAction::Load
        );
        assert_eq!(
            navigation_action(&url("http://macrodeck-error.localhost/")),
            NavigationAction::Load
        );
        assert_eq!(navigation_action(&page_url()), NavigationAction::Load);
    }

    #[test]
    fn the_buttons_become_actions_on_every_platform() {
        assert_eq!(
            navigation_action(&url("macrodeck-error://localhost/action/restart")),
            NavigationAction::Restart
        );
        assert_eq!(
            navigation_action(&url("http://macrodeck-error.localhost/action/restart")),
            NavigationAction::Restart
        );
        assert_eq!(
            navigation_action(&url("macrodeck-error://localhost/action/quit")),
            NavigationAction::Quit
        );
        assert_eq!(
            navigation_action(&url("http://macrodeck-error.localhost/action/quit")),
            NavigationAction::Quit
        );
    }

    #[test]
    fn only_the_support_links_open_in_the_browser() {
        assert_eq!(
            navigation_action(&url(ISSUES_URL)),
            NavigationAction::Open(ISSUES_URL)
        );
        assert_eq!(
            navigation_action(&url("https://discord.macro-deck.app")),
            NavigationAction::Open(crate::menu::DISCORD_URL)
        );
        assert_eq!(
            navigation_action(&url("https://example.com/")),
            NavigationAction::Block
        );
        assert_eq!(
            navigation_action(&url(
                "https://github.com/Macro-Deck-App/Macro-Deck/issues/new"
            )),
            NavigationAction::Block
        );
        assert_eq!(
            navigation_action(&url("file:///etc/passwd")),
            NavigationAction::Block
        );
    }

    #[test]
    fn other_pages_and_look_alike_hosts_are_blocked() {
        assert_eq!(
            navigation_action(&url("macrodeck-error://localhost/elsewhere")),
            NavigationAction::Block
        );
        assert_eq!(
            navigation_action(&url(
                "http://macrodeck-error.localhost.example.com/action/restart"
            )),
            NavigationAction::Block
        );
        assert_eq!(
            navigation_action(&url("http://127.0.0.1:5000/action/restart")),
            NavigationAction::Block
        );
    }

    #[test]
    fn host_output_is_rendered_as_text() {
        let mut texts = sample_texts();
        texts.details = "<script>alert(1)</script> & {{quit}}".into();

        let html = render(&texts);

        assert!(html.contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; {{quit}}"));
        assert!(!html.contains("<script>alert(1)"));
    }

    #[test]
    fn the_page_offers_details_links_and_actions() {
        let html = render(&sample_texts());

        assert!(html.contains("Macro Deck tried 3 restarts"));
        assert!(html.contains("Exit code: 3\n\nboom"));
        assert!(html.contains("href=\"https://github.com/Macro-Deck-App/Macro-Deck/issues\""));
        assert!(html.contains("href=\"https://discord.macro-deck.app\""));
        assert!(html.contains("href=\"action/restart\""));
        assert!(html.contains("href=\"action/quit\""));
        assert!(!html.contains("{{"));
    }

    #[test]
    fn a_report_without_attempts_shows_no_attempt_count() {
        let mut texts = sample_texts();
        texts.attempts = None;

        assert!(!render(&texts).contains("restarts"));
    }

    #[test]
    fn details_leave_out_what_is_unknown() {
        assert_eq!(details_text(None, Some("No free port"), ""), "No free port");
        assert_eq!(
            details_text(Some("Exit code: 3"), None, "  line\n"),
            "Exit code: 3\n\nline"
        );
    }

    #[test]
    fn only_the_error_window_is_served_the_page() {
        let request = Request::builder()
            .uri("macrodeck-error://localhost/")
            .body(Vec::new())
            .unwrap();

        assert_eq!(respond("main", &request).status(), StatusCode::NOT_FOUND);
    }
}
