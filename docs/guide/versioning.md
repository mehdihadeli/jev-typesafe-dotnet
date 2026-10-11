# Release versioning

Typesafe.Sdk uses [Nerdbank.GitVersioning (NBGV)](https://dotnet.github.io/Nerdbank.GitVersioning/)
with GitHub Flow. `main` is the long-lived branch. Changes arrive through pull
requests, and release tags identify approved commits on `main`.

The release contract is:

- Ordinary merges advance a commit-height preview version.
- RC and stable releases start with a reviewed change to `version.json`.
- CI validates release tags against the version calculated by NBGV.
- Preview and RC packages include a UTC date and GitHub Actions run number.
- Stable packages use the clean `MAJOR.MINOR.PATCH` version.

## Sources of truth

[`version.json`](../../version.json) contains version intent. NBGV calculates
SemVer, assembly metadata, and commit height from that file and Git history.
CI pins the NBGV CLI to `3.10.94`; use that version locally too.

Current preview configuration:

```json
{
  "version": "1.0.0-preview.{height}",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/v\\d+\\.\\d+\\.\\d+(?:-rc\\.\\d+)?$"
  ]
}
```

Install the CLI once and inspect the calculated version:

```bash
dotnet tool install --global nbgv --version 3.10.94
dotnet nbgv get-version -v SemVer2
```

An untagged commit can include `.g<commit>` in the calculated version. That
suffix identifies the source commit and is not part of the public release tag.
CI uses `SemVer2` rather than the normalized `NuGetPackageVersion` output.

Run the helper commands from the repository root in Bash, such as Git Bash or
WSL on Windows. Preparation helpers change version intent only; they do not
commit, push, or publish. The `tag` command creates a local Git tag but does
not push it.

## Version shapes

| Source ref | Effective version | Release intent | Publication |
| --- | --- | --- | --- |
| `main`, preview height `0` | `1.0.0-preview.0` | none | Validation only |
| `main`, preview height `1+` | `1.0.0-preview.N.YYDDD.RUN_NUMBER` | dev | NuGet prerelease and GitHub draft |
| `v1.0.0-rc.1` | `1.0.0-rc.1.YYDDD.RUN_NUMBER` | staging | NuGet prerelease and GitHub prerelease |
| `v1.0.0` | `1.0.0` | production | NuGet package and GitHub release |

`YYDDD` is the UTC two-digit year and day of year. `RUN_NUMBER` is the GitHub
Actions workflow run number. These suffixes distinguish separately built
preview or RC packages without replacing the NBGV preview or RC ordinal.

The calculator also outputs assembly version, informational version
(`VERSION+COMMIT`), and the short source commit. The pack step passes the
effective package version through `ReleasePackageVersion`. The project applies
this override after NBGV calculates versions, so the package matches CI's
dated preview or RC version. NBGV continues to generate assembly metadata;
the calculator's informational-version output does not override it.

Typesafe.Sdk publishes the SDK package to NuGet.org and uploads package and
symbol-package assets to the matching GitHub release. It does not publish
executables, Docker images, or deployments. The calculator's environment
output describes release intent; the publish job always uses the GitHub
`nuget` environment for permissions and secrets.

## Preview releases

Do not edit `preview.1` to create `preview.2`. NBGV derives the ordinal from
Git commit height. Ordinary feature, fix, test, and documentation pull requests
should not change `version.json`.

The initial preview-train commit is validation-only when it calculates
`preview.0`. Later accepted changes advance the preview height and publish
dated preview packages. Preview tags are not supported; preview packages
publish from `main`.

Start a new preview train on a short-lived branch:

```bash
bash scripts/release-version.sh prepare-train 1.1.0
git add version.json
git commit -m "chore: start 1.1.0 preview train"
git push -u origin chore/prepare-1.1.0-preview
```

Open a pull request. The helper sets `1.1.0-preview.{height}` and removes
bootstrap height-offset properties if present.

## RC and stable releases

Prepare version changes on short-lived branches and merge them through
protected `main`. Tag the merged commit after CI validation succeeds.

Prepare the first RC train:

```bash
bash scripts/release-version.sh prepare-rc 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0 RC train"
git push -u origin chore/prepare-1.0.0-rc
```

After the pull request is merged and local `main` is updated, inspect the
calculated version, then create and push its tag:

```bash
dotnet nbgv get-version -v SemVer2
bash scripts/release-version.sh tag
git push origin v1.0.0-rc.1
```

The tag above is an example; push the tag NBGV actually creates. Later RC fixes
do not edit `version.json` or rerun `prepare-rc`. Another merge advances the
`1.0.0-rc.{height}` template to the next RC ordinal.

Prepare stable through a reviewed pull request:

```bash
bash scripts/release-version.sh prepare-stable 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0"
git push -u origin chore/prepare-1.0.0
```

After that change is merged and validated, update local `main` and run:

```bash
bash scripts/release-version.sh tag
git push origin v1.0.0
```

`prepare-stable` removes height-offset properties. The stable tag must point
to the merged stable-preparation commit, and CI publishes clean `1.0.0`
package metadata. After stable publication, use `prepare-train` to start the
next preview line.

## CI behavior

[`.github/workflows/build-and-publish.yml`](../../.github/workflows/build-and-publish.yml)
runs validation for pull requests targeting `main`, `main` pushes, and `v*`
tags. Version calculation and publication run only for `main` and `v*` tags.
Every version-calculating job checks out full history and tags with
`actions/checkout@v4` and `fetch-depth: 0`.

The workflow uses these stages:

1. `build-test` restores, builds, and runs unit tests. Integration tests run
   only when `TYPESAFE_API_KEY` is available.
2. `calculate-version` invokes [`scripts/calculate-version.sh`](../../scripts/calculate-version.sh)
   with the pinned NBGV CLI, selecting the effective version and release intent.
3. `publish` packs and pushes the SDK, updates or publishes the matching
   Release Drafter release, and uploads package assets. On `main`, only the
   `dev` intent publishes: `preview.0`, untagged RC commits, and untagged stable
   commits do not publish. NuGet publication requires `NUGET_API_KEY`.

For a supported release tag (`vMAJOR.MINOR.PATCH` or
`vMAJOR.MINOR.PATCH-rc.N`), CI removes the `v` prefix and compares it with NBGV's
calculated version after removing `.g<commit>`. A mismatch fails before
publication. For example, `v1.0.0-rc.3` cannot publish a commit calculating as
`1.0.0-rc.2`. Other tag shapes fail version calculation.

RC tag validation happens before CI appends the date and run number. The
GitHub release keeps the original RC tag, while its displayed version and
NuGet package include the build suffix.

Release Drafter uses `if: always()` so release notes can still update when an
earlier publication step fails. The job remains failed; fix the publication
problem and rerun it.

## Release Drafter

[`.github/release-drafter.yml`](../../.github/release-drafter.yml) classifies
pull requests by labels and excludes `skip-changelog`. `include-pre-releases:
false` keeps stable release notes based on the previous stable release.

On `main`, Release Drafter updates a preview draft. On RC and stable tags, the
workflow publishes the matching release. Release Drafter does not create the
Git tag; `dotnet nbgv tag` creates it locally, and the operator pushes it
explicitly. There is no separate Release Drafter workflow.

## Quick checks

Before pushing a release tag, inspect the calculated version:

```bash
dotnet nbgv get-version -v SemVer2
dotnet test --project tests/unit-tests/Typesafe.Sdk.UnitTests.csproj --configuration Release
```

Then inspect `version.json`, confirm the tag comes from the merged `main`
commit, and push only that tag. Never rewrite version intent in CI. The
release helper never commits or pushes on your behalf.

