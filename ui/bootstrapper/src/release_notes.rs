use std::time::Duration;

use serde::Deserialize;

use crate::logging;

const RELEASES_API: &str = "https://api.github.com/repos/Macro-Deck-App/Macro-Deck/releases/tags/v";
const RELEASE_PAGE: &str = "https://github.com/Macro-Deck-App/Macro-Deck/releases/tag/v";
const FETCH_TIMEOUT: Duration = Duration::from_secs(5);

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) enum ReleaseNotes {
    Published(String),
    Empty,
    Unavailable,
}

pub(crate) fn release_page_url(version: &str) -> String {
    format!("{RELEASE_PAGE}{version}")
}

#[derive(Deserialize)]
struct Release {
    #[serde(default)]
    draft: bool,
    body: Option<String>,
}

fn without_html_comments(text: &str) -> String {
    let mut result = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(start) = rest.find("<!--") {
        result.push_str(&rest[..start]);
        match rest[start + 4..].find("-->") {
            Some(end) => rest = &rest[start + 4 + end + 3..],
            None => return result,
        }
    }
    result.push_str(rest);
    result
}

pub(crate) fn parse(status: u16, body: &str) -> ReleaseNotes {
    if status != 200 {
        return ReleaseNotes::Unavailable;
    }
    match serde_json::from_str::<Release>(body) {
        Ok(release) if release.draft => ReleaseNotes::Unavailable,
        Ok(release) => match release.body {
            Some(notes) if !without_html_comments(&notes).trim().is_empty() => {
                ReleaseNotes::Published(notes)
            }
            _ => ReleaseNotes::Empty,
        },
        Err(_) => ReleaseNotes::Unavailable,
    }
}

// Unauthenticated on purpose: no token may ship in the app, and the public API allows 60 requests
// per hour and address, which is why automatic checks reuse the notes of a version already fetched.
pub(crate) async fn fetch(version: &str, app_version: &str) -> ReleaseNotes {
    let Ok(client) = reqwest::Client::builder()
        .timeout(FETCH_TIMEOUT)
        .user_agent(format!("MacroDeck/{app_version}"))
        .build()
    else {
        return ReleaseNotes::Unavailable;
    };
    let response = match client
        .get(format!("{RELEASES_API}{version}"))
        .header("Accept", "application/vnd.github+json")
        .send()
        .await
    {
        Ok(response) => response,
        Err(error) => {
            logging::warn(&format!(
                "[release-notes] could not fetch the notes of {version}: {error}"
            ));
            return ReleaseNotes::Unavailable;
        }
    };
    let status = response.status().as_u16();
    let body = response.text().await.unwrap_or_default();
    let notes = parse(status, &body);
    if notes == ReleaseNotes::Unavailable {
        logging::warn(&format!(
            "[release-notes] GitHub answered {status} for the notes of {version}"
        ));
    }
    notes
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_release_with_a_body_has_published_notes() {
        let json = r###"{"draft":false,"body":"## Fixes\r\n* One"}"###;
        assert_eq!(
            parse(200, json),
            ReleaseNotes::Published("## Fixes\r\n* One".to_string())
        );
    }

    #[test]
    fn a_release_with_no_body_or_a_blank_one_is_empty() {
        assert_eq!(
            parse(200, r#"{"draft":false,"body":null}"#),
            ReleaseNotes::Empty
        );
        assert_eq!(
            parse(200, r#"{"draft":false,"body":" \r\n"}"#),
            ReleaseNotes::Empty
        );
        assert_eq!(parse(200, r#"{"draft":false}"#), ReleaseNotes::Empty);
    }

    #[test]
    fn a_body_that_is_only_the_generator_comment_is_empty() {
        let json = r#"{"draft":false,"body":"<!-- Release notes generated using configuration in .github/release.yml at abc -->\r\n\r\n"}"#;
        assert_eq!(parse(200, json), ReleaseNotes::Empty);
        let multi_line = r#"{"draft":false,"body":"<!-- first\nsecond -->\n"}"#;
        assert_eq!(parse(200, multi_line), ReleaseNotes::Empty);
    }

    #[test]
    fn notes_after_the_generator_comment_are_published_unchanged() {
        let json = r#"{"draft":false,"body":"<!-- generated -->\n## Fixes\n* One"}"#;
        assert_eq!(
            parse(200, json),
            ReleaseNotes::Published("<!-- generated -->\n## Fixes\n* One".to_string())
        );
    }

    #[test]
    fn a_missing_release_a_rate_limit_or_a_server_error_is_unavailable() {
        assert_eq!(
            parse(404, r#"{"message":"Not Found"}"#),
            ReleaseNotes::Unavailable
        );
        assert_eq!(
            parse(403, r#"{"message":"API rate limit exceeded"}"#),
            ReleaseNotes::Unavailable
        );
        assert_eq!(parse(502, ""), ReleaseNotes::Unavailable);
    }

    #[test]
    fn a_draft_or_an_unreadable_answer_is_unavailable() {
        assert_eq!(
            parse(200, r#"{"draft":true,"body":"secret"}"#),
            ReleaseNotes::Unavailable
        );
        assert_eq!(
            parse(200, "<html>proxy login</html>"),
            ReleaseNotes::Unavailable
        );
    }

    #[test]
    fn the_release_page_is_the_tag_of_the_version() {
        assert_eq!(
            release_page_url("3.0.0-beta.12"),
            "https://github.com/Macro-Deck-App/Macro-Deck/releases/tag/v3.0.0-beta.12"
        );
    }
}
