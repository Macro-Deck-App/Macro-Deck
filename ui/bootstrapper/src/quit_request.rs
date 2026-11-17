const ARGUMENT: &str = "--quit";

// Only a launch that is the flag and nothing else: a link launch can smuggle arguments (the
// Windows single-instance channel splits on a pipe), and a page must not be able to quit the app.
pub fn requested(args: &[String]) -> bool {
    matches!(args, [_, argument] if argument == ARGUMENT)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| value.to_string()).collect()
    }

    #[test]
    fn the_flag_alone_asks_to_quit() {
        assert!(requested(&args(&["macro-deck", "--quit"])));
    }

    #[test]
    fn a_plain_launch_does_not_quit() {
        assert!(!requested(&args(&["macro-deck"])));
    }

    #[test]
    fn the_flag_next_to_other_arguments_is_not_a_quit() {
        assert!(!requested(&args(&[
            "macro-deck",
            "macrodeck://store/plugin/x",
            "--quit"
        ])));
        assert!(!requested(&args(&[
            "macro-deck",
            "--quit",
            "/home/user/backup.mdbackup"
        ])));
    }

    #[test]
    fn near_misses_of_the_flag_are_not_a_quit() {
        assert!(!requested(&args(&["macro-deck", "--quit=now"])));
        assert!(!requested(&args(&["macro-deck", "--QUIT"])));
    }
}
