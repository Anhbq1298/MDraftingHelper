# Git hooks

Enable the repository hooks after cloning:

```powershell
git config core.hooksPath .githooks
```

The `commit-msg` hook requires a `feat: <description>` subject. The
`prepare-commit-msg` hook adds the UTC commit time and configured Git author to
the commit message. Git does not transfer hook configuration to other clones,
so each clone must enable the hooks separately.
