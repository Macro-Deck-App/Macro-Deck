use std::collections::HashMap;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serde::Deserialize;

const REPEAT_AFTER: Duration = Duration::from_secs(30);
const MAX_REASON_CHARS: usize = 300;
const STEPS: [&str; 6] = [
    "key-ring", "auth", "host-url", "ticket", "socket", "register",
];

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UiConnectionReport {
    pub step: String,
    pub reason: String,
    pub attempt: u32,
    pub recovered: bool,
}

pub enum LogLine {
    Warn(String),
    Info(String),
}

// The page decides when to report, but any script in the window can call the command, so the
// bootstrapper keeps its own limit rather than trusting the caller's cadence.
#[derive(Default)]
pub struct UiConnectionLog {
    last_logged: HashMap<String, Instant>,
}

static LOG: Mutex<Option<UiConnectionLog>> = Mutex::new(None);

pub fn record(report: &UiConnectionReport) {
    let line = LOG.lock().ok().and_then(|mut log| {
        log.get_or_insert_with(UiConnectionLog::default)
            .admit(report, Instant::now())
    });
    match line {
        Some(LogLine::Warn(text)) => crate::logging::warn(&text),
        Some(LogLine::Info(text)) => crate::logging::info(&text),
        None => {}
    }
}

impl UiConnectionLog {
    pub fn admit(&mut self, report: &UiConnectionReport, now: Instant) -> Option<LogLine> {
        let step = step_name(&report.step);
        let key = if report.recovered {
            "recovered".to_string()
        } else {
            step.clone()
        };
        if let Some(last) = self.last_logged.get(&key) {
            if now.duration_since(*last) < REPEAT_AFTER {
                return None;
            }
        }
        self.last_logged.insert(key, now);

        if report.recovered {
            return Some(LogLine::Info(format!(
                "[window] the UI reached the host after {} failed attempts",
                report.attempt
            )));
        }
        Some(LogLine::Warn(format!(
            "[window] the UI cannot reach the host: {step}: {} (attempt {})",
            reason_text(&report.reason),
            report.attempt
        )))
    }
}

fn step_name(step: &str) -> String {
    if STEPS.contains(&step) {
        step.to_string()
    } else {
        "unknown".to_string()
    }
}

fn reason_text(reason: &str) -> String {
    let single_line: String = reason
        .chars()
        .map(|c| if c.is_control() { ' ' } else { c })
        .take(MAX_REASON_CHARS)
        .collect();
    crate::redact::redact(&single_line)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn failure(step: &str, reason: &str, attempt: u32) -> UiConnectionReport {
        UiConnectionReport {
            step: step.to_string(),
            reason: reason.to_string(),
            attempt,
            recovered: false,
        }
    }

    fn text(line: Option<LogLine>) -> Option<String> {
        line.map(|line| match line {
            LogLine::Warn(text) | LogLine::Info(text) => text,
        })
    }

    #[test]
    fn a_failure_is_logged_with_its_step_reason_and_attempt() {
        let mut log = UiConnectionLog::default();
        let line = text(log.admit(
            &failure("ticket", "UI WebSocket ticket request failed: 500", 1),
            Instant::now(),
        ));
        assert_eq!(
            line.as_deref(),
            Some("[window] the UI cannot reach the host: ticket: UI WebSocket ticket request failed: 500 (attempt 1)")
        );
    }

    #[test]
    fn repeats_of_the_same_step_are_dropped_until_the_interval_passes() {
        let mut log = UiConnectionLog::default();
        let start = Instant::now();
        assert!(log.admit(&failure("socket", "a", 1), start).is_some());
        assert!(log
            .admit(&failure("socket", "b", 2), start + Duration::from_secs(2))
            .is_none());
        assert!(log
            .admit(&failure("socket", "c", 16), start + REPEAT_AFTER)
            .is_some());
    }

    #[test]
    fn a_different_step_and_the_recovery_are_logged_right_away() {
        let mut log = UiConnectionLog::default();
        let start = Instant::now();
        assert!(log.admit(&failure("ticket", "a", 1), start).is_some());
        assert!(log
            .admit(&failure("socket", "b", 2), start + Duration::from_secs(1))
            .is_some());
        let recovered = UiConnectionReport {
            step: "socket".to_string(),
            reason: String::new(),
            attempt: 2,
            recovered: true,
        };
        assert!(matches!(
            log.admit(&recovered, start + Duration::from_secs(2)),
            Some(LogLine::Info(_))
        ));
    }

    #[test]
    fn invented_step_names_share_one_limit() {
        let mut log = UiConnectionLog::default();
        let start = Instant::now();
        assert!(log.admit(&failure("a", "x", 1), start).is_some());
        assert!(log.admit(&failure("b", "x", 2), start).is_none());
        assert!(log.admit(&failure("aa", "x", 3), start).is_none());
    }

    #[test]
    fn caller_text_cannot_forge_lines_or_grow_without_bound() {
        let mut log = UiConnectionLog::default();
        let reason = format!("first\n[12:00:00 ERR] forged{}", "x".repeat(1000));
        let line = text(log.admit(&failure("Ticket\n", &reason, 1), Instant::now())).unwrap();
        assert!(!line.contains('\n'));
        assert!(line.contains(": unknown: "));
        assert!(line.chars().count() < MAX_REASON_CHARS + 100);
    }
}
