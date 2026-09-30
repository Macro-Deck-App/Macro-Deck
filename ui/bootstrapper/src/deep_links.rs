// macrodeck:// links are untrusted input from any web page or app: only exact allow-listed shapes
// get through, as a typed value holding a validated id.

use std::collections::VecDeque;
use std::sync::Mutex;

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager};

use crate::logging;
use crate::window;

pub const DEEP_LINK_EVENT: &str = "deep-link";

const SCHEME: &str = "macrodeck";
const MAX_LINK_LENGTH: usize = 256;
const MAX_PACKAGE_ID_LENGTH: usize = 128;
const MAX_PENDING_LINKS: usize = 8;
const MAX_LOGGED_CHARACTERS: usize = 64;

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum DeepLink {
    Store { package_id: String },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Rejection {
    TooLong,
    Encoded,
    ForbiddenCharacter,
    Malformed,
    WrongScheme,
    Credentials,
    Port,
    Query,
    Fragment,
    UnsupportedDestination,
    InvalidPath,
    InvalidPackageId,
}

// Whether an argv entry is meant as a link, valid or not, so no other argv classifier sees it.
pub fn is_link_argument(argument: &str) -> bool {
    let bytes = argument.as_bytes();
    bytes.len() > SCHEME.len()
        && bytes[..SCHEME.len()].eq_ignore_ascii_case(SCHEME.as_bytes())
        && bytes[SCHEME.len()] == b':'
}

// The process arguments as text; bytes that are not Unicode become replacement characters, so an
// odd argument cannot crash startup the way std::env::args would.
pub fn launch_arguments() -> Vec<String> {
    std::env::args_os()
        .map(|argument| argument.to_string_lossy().into_owned())
        .collect()
}

// A launch carrying a link is that link and nothing else: arguments smuggled next to it (a pipe
// the Windows single-instance channel splits on, a quote ending "%1") are never files or flags.
pub fn is_link_invocation(args: &[String]) -> bool {
    args.iter()
        .skip(1)
        .any(|argument| is_link_argument(argument))
}

pub struct Launch {
    pub links: Vec<DeepLink>,
    pub files: Vec<String>,
    pub wants_window: bool,
}

pub fn classify(args: Vec<String>) -> Launch {
    if is_link_invocation(&args) {
        let links = from_args(args.into_iter());
        // Junk links must not pop the window up: a page can fire them as often as it likes.
        let wants_window = !links.is_empty();
        return Launch {
            links,
            files: Vec::new(),
            wants_window,
        };
    }

    Launch {
        links: Vec::new(),
        files: crate::opened_files::paths_from_args(args.into_iter()),
        wants_window: true,
    }
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
pub fn is_link_url(url: &url::Url) -> bool {
    url.scheme() == SCHEME
}

pub fn parse(raw: &str) -> Result<DeepLink, Rejection> {
    if raw.len() > MAX_LINK_LENGTH {
        return Err(Rejection::TooLong);
    }
    // Refused rather than decoded: no legitimate id needs it, and an encoded slash, dot or NUL is
    // what a smuggling attempt looks like.
    if raw.contains('%') {
        return Err(Rejection::Encoded);
    }
    if !raw.bytes().all(|byte| byte.is_ascii_graphic()) || raw.contains('\\') {
        return Err(Rejection::ForbiddenCharacter);
    }

    let url = url::Url::parse(raw).map_err(|_| Rejection::Malformed)?;
    if url.scheme() != SCHEME {
        return Err(Rejection::WrongScheme);
    }
    if !url.username().is_empty() || url.password().is_some() {
        return Err(Rejection::Credentials);
    }
    if url.port().is_some() {
        return Err(Rejection::Port);
    }
    if url.query().is_some() {
        return Err(Rejection::Query);
    }
    if url.fragment().is_some() {
        return Err(Rejection::Fragment);
    }
    let host = url.host_str().ok_or(Rejection::UnsupportedDestination)?;

    // Below reads the raw text, never the normalised path: normalising would forgive dot segments
    // and an empty port. macOS hands over a URL that is already normalised, which is checked the same.
    let (_, after_scheme) = raw.split_once("://").ok_or(Rejection::Malformed)?;
    let (raw_host, raw_path) = after_scheme.split_once('/').unwrap_or((after_scheme, ""));
    if !raw_host.eq_ignore_ascii_case(host) {
        return Err(Rejection::Malformed);
    }

    match host.to_ascii_lowercase().as_str() {
        "store" => parse_store(raw_path),
        _ => Err(Rejection::UnsupportedDestination),
    }
}

fn parse_store(path: &str) -> Result<DeepLink, Rejection> {
    let package_id = path.strip_suffix('/').unwrap_or(path);
    if package_id.is_empty() || package_id.contains('/') {
        return Err(Rejection::InvalidPath);
    }
    if !is_package_id(package_id) {
        return Err(Rejection::InvalidPackageId);
    }

    Ok(DeepLink::Store {
        package_id: package_id.to_string(),
    })
}

// The registry's package id pattern by hand: two or more dot separated parts, each a lowercase
// letter then lowercase letters and digits, with single hyphens between runs.
fn is_package_id(id: &str) -> bool {
    id.len() <= MAX_PACKAGE_ID_LENGTH && id.split('.').count() >= 2 && id.split('.').all(is_id_part)
}

fn is_id_part(part: &str) -> bool {
    let mut characters = part.chars();
    if !characters
        .next()
        .is_some_and(|first| first.is_ascii_lowercase())
    {
        return false;
    }

    let mut after_hyphen = false;
    for character in characters {
        match character {
            'a'..='z' | '0'..='9' => after_hyphen = false,
            '-' if !after_hyphen => after_hyphen = true,
            _ => return false,
        }
    }
    !after_hyphen
}

pub fn from_args(args: impl Iterator<Item = String>) -> Vec<DeepLink> {
    args.skip(1)
        .filter(|argument| is_link_argument(argument))
        .filter_map(|argument| accept(&argument))
        .collect()
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
pub fn from_urls(urls: &[url::Url]) -> Vec<DeepLink> {
    urls.iter()
        .filter(|url| is_link_url(url))
        .filter_map(|url| accept(url.as_str()))
        .collect()
}

fn accept(raw: &str) -> Option<DeepLink> {
    match parse(raw) {
        Ok(link) => Some(link),
        Err(reason) => {
            logging::warn(&format!(
                "[deep-link] rejected a link ({reason:?}): {}",
                printable(raw)
            ));
            None
        }
    }
}

// What reaches the log: bounded, and never a control character or a line break.
fn printable(raw: &str) -> String {
    let mut shown: String = raw
        .chars()
        .take(MAX_LOGGED_CHARACTERS)
        .map(|character| {
            if character.is_ascii_graphic() {
                character
            } else {
                '?'
            }
        })
        .collect();
    if raw.chars().nth(MAX_LOGGED_CHARACTERS).is_some() {
        shown.push_str("...");
    }
    shown
}

#[derive(Default)]
pub struct PendingDeepLinks(Mutex<VecDeque<DeepLink>>);

impl PendingDeepLinks {
    // A flood of links must not grow memory: the oldest one is the first to go.
    fn push(&self, links: Vec<DeepLink>) {
        if let Ok(mut queued) = self.0.lock() {
            for link in links {
                if queued.len() == MAX_PENDING_LINKS {
                    queued.pop_front();
                }
                queued.push_back(link);
            }
        }
    }

    // Only ever read: announcing a link must never consume it.
    fn is_waiting(&self) -> bool {
        self.0
            .lock()
            .map(|queued| !queued.is_empty())
            .unwrap_or(false)
    }

    fn take(&self) -> Vec<DeepLink> {
        self.0
            .lock()
            .map(|mut queued| queued.drain(..).collect())
            .unwrap_or_default()
    }
}

pub fn queue(app: &AppHandle, links: Vec<DeepLink>) {
    if links.is_empty() {
        return;
    }

    logging::info(&format!(
        "[deep-link] {} link(s) received from the OS",
        links.len()
    ));
    if let Some(pending) = app.try_state::<PendingDeepLinks>() {
        pending.push(links);
    }

    if window::is_main_window_loaded() {
        notify(app);
    }
}

// Tells the UI that links are waiting. Leaves the queue alone: only take_deep_links empties it,
// so a nudge nobody hears yet costs nothing.
pub fn notify(app: &AppHandle) {
    let waiting = app
        .try_state::<PendingDeepLinks>()
        .is_some_and(|pending| pending.is_waiting());
    if !waiting {
        return;
    }

    if let Err(error) = app.emit(DEEP_LINK_EVENT, ()) {
        logging::error(&format!(
            "[deep-link] could not emit deep-link event: {error}"
        ));
    }
}

#[tauri::command]
pub fn take_deep_links(app: AppHandle) -> Vec<DeepLink> {
    app.try_state::<PendingDeepLinks>()
        .map(|pending| pending.take())
        .unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn store(id: &str) -> Result<DeepLink, Rejection> {
        Ok(DeepLink::Store {
            package_id: id.to_string(),
        })
    }

    fn arguments(values: &[&str]) -> impl Iterator<Item = String> {
        values
            .iter()
            .map(|value| value.to_string())
            .collect::<Vec<_>>()
            .into_iter()
    }

    #[test]
    fn a_store_link_carries_the_package_id() {
        assert_eq!(parse("macrodeck://store/acme.obs"), store("acme.obs"));
    }

    #[test]
    fn package_ids_may_have_hyphens_digits_and_many_parts() {
        assert_eq!(
            parse("macrodeck://store/com.some-vendor.obs-studio2.v3"),
            store("com.some-vendor.obs-studio2.v3")
        );
        assert_eq!(parse("macrodeck://store/a1.b2-c3"), store("a1.b2-c3"));
    }

    #[test]
    fn one_trailing_slash_is_tolerated() {
        assert_eq!(parse("macrodeck://store/acme.obs/"), store("acme.obs"));
        assert_eq!(
            parse("macrodeck://store/acme.obs//"),
            Err(Rejection::InvalidPath)
        );
    }

    #[test]
    fn the_scheme_and_host_ignore_case() {
        assert_eq!(parse("MACRODECK://STORE/acme.obs"), store("acme.obs"));
        assert_eq!(parse("MacroDeck://Store/acme.obs"), store("acme.obs"));
    }

    #[test]
    fn other_schemes_are_rejected() {
        for link in [
            "https://store/acme.obs",
            "macrodeck2://store/acme.obs",
            "xmacrodeck://store/acme.obs",
            "file://store/acme.obs",
            "javascript:alert(1)",
            "store/acme.obs",
            "",
        ] {
            assert!(parse(link).is_err(), "{link} must be rejected");
        }
    }

    #[test]
    fn only_the_store_destination_exists() {
        for host in [
            "settings", "plugin", "plugins", "file", "run", "open", "stores", "sto",
        ] {
            assert_eq!(
                parse(&format!("macrodeck://{host}/acme.obs")),
                Err(Rejection::UnsupportedDestination),
                "{host} must not be a destination"
            );
        }
        assert_eq!(
            parse("macrodeck://settings"),
            Err(Rejection::UnsupportedDestination)
        );
        assert!(parse("macrodeck:store/acme.obs").is_err());
        assert!(parse("macrodeck:///store/acme.obs").is_err());
    }

    #[test]
    fn a_link_without_an_id_is_rejected() {
        for link in [
            "macrodeck://store",
            "macrodeck://store/",
            "macrodeck://store//",
        ] {
            assert_eq!(parse(link), Err(Rejection::InvalidPath), "{link}");
        }
    }

    #[test]
    fn extra_path_segments_are_rejected() {
        for link in [
            "macrodeck://store/acme.obs/extra",
            "macrodeck://store/acme.obs/extra/",
            "macrodeck://store/acme.obs/..",
            "macrodeck://store/acme.obs/../acme.other",
            "macrodeck://store/./acme.obs",
            "macrodeck://store/../acme.obs",
            "macrodeck://store/..",
            "macrodeck://store//acme.obs",
        ] {
            assert!(parse(link).is_err(), "{link} must be rejected");
        }
    }

    #[test]
    fn percent_encoded_input_is_rejected_and_never_decoded() {
        for link in [
            "macrodeck://store/%2e%2e",
            "macrodeck://store/acme%2Eobs",
            "macrodeck://store/acme.obs%2Fextra",
            "macrodeck://store/acme.obs%00",
            "macrodeck://store/acme.obs%0a",
            "macrodeck://st%6Fre/acme.obs",
            "macrodeck%3A//store/acme.obs",
            "macrodeck://store/acme.obs%",
        ] {
            assert_eq!(parse(link), Err(Rejection::Encoded), "{link}");
        }
    }

    #[test]
    fn backslashes_whitespace_and_control_characters_are_rejected() {
        for link in [
            "macrodeck://store/acme\\obs",
            "macrodeck:\\\\store\\acme.obs",
            "macrodeck://store/acme.obs ",
            " macrodeck://store/acme.obs",
            "macrodeck://store/acme .obs",
            "macrodeck://store/acme.obs\n",
            "macrodeck://store/acme.obs\r\n",
            "macrodeck://store/acme.obs\t",
            "macrodeck://store/acme.obs\0",
            "macrodeck://store/acme.obs\u{7f}",
            "macrodeck://store/acme.obs\u{1b}[2J",
        ] {
            assert_eq!(parse(link), Err(Rejection::ForbiddenCharacter), "{link:?}");
        }
    }

    #[test]
    fn a_second_url_appended_to_the_link_is_rejected() {
        for link in [
            "macrodeck://store/acme.obs https://evil.example",
            "macrodeck://store/acme.obs?x=https://evil.example",
            "macrodeck://store/acme.obs#https://evil.example",
            "macrodeck://store/acme.obs/https://evil.example",
            "macrodeck://store/acme.obs,https://evil.example",
            "macrodeck://store/acme.obs;https://evil.example",
        ] {
            assert!(parse(link).is_err(), "{link} must be rejected");
        }
    }

    #[test]
    fn a_query_or_a_fragment_is_rejected() {
        assert_eq!(parse("macrodeck://store/acme.obs?"), Err(Rejection::Query));
        assert_eq!(
            parse("macrodeck://store/acme.obs?a=b"),
            Err(Rejection::Query)
        );
        assert_eq!(
            parse("macrodeck://store/acme.obs#"),
            Err(Rejection::Fragment)
        );
        assert_eq!(
            parse("macrodeck://store/acme.obs#top"),
            Err(Rejection::Fragment)
        );
    }

    #[test]
    fn credentials_and_ports_are_rejected() {
        for link in [
            "macrodeck://user@store/acme.obs",
            "macrodeck://user:secret@store/acme.obs",
            "macrodeck://@store/acme.obs",
            "macrodeck://store@evil.example/acme.obs",
        ] {
            assert!(parse(link).is_err(), "{link} must be rejected");
        }
        assert_eq!(
            parse("macrodeck://store:8080/acme.obs"),
            Err(Rejection::Port)
        );
        assert!(parse("macrodeck://store:/acme.obs").is_err());
    }

    #[test]
    fn shell_shaped_tails_are_rejected() {
        for tail in [
            "acme.obs;rm",
            "acme.obs&calc",
            "acme.obs|cat",
            "acme.obs`id`",
            "acme.obs$(id)",
            "$(id).obs",
            "acme.obs>out",
            "acme.obs'",
            "acme.obs\"",
            "acme.obs*",
            "acme.obs=1",
            "acme.obs:1",
            "acme.obs@evil",
        ] {
            assert!(
                parse(&format!("macrodeck://store/{tail}")).is_err(),
                "{tail} must be rejected"
            );
        }
    }

    #[test]
    fn ids_outside_the_registry_pattern_are_rejected() {
        for id in [
            "Acme.obs",
            "acme.Obs",
            "ACME.OBS",
            "acme",
            "1acme.obs",
            "acme.1obs",
            ".acme.obs",
            "acme..obs",
            "acme.obs.",
            "-acme.obs",
            "acme.-obs",
            "acme-.obs",
            "acme.obs-",
            "acme--x.obs",
            "acme_x.obs",
            "acme.o\u{00e9}s",
            "acme.\u{043e}bs",
            "..",
            ".",
        ] {
            assert_eq!(
                parse(&format!("macrodeck://store/{id}")),
                Err(if id.is_ascii() {
                    Rejection::InvalidPackageId
                } else {
                    Rejection::ForbiddenCharacter
                }),
                "{id} must be rejected"
            );
        }
    }

    #[test]
    fn a_package_id_is_capped_at_128_characters() {
        let part = "a".repeat(61);
        let longest = format!("{part}.{}", "b".repeat(128 - 62));
        assert_eq!(longest.len(), 128);
        assert_eq!(
            parse(&format!("macrodeck://store/{longest}")),
            store(&longest)
        );

        let too_long = format!("{longest}b");
        assert_eq!(
            parse(&format!("macrodeck://store/{too_long}")),
            Err(Rejection::InvalidPackageId)
        );
    }

    #[test]
    fn a_link_is_capped_at_256_characters() {
        let padded = format!("macrodeck://store/{}.b", "a".repeat(250));
        assert!(padded.len() > 256);
        assert_eq!(parse(&padded), Err(Rejection::TooLong));
        assert_eq!(parse(&"a".repeat(10_000)), Err(Rejection::TooLong));
    }

    #[test]
    fn from_args_takes_a_lone_link() {
        let links = from_args(arguments(&["MacroDeck", "macrodeck://store/acme.obs"]));

        assert_eq!(
            links,
            vec![DeepLink::Store {
                package_id: "acme.obs".to_string()
            }]
        );
    }

    #[test]
    fn from_args_never_takes_the_first_argument() {
        assert!(from_args(arguments(&["macrodeck://store/acme.obs"])).is_empty());
    }

    #[test]
    fn from_args_ignores_flags_files_and_other_arguments() {
        let links = from_args(arguments(&[
            "MacroDeck",
            "--autostart",
            "--minimized",
            "/tmp/Default.macroDeckProfile",
            "https://macro-deck.app",
            "macrodeck-not-a-link",
            "macrodeck://store/acme.obs",
            "--url=macrodeck://store/acme.other",
        ]));

        assert_eq!(
            links,
            vec![DeepLink::Store {
                package_id: "acme.obs".to_string()
            }]
        );
    }

    #[test]
    fn from_args_keeps_every_valid_link_in_order() {
        let links = from_args(arguments(&[
            "MacroDeck",
            "macrodeck://store/acme.one",
            "MACRODECK://STORE/acme.two",
        ]));

        assert_eq!(
            links,
            vec![
                DeepLink::Store {
                    package_id: "acme.one".to_string()
                },
                DeepLink::Store {
                    package_id: "acme.two".to_string()
                },
            ]
        );
    }

    #[test]
    fn from_args_drops_rejected_links() {
        let links = from_args(arguments(&[
            "MacroDeck",
            "macrodeck://settings",
            "macrodeck://store/acme.obs/extra",
            "macrodeck://store/%2e%2e",
            "macrodeck://store/acme.obs?x=1",
            "macrodeck:",
        ]));

        assert!(links.is_empty());
    }

    #[test]
    fn from_urls_takes_links_and_ignores_files_and_other_schemes() {
        let urls = [
            url::Url::parse("file:///tmp/Default.macroDeckProfile").unwrap(),
            url::Url::parse("https://store/acme.obs").unwrap(),
            url::Url::parse("MacroDeck://Store/acme.obs").unwrap(),
            url::Url::parse("macrodeck://settings").unwrap(),
            url::Url::parse("macrodeck://store/acme.obs/extra").unwrap(),
        ];

        assert_eq!(
            from_urls(&urls),
            vec![DeepLink::Store {
                package_id: "acme.obs".to_string()
            }]
        );
    }

    #[test]
    fn link_arguments_are_recognised_by_scheme_alone() {
        assert!(is_link_argument("macrodeck://store/acme.obs"));
        assert!(is_link_argument("MacroDeck:whatever"));
        assert!(is_link_argument("MACRODECK:"));
        assert!(!is_link_argument("macrodeck"));
        assert!(!is_link_argument("macrodeckx://store/acme.obs"));
        assert!(!is_link_argument("/tmp/macrodeck:x"));
        assert!(!is_link_argument("\u{00e9}macrodeck:"));
        assert!(!is_link_argument(""));
    }

    #[test]
    fn the_log_form_of_a_link_is_bounded_and_printable() {
        let hostile = format!(
            "macrodeck://store/\n\r\u{1b}[31m\u{00e9}{}",
            "x".repeat(500)
        );

        let shown = printable(&hostile);

        assert!(shown.chars().all(|c| c.is_ascii_graphic() || c == '.'));
        assert!(shown.chars().count() <= MAX_LOGGED_CHARACTERS + 3);
        assert!(shown.ends_with("..."));
        assert_eq!(printable("macrodeck://store/a.b"), "macrodeck://store/a.b");
    }

    #[test]
    fn a_link_is_serialised_the_way_the_ui_reads_it() {
        let link = DeepLink::Store {
            package_id: "acme.obs".to_string(),
        };

        assert_eq!(
            serde_json::to_string(&link).unwrap(),
            r#"{"kind":"store","packageId":"acme.obs"}"#
        );
    }

    fn queue_of(ids: &[&str]) -> PendingDeepLinks {
        let pending = PendingDeepLinks::default();
        pending.push(ids.iter().map(|id| link(id)).collect());
        pending
    }

    fn link(id: &str) -> DeepLink {
        DeepLink::Store {
            package_id: id.to_string(),
        }
    }

    #[test]
    fn announcing_a_link_does_not_consume_it() {
        let pending = queue_of(&["acme.obs"]);

        assert!(pending.is_waiting());
        assert!(pending.is_waiting());

        assert_eq!(pending.take(), vec![link("acme.obs")]);
    }

    #[test]
    fn a_link_is_handed_over_exactly_once() {
        let pending = queue_of(&["acme.obs"]);

        assert_eq!(pending.take().len(), 1);
        assert!(pending.take().is_empty());
        assert!(!pending.is_waiting());
    }

    #[test]
    fn links_arrive_in_the_order_they_were_opened() {
        let pending = queue_of(&["acme.one", "acme.two"]);
        pending.push(vec![link("acme.three")]);

        assert_eq!(
            pending.take(),
            vec![link("acme.one"), link("acme.two"), link("acme.three")]
        );
    }

    #[test]
    fn an_empty_queue_is_never_announced() {
        assert!(!PendingDeepLinks::default().is_waiting());
    }

    #[test]
    fn a_flood_of_links_keeps_only_the_newest() {
        let pending = PendingDeepLinks::default();
        let flood: Vec<DeepLink> = (0..MAX_PENDING_LINKS + 5)
            .map(|index| link(&format!("acme.n{index}")))
            .collect();

        pending.push(flood);
        let taken = pending.take();

        assert_eq!(taken.len(), MAX_PENDING_LINKS);
        assert_eq!(taken.first(), Some(&link("acme.n5")));
        assert_eq!(
            taken.last(),
            Some(&link(&format!("acme.n{}", MAX_PENDING_LINKS + 4)))
        );
    }

    #[test]
    fn the_page_load_handler_announces_without_consuming_the_queue() {
        let main_source = include_str!("main.rs");
        let handler = main_source
            .split("PageLoadEvent::Finished")
            .nth(1)
            .and_then(|rest| rest.split(".setup(").next())
            .expect("main.rs must handle PageLoadEvent::Finished");

        assert!(handler.contains("deep_links::notify"));
        assert!(!handler.contains("deep_links::take_deep_links"));
    }

    fn owned(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| value.to_string()).collect()
    }

    #[test]
    fn a_launch_carrying_a_link_reads_nothing_but_the_link() {
        let launch = classify(owned(&[
            "MacroDeck",
            "macrodeck://store/acme.obs",
            "\\\\attacker.example\\share\\evil.macroDeckPlugin",
            "/tmp/evil.macroDeckProfile",
            "--autostart",
            "--minimized",
        ]));

        assert_eq!(
            launch.links,
            vec![DeepLink::Store {
                package_id: "acme.obs".to_string()
            }]
        );
        assert!(launch.files.is_empty());
        assert!(launch.wants_window);
    }

    #[test]
    fn a_rejected_link_opens_nothing_and_does_not_raise_the_window() {
        let launch = classify(owned(&[
            "MacroDeck",
            "macrodeck://store/../../etc",
            "/tmp/evil.macroDeckPlugin",
        ]));

        assert!(launch.links.is_empty());
        assert!(launch.files.is_empty());
        assert!(!launch.wants_window);
    }

    #[test]
    fn a_launch_without_a_link_still_opens_its_files_and_shows_the_window() {
        let launch = classify(owned(&["MacroDeck", "/tmp/Default.macroDeckProfile"]));

        assert_eq!(
            launch.files,
            vec!["/tmp/Default.macroDeckProfile".to_string()]
        );
        assert!(launch.links.is_empty());
        assert!(launch.wants_window);
    }

    #[test]
    fn a_plain_launch_shows_the_window() {
        assert!(classify(owned(&["MacroDeck"])).wants_window);
    }

    #[test]
    fn the_id_grammar_matches_the_manifest_reference() {
        for valid in ["com.example.hue-lights", "com.example", "my-plugin2.a"] {
            assert!(is_package_id(valid), "{valid}");
        }
        for invalid in [
            "com.Example.hue_lights",
            "myplugin",
            "-my.a",
            "my--plugin.a",
            "my-.a",
            "1a.b",
            "a..b",
            "a.b.",
            ".a.b",
        ] {
            assert!(!is_package_id(invalid), "{invalid}");
        }
        assert!(is_package_id(&format!("a.{}", "b".repeat(126))));
        assert!(!is_package_id(&format!("a.{}", "b".repeat(127))));
    }
}
