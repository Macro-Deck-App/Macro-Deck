// OS notifications posted from this bundle. The host raises them (pairing requests, the "send
// notification" action), but a notification is attributed to whichever process posts it - and the
// host's own helpers are `osascript`, a tray balloon and `notify-send`, none of which can carry the
// Macro Deck identity. So the host queues them and this module long-polls for them, shows them, and
// reports back whether that worked; on `false` the host falls back to its own helper, which is why
// nothing is lost when the OS refuses us.

use std::sync::Arc;
use std::time::Duration;

use serde::Deserialize;
use tauri::{AppHandle, Manager};

use crate::host::{self, HostState};
use crate::{install_state, logging};

const REQUEST_TIMEOUT: Duration = Duration::from_secs(30);
// Well inside the host's lease, so a notification system that hangs instead of failing still ends as
// a reported failure the host can fall back on.
const SHOW_TIMEOUT: Duration = Duration::from_secs(3);
const MIN_BACKOFF: Duration = Duration::from_secs(1);
const MAX_BACKOFF: Duration = Duration::from_secs(30);

#[derive(Deserialize)]
struct PollResponse {
    #[serde(default)]
    notifications: Vec<HostNotification>,
}

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct HostNotification {
    id: i64,
    title: String,
    message: String,
}

/// The shell may only take notifications away from the host when it can actually brand them: an
/// unpackaged or uninstalled build has no registered identity, so its notifications would arrive as
/// "Terminal" or "PowerShell" - worse than what the host already shows.
fn should_claim(packaged: bool, installed: bool, bundle_registered: bool) -> bool {
    packaged && installed && bundle_registered
}

fn next_backoff(current: Duration) -> Duration {
    (current * 2).min(MAX_BACKOFF)
}

fn parse_notifications(body: &str) -> Vec<HostNotification> {
    serde_json::from_str::<PollResponse>(body)
        .map(|response| response.notifications)
        .unwrap_or_default()
}

/// Anything but a success is an error rather than "no notifications": a body that is not a poll
/// response parses as an empty batch, which would turn a rejected poll into a hot loop.
fn interpret(status: u16, body: &str) -> Result<Vec<HostNotification>, String> {
    match status {
        200..=299 => Ok(parse_notifications(body)),
        status => Err(format!("the host answered with {status}")),
    }
}

/// A conflict means another poll holds the queue, which it can do for at most the host's hold - so
/// retrying soon is right, where backing further off would let the host give up on this shell.
fn retry_delay(status: Option<u16>, backoff: Duration) -> Duration {
    match status {
        Some(409) => MIN_BACKOFF,
        _ => backoff,
    }
}

// Registering the bundle identifier is also the honest test for "is this a real installed bundle":
// macOS resolves it through Launch Services, which fails for a `cargo run` binary.
#[cfg(target_os = "macos")]
fn register_bundle(identifier: &str) -> bool {
    match notify_rust::set_application(identifier) {
        Ok(()) => true,
        Err(error) => {
            logging::warn(&format!(
                "[notifications] bundle identifier {identifier} could not be registered: {error}"
            ));
            false
        }
    }
}

#[cfg(not(target_os = "macos"))]
fn register_bundle(_identifier: &str) -> bool {
    true
}

/// The freedesktop icon name, which the Linux bundler installs under the main binary's name.
#[cfg(target_os = "linux")]
fn icon_name() -> Option<String> {
    std::env::current_exe()
        .ok()?
        .file_stem()
        .map(|stem| stem.to_string_lossy().into_owned())
}

fn show(notification: &HostNotification, app_name: &str, identifier: &str) -> bool {
    let _ = identifier;
    let mut builder = notify_rust::Notification::new();
    builder
        .summary(&notification.title)
        .body(&notification.message)
        .appname(app_name);

    #[cfg(target_os = "linux")]
    if let Some(icon) = icon_name() {
        builder.icon(&icon);
    }

    #[cfg(windows)]
    builder.app_id(identifier);

    match builder.show() {
        Ok(_) => true,
        Err(error) => {
            logging::warn(&format!(
                "[notifications] could not show a notification: {error}"
            ));
            false
        }
    }
}

async fn poll(
    client: &reqwest::Client,
    port: u16,
) -> Result<Vec<HostNotification>, (Option<u16>, String)> {
    let response = client
        .get(format!(
            "http://127.0.0.1:{port}/api/host/shell-notifications"
        ))
        .send()
        .await
        .map_err(|error| (None, error.to_string()))?;

    let status = response.status().as_u16();
    let body = response
        .text()
        .await
        .map_err(|error| (Some(status), error.to_string()))?;

    interpret(status, &body).map_err(|error| (Some(status), error))
}

async fn report(client: &reqwest::Client, port: u16, id: i64, shown: bool) {
    if let Err(error) = client
        .post(format!(
            "http://127.0.0.1:{port}/api/host/shell-notifications/{id}/result"
        ))
        .json(&serde_json::json!({ "shown": shown }))
        .send()
        .await
    {
        logging::warn(&format!(
            "[notifications] could not report notification {id} back to the host: {error}"
        ));
    }
}

async fn run(app: AppHandle, app_name: String, identifier: String) {
    let Ok(client) = reqwest::Client::builder().timeout(REQUEST_TIMEOUT).build() else {
        return;
    };

    let mut backoff = MIN_BACKOFF;
    while !crate::is_quitting() {
        let Some(port) = app.state::<Arc<HostState>>().ui_port() else {
            tokio::time::sleep(MIN_BACKOFF).await;
            continue;
        };

        match poll(&client, port).await {
            Ok(notifications) => {
                backoff = MIN_BACKOFF;
                for notification in notifications {
                    let payload = notification.clone();
                    let name = app_name.clone();
                    let id = identifier.clone();
                    let shown = tokio::time::timeout(
                        SHOW_TIMEOUT,
                        tauri::async_runtime::spawn_blocking(move || show(&payload, &name, &id)),
                    )
                    .await
                    .map(|joined| joined.unwrap_or(false))
                    .unwrap_or(false);
                    report(&client, port, notification.id, shown).await;
                }
            }
            Err((status, error)) => {
                logging::warn(&format!("[notifications] polling the host failed: {error}"));
                tokio::time::sleep(retry_delay(status, backoff)).await;
                backoff = next_backoff(backoff);
            }
        }
    }
}

pub fn spawn(app: &AppHandle) {
    let identifier = app.config().identifier.clone();
    let app_name = app
        .config()
        .product_name
        .clone()
        .unwrap_or_else(|| identifier.clone());

    if !should_claim(
        host::is_packaged(),
        install_state::current().is_installed(),
        register_bundle(&identifier),
    ) {
        logging::info("[notifications] leaving OS notifications to the host: this build cannot post them as Macro Deck");
        return;
    }

    let app = app.clone();
    tauri::async_runtime::spawn(async move { run(app, app_name, identifier).await });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_dev_build_leaves_notifications_to_the_host() {
        assert!(!should_claim(false, true, true));
    }

    #[test]
    fn a_packaged_build_that_is_not_installed_leaves_notifications_to_the_host() {
        assert!(!should_claim(true, false, true));
    }

    #[test]
    fn an_unregistered_bundle_leaves_notifications_to_the_host() {
        assert!(!should_claim(true, true, false));
    }

    #[test]
    fn an_installed_bundle_posts_notifications_itself() {
        assert!(should_claim(true, true, true));
    }

    #[test]
    fn a_poll_response_carries_every_notification_it_names() {
        let notifications = parse_notifications(
            r#"{"notifications":[{"id":7,"title":"A plugin wants to pair","message":"Example Plugin"},
                                 {"id":8,"title":"Second","message":"Body"}]}"#,
        );

        assert_eq!(notifications.len(), 2);
        assert_eq!(notifications[0].id, 7);
        assert_eq!(notifications[0].title, "A plugin wants to pair");
        assert_eq!(notifications[0].message, "Example Plugin");
        assert_eq!(notifications[1].id, 8);
    }

    #[test]
    fn an_empty_or_unreadable_response_carries_nothing() {
        assert!(parse_notifications(r#"{"notifications":[]}"#).is_empty());
        assert!(parse_notifications("not json").is_empty());
    }

    #[test]
    fn a_rejected_poll_is_an_error_rather_than_an_empty_batch() {
        assert!(interpret(409, "").is_err());
        assert!(interpret(404, "<html>not found</html>").is_err());
        assert!(interpret(200, r#"{"notifications":[]}"#).is_ok());
    }

    #[test]
    fn a_poll_rejected_by_another_waiter_is_retried_before_the_host_gives_up_on_us() {
        assert_eq!(retry_delay(Some(409), MAX_BACKOFF), MIN_BACKOFF);
        assert_eq!(retry_delay(Some(500), MAX_BACKOFF), MAX_BACKOFF);
        assert_eq!(retry_delay(None, MAX_BACKOFF), MAX_BACKOFF);
    }

    #[test]
    fn retries_slow_down_and_stop_at_the_ceiling() {
        assert_eq!(next_backoff(MIN_BACKOFF), Duration::from_secs(2));
        assert_eq!(next_backoff(Duration::from_secs(16)), MAX_BACKOFF);
        assert_eq!(next_backoff(MAX_BACKOFF), MAX_BACKOFF);
    }
}
