//! The bootstrapper's slice of the Macro Deck localization catalog.
//!
//! The host owns the active language, but the bootstrapper needs text before the host can answer - the
//! tray menu is built during startup, and the error dialogs here are the ones shown when the host never
//! starts at all. So the `Bootstrapper.*` keys are compiled in from the same `.resx` resources the host
//! and the Angular clients are generated from ([`generated`]), and the active culture is taken from the
//! last one the host reported, falling back to the operating system's and then to English.
//!
//! Everything else - the whole catalog, plugin scopes, live updates - stays the host's job. This module
//! deliberately knows only what it has to render on its own.

mod generated;

use std::sync::RwLock;

pub use generated::keys;

/// The culture text is rendered in, until the host says otherwise.
static CULTURE: RwLock<Option<String>> = RwLock::new(None);

/// Adopts the culture the host reports, so the next menu rebuild renders in it.
pub fn set_culture(culture: &str) {
    if let Ok(mut current) = CULTURE.write() {
        *current = Some(culture.to_owned());
    }
}

/// The active culture: the host's if it has reported one, otherwise the operating system's.
pub fn culture() -> String {
    if let Ok(current) = CULTURE.read() {
        if let Some(culture) = current.as_ref() {
            return culture.clone();
        }
    }

    system_culture().unwrap_or_else(|| generated::DEFAULT_CULTURE.to_owned())
}

/// Renders `key` in the active language.
pub fn t(key: &str) -> String {
    t_args(key, &[])
}

/// Renders `key`, substituting `{name}` placeholders from `args`.
pub fn t_args(key: &str, args: &[(&str, &str)]) -> String {
    match template(key, None) {
        Some(template) => format_template(template, args),
        // The same conspicuous shape the host and the clients render, so a missing key reads as a missing
        // key rather than as a blank menu entry.
        None => format!("[[{}:{}]]", generated::SCOPE, key),
    }
}

/// Renders the plural form `count` selects, substituting `{count}` along with `args`.
pub fn t_plural(key: &str, count: i64, args: &[(&str, &str)]) -> String {
    let count_text = count.to_string();
    let mut all: Vec<(&str, &str)> = Vec::with_capacity(args.len() + 1);
    all.push(("count", count_text.as_str()));
    all.extend_from_slice(args);

    match template(key, Some(plural_form(count))) {
        Some(template) => format_template(template, &all),
        None => format!("[[{}:{}]]", generated::SCOPE, key),
    }
}

/// The form a count selects. Mirrors the host's `LocalizationPluralForms` exactly: `One` and `Other`
/// only, chosen by `count == 1`, for every language. See ADR 0057 for why this is not CLDR.
fn plural_form(count: i64) -> &'static str {
    if count == 1 {
        "One"
    } else {
        "Other"
    }
}

/// Walks the culture chain for `key`: the active culture, its neutral culture, then the default. A
/// plural family is stored as its forms, so a form falls back to `Other` before moving on.
fn template(key: &str, form: Option<&str>) -> Option<&'static str> {
    let active = culture();
    let neutral = neutral_of(&active);

    let mut candidates: Vec<&str> = vec![active.as_str()];
    if let Some(neutral) = neutral.as_deref() {
        candidates.push(neutral);
    }
    candidates.push(generated::DEFAULT_CULTURE);

    for candidate in candidates {
        let Some(templates) = templates_of(candidate) else {
            continue;
        };

        match form {
            None => {
                if let Some(found) = lookup(templates, key) {
                    return Some(found);
                }
            }
            Some(form) => {
                if let Some(found) = lookup(templates, &format!("{key}.{form}"))
                    .or_else(|| lookup(templates, &format!("{key}.Other")))
                {
                    return Some(found);
                }
            }
        }
    }

    None
}

fn templates_of(culture: &str) -> Option<&'static [(&'static str, &'static str)]> {
    generated::CATALOG
        .iter()
        .find(|(name, _)| name.eq_ignore_ascii_case(culture))
        .map(|(_, templates)| *templates)
}

fn lookup(templates: &'static [(&'static str, &'static str)], key: &str) -> Option<&'static str> {
    templates
        .binary_search_by(|(candidate, _)| (*candidate).cmp(key))
        .ok()
        .map(|index| templates[index].1)
}

fn neutral_of(culture: &str) -> Option<String> {
    culture
        .split_once('-')
        .map(|(neutral, _)| neutral.to_owned())
}

/// Substitutes `{name}` placeholders, matching the host's formatter: `{{`/`}}` escape to one brace, and
/// an argument with no value is left as its literal `{name}` rather than silently disappearing.
fn format_template(template: &str, args: &[(&str, &str)]) -> String {
    let mut out = String::with_capacity(template.len());
    let bytes = template.as_bytes();
    let mut index = 0;

    while index < bytes.len() {
        let rest = &template[index..];

        if rest.starts_with("{{") {
            out.push('{');
            index += 2;
            continue;
        }

        if rest.starts_with("}}") {
            out.push('}');
            index += 2;
            continue;
        }

        if bytes[index] == b'{' {
            if let Some(end) = rest.find('}') {
                let name = &rest[1..end];
                if is_placeholder_name(name) {
                    match args.iter().find(|(candidate, _)| *candidate == name) {
                        Some((_, value)) => out.push_str(value),
                        None => out.push_str(&rest[..=end]),
                    }
                    index += end + 1;
                    continue;
                }
            }
        }

        let character = template[index..]
            .chars()
            .next()
            .expect("index is a char boundary");
        out.push(character);
        index += character.len_utf8();
    }

    out
}

fn is_placeholder_name(name: &str) -> bool {
    let mut characters = name.chars();

    match characters.next() {
        Some(first) if first == '_' || first.is_alphabetic() => {}
        _ => return false,
    }

    characters.all(|character| character == '_' || character.is_alphanumeric())
}

/// The operating system's language, normalised to a bare culture name (`de_DE.UTF-8` -> `de-DE`).
fn system_culture() -> Option<String> {
    let raw = std::env::var("LC_ALL")
        .or_else(|_| std::env::var("LC_MESSAGES"))
        .or_else(|_| std::env::var("LANG"))
        .ok()?;

    let trimmed = raw.split('.').next()?.trim();
    if trimmed.is_empty()
        || trimmed.eq_ignore_ascii_case("C")
        || trimmed.eq_ignore_ascii_case("POSIX")
    {
        return None;
    }

    Some(trimmed.replace('_', "-"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn placeholders_are_substituted_by_name() {
        assert_eq!(
            format_template("port {port} is busy", &[("port", "8194")]),
            "port 8194 is busy"
        );
    }

    #[test]
    fn an_argument_with_no_value_is_left_visible() {
        assert_eq!(format_template("port {port}", &[]), "port {port}");
    }

    #[test]
    fn doubled_braces_escape_to_one() {
        assert_eq!(format_template("{{port}}", &[("port", "1")]), "{port}");
    }

    #[test]
    fn a_count_of_one_selects_the_singular() {
        assert_eq!(plural_form(1), "One");
        assert_eq!(plural_form(0), "Other");
        assert_eq!(plural_form(2), "Other");
    }

    #[test]
    fn a_regional_culture_falls_back_to_its_neutral() {
        assert_eq!(neutral_of("de-DE").as_deref(), Some("de"));
        assert_eq!(neutral_of("de"), None);
    }

    #[test]
    fn a_posix_locale_is_not_a_culture() {
        assert!(!is_placeholder_name(""));
        assert!(is_placeholder_name("userName"));
        assert!(!is_placeholder_name("1st"));
    }

    /// The catalog is looked up with a binary search, which is only correct if the emitter sorted it.
    #[test]
    fn every_culture_is_sorted_by_key() {
        for (culture, templates) in generated::CATALOG {
            let mut sorted: Vec<&str> = templates.iter().map(|(key, _)| *key).collect();
            let original = sorted.clone();
            sorted.sort_unstable();
            assert_eq!(original, sorted, "{culture} is not sorted by key");
        }
    }
}
