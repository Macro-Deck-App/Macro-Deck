Closes #

## Summary

<!-- Briefly describe what changed and why. -->

## Testing

<!-- Describe the tests and manual checks that were relevant for this change. -->

## Plugin SDK / API compatibility

<!--
Only relevant when this PR touches sdk/, protocol/, the /api/plugins endpoints, the manifest or
package format, analyzer diagnostic ids or conformance check ids. Remove this section otherwise.

State either "no public contract changed" or list what was added, and for anything removed or
changed, link the decision that approved the break.
-->

- [ ] No public, non-obsolete SDK or plugin protocol contract was removed or changed
- [ ] Contracts were only extended additively (new members with defaults, new optional fields, new message types)
- [ ] Anything being retired carries `[Obsolete]` *and* `[MacroDeckDeprecated]`, is in the deprecation registry, and still works
- [ ] A plugin compiled against the previous SDK still loads and behaves identically (source *and* binary compatible)
- [ ] The conformance suite passes and the sample plugin builds and runs unchanged
- [ ] Any intentional break was explicitly agreed beforehand and is described below

## Notes

<!-- Optional: anything reviewers should know. Remove this section when unused. -->

## Checklist

- [ ] Tests were added or updated where needed
- [ ] The change was verified manually where appropriate
- [ ] Documentation was updated if the change affects documented behaviour
- [ ] I reviewed and understand every submitted change
