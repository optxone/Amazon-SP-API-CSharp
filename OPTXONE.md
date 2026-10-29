# Optxone fork

Fork of [abuzuhri/Amazon-SP-API-CSharp](https://github.com/abuzuhri/Amazon-SP-API-CSharp), built and
published by us so we can ship library fixes without waiting on upstream merges.

- **Package**: `Optxone.CSharpAmazonSpAPI` on the optxone GitHub Packages feed
  (`https://nuget.pkg.github.com/optxone/index.json`). Same `FikaAmazonAPI` assembly and namespaces as
  upstream's `CSharpAmazonSpAPI` — only the package id differs.
- **Versioning**: `<upstream version>.<our revision>`, e.g. `1.9.33.1`, `1.9.33.2`. Reset the revision
  to `.1` after taking a new upstream version.
- **Upstream's `Release` workflow (nuget.yml) is disabled** in this repo — it publishes to nuget.org.

## Getting upstream updates

```bash
git remote add upstream https://github.com/abuzuhri/Amazon-SP-API-CSharp.git   # once
git fetch upstream
git checkout main
git merge upstream/main        # resolve conflicts with our changes, if any
git push origin main
```

(Or use **Sync fork** on GitHub when there are no conflicts.)

## Releasing

```bash
git tag optx-v1.9.33.1
git push origin optx-v1.9.33.1
```

The `Optxone Package` workflow packs and pushes the package. Then bump the version in Crigora's
`Crigora.Integration.Amazon.csproj`.

## Our changes

Keep upstream-able fixes small and, where possible, also open a PR upstream so the fork does not drift.
