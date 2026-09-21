use std::time::{Duration, Instant};

use crate::host::HOST_RESTART_EXIT_CODE;

pub const MAX_RESTART_ATTEMPTS: u32 = 3;

pub const HEALTHY_RESET: Duration = Duration::from_secs(10 * 60);

const RESTART_BACKOFF: [Duration; MAX_RESTART_ATTEMPTS as usize] = [
    Duration::from_secs(2),
    Duration::from_secs(4),
    Duration::from_secs(8),
];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Phase {
    Idle,
    Starting,
    Running,
    Recovering,
    Stopping,
    GaveUp,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExitAction {
    Ignore,
    Relaunch,
    Recover,
    HandledByLoop,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Decision {
    Retry { attempt: u32, delay: Duration },
    GiveUp { attempts: u32 },
}

#[derive(Debug)]
pub struct Supervisor {
    phase: Phase,
    generation: u64,
    exited: Option<(u64, Option<i32>)>,
    attempts: u32,
    healthy_since: Option<Instant>,
    ever_running: bool,
}

impl Default for Supervisor {
    fn default() -> Self {
        Self::new()
    }
}

impl Supervisor {
    pub fn new() -> Self {
        Self {
            phase: Phase::Idle,
            generation: 0,
            exited: None,
            attempts: 0,
            healthy_since: None,
            ever_running: false,
        }
    }

    #[cfg(test)]
    pub fn phase(&self) -> Phase {
        self.phase
    }

    pub fn attempts(&self) -> u32 {
        self.attempts
    }

    pub fn ever_running(&self) -> bool {
        self.ever_running
    }

    pub fn is_stopping(&self) -> bool {
        self.phase == Phase::Stopping
    }

    pub fn start(&mut self) -> bool {
        if self.phase != Phase::Idle {
            return false;
        }
        self.phase = Phase::Starting;
        true
    }

    pub fn begin_attempt(&mut self) -> Option<u64> {
        if !matches!(self.phase, Phase::Starting | Phase::Recovering) {
            return None;
        }
        self.generation += 1;
        Some(self.generation)
    }

    pub fn on_exit(
        &mut self,
        generation: u64,
        code: Option<i32>,
        shutdown_expected: bool,
        now: Instant,
    ) -> ExitAction {
        if generation != self.generation {
            return ExitAction::Ignore;
        }
        self.exited = Some((generation, code));

        match self.phase {
            Phase::Idle | Phase::Stopping | Phase::GaveUp => ExitAction::Ignore,
            _ if shutdown_expected => ExitAction::Ignore,
            Phase::Starting | Phase::Recovering => ExitAction::HandledByLoop,
            Phase::Running if code == Some(HOST_RESTART_EXIT_CODE) => ExitAction::Relaunch,
            Phase::Running => {
                if self
                    .healthy_since
                    .is_some_and(|since| now.saturating_duration_since(since) >= HEALTHY_RESET)
                {
                    self.attempts = 0;
                }
                self.healthy_since = None;
                self.phase = Phase::Recovering;
                ExitAction::Recover
            }
        }
    }

    pub fn exit_of(&self, generation: u64) -> Option<Option<i32>> {
        match self.exited {
            Some((exited, code)) if exited == generation => Some(code),
            _ => None,
        }
    }

    pub fn on_ready(&mut self, generation: u64, now: Instant) -> bool {
        if generation != self.generation
            || self.exit_of(generation).is_some()
            || !matches!(self.phase, Phase::Starting | Phase::Recovering)
        {
            return false;
        }
        self.phase = Phase::Running;
        self.healthy_since = Some(now);
        self.ever_running = true;
        true
    }

    pub fn on_failure(&mut self) -> Decision {
        if self.attempts >= MAX_RESTART_ATTEMPTS {
            return Decision::GiveUp {
                attempts: self.attempts,
            };
        }
        self.attempts += 1;
        Decision::Retry {
            attempt: self.attempts,
            delay: RESTART_BACKOFF[(self.attempts - 1) as usize],
        }
    }

    pub fn begin_stop(&mut self) {
        self.phase = Phase::Stopping;
    }

    pub fn give_up(&mut self) {
        if self.phase != Phase::Stopping {
            self.phase = Phase::GaveUp;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn running(now: Instant) -> (Supervisor, u64) {
        let mut supervisor = Supervisor::new();
        assert!(supervisor.start());
        let generation = supervisor.begin_attempt().unwrap();
        assert!(supervisor.on_ready(generation, now));
        (supervisor, generation)
    }

    fn crash(supervisor: &mut Supervisor, generation: u64, now: Instant) -> ExitAction {
        supervisor.on_exit(generation, Some(1), false, now)
    }

    #[test]
    fn a_crash_while_running_starts_a_recovery() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);

        assert_eq!(crash(&mut supervisor, generation, now), ExitAction::Recover);
        assert_eq!(supervisor.phase(), Phase::Recovering);
    }

    #[test]
    fn an_unknown_exit_code_while_running_is_a_crash() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);

        assert_eq!(
            supervisor.on_exit(generation, None, false, now),
            ExitAction::Recover
        );
    }

    #[test]
    fn the_restart_exit_code_relaunches_only_once_the_host_was_running() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);
        assert_eq!(
            supervisor.on_exit(generation, Some(HOST_RESTART_EXIT_CODE), false, now),
            ExitAction::Relaunch
        );

        let mut starting = Supervisor::new();
        starting.start();
        let generation = starting.begin_attempt().unwrap();
        assert_eq!(
            starting.on_exit(generation, Some(HOST_RESTART_EXIT_CODE), false, now),
            ExitAction::HandledByLoop
        );
    }

    #[test]
    fn an_expected_shutdown_is_ignored() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);

        assert_eq!(
            supervisor.on_exit(generation, Some(0), true, now),
            ExitAction::Ignore
        );
        assert_eq!(
            supervisor.on_exit(generation, Some(HOST_RESTART_EXIT_CODE), true, now),
            ExitAction::Ignore
        );
    }

    #[test]
    fn an_exit_while_stopping_is_ignored() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);
        supervisor.begin_stop();

        assert_eq!(crash(&mut supervisor, generation, now), ExitAction::Ignore);
    }

    #[test]
    fn an_exit_after_giving_up_is_ignored() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let generation = supervisor.begin_attempt().unwrap();
        supervisor.give_up();

        assert_eq!(crash(&mut supervisor, generation, now), ExitAction::Ignore);
    }

    #[test]
    fn an_exit_of_an_earlier_attempt_is_ignored() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let first = supervisor.begin_attempt().unwrap();
        let second = supervisor.begin_attempt().unwrap();

        assert_eq!(crash(&mut supervisor, first, now), ExitAction::Ignore);
        assert!(supervisor.on_ready(second, now));
    }

    #[test]
    fn an_exit_before_readiness_belongs_to_the_attempt_loop() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let generation = supervisor.begin_attempt().unwrap();

        assert_eq!(
            crash(&mut supervisor, generation, now),
            ExitAction::HandledByLoop
        );
        assert_eq!(supervisor.exit_of(generation), Some(Some(1)));
    }

    #[test]
    fn an_exit_just_before_readiness_is_published_fails_the_attempt() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let generation = supervisor.begin_attempt().unwrap();

        assert_eq!(
            crash(&mut supervisor, generation, now),
            ExitAction::HandledByLoop
        );
        assert!(!supervisor.on_ready(generation, now));
        assert_ne!(supervisor.phase(), Phase::Running);
    }

    #[test]
    fn an_exit_just_after_readiness_is_published_is_recovered() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let generation = supervisor.begin_attempt().unwrap();

        assert!(supervisor.on_ready(generation, now));
        assert_eq!(crash(&mut supervisor, generation, now), ExitAction::Recover);
    }

    #[test]
    fn readiness_during_a_stop_does_not_count_as_running() {
        let now = Instant::now();
        let mut supervisor = Supervisor::new();
        supervisor.start();
        let generation = supervisor.begin_attempt().unwrap();
        supervisor.begin_stop();

        assert!(!supervisor.on_ready(generation, now));
        assert_eq!(supervisor.phase(), Phase::Stopping);
    }

    #[test]
    fn no_attempt_starts_once_stopping() {
        let mut supervisor = Supervisor::new();
        supervisor.start();
        supervisor.begin_stop();

        assert_eq!(supervisor.begin_attempt(), None);
    }

    #[test]
    fn a_stop_is_not_undone_by_giving_up() {
        let mut supervisor = Supervisor::new();
        supervisor.start();
        supervisor.begin_stop();
        supervisor.give_up();

        assert!(supervisor.is_stopping());
    }

    #[test]
    fn restarts_back_off_and_stop_after_three_attempts() {
        let mut supervisor = Supervisor::new();

        assert_eq!(
            supervisor.on_failure(),
            Decision::Retry {
                attempt: 1,
                delay: Duration::from_secs(2)
            }
        );
        assert_eq!(
            supervisor.on_failure(),
            Decision::Retry {
                attempt: 2,
                delay: Duration::from_secs(4)
            }
        );
        assert_eq!(
            supervisor.on_failure(),
            Decision::Retry {
                attempt: 3,
                delay: Duration::from_secs(8)
            }
        );
        assert_eq!(supervisor.on_failure(), Decision::GiveUp { attempts: 3 });
    }

    #[test]
    fn a_crash_soon_after_a_recovery_uses_the_remaining_attempts() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);
        crash(&mut supervisor, generation, now);
        supervisor.on_failure();
        let generation = supervisor.begin_attempt().unwrap();
        assert!(supervisor.on_ready(generation, now));

        let later = now + Duration::from_secs(60);
        crash(&mut supervisor, generation, later);

        assert_eq!(
            supervisor.on_failure(),
            Decision::Retry {
                attempt: 2,
                delay: Duration::from_secs(4)
            }
        );
    }

    #[test]
    fn a_long_healthy_run_resets_the_attempts() {
        let now = Instant::now();
        let (mut supervisor, generation) = running(now);
        crash(&mut supervisor, generation, now);
        supervisor.on_failure();
        supervisor.on_failure();
        let generation = supervisor.begin_attempt().unwrap();
        assert!(supervisor.on_ready(generation, now));

        crash(&mut supervisor, generation, now + HEALTHY_RESET);

        assert_eq!(
            supervisor.on_failure(),
            Decision::Retry {
                attempt: 1,
                delay: Duration::from_secs(2)
            }
        );
    }

    #[test]
    fn a_cold_start_can_only_begin_once() {
        let mut supervisor = Supervisor::new();

        assert!(supervisor.start());
        assert!(!supervisor.start());
    }
}
