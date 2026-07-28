# Release Process

This project publishes the `NUglify` NuGet package from GitHub Actions.

## Prepare the Release

1. Update `src/NUglify/NUglify.csproj`.
   - Set `<Version>` to the release version, for example `1.22.1`.
2. Update `changelog.md`.
   - Add a new entry at the top.
   - Use the tag format as the heading, for example `## v1.22.1 (28 July 2026)`.
3. Run the release validation locally.

```powershell
dotnet restore NUglify.slnx
dotnet build NUglify.slnx -c Release --no-restore -p:UseSharedCompilation=false
dotnet test src\NUglify.Tests\NUglify.Tests.csproj -c Release --no-build -p:UseSharedCompilation=false
dotnet pack src\NUglify\NUglify.csproj -c Release -o artifacts\packages --no-build
```

4. Inspect the package in `artifacts\packages`.
   - Confirm the `.nupkg` version matches the intended release.
   - Confirm package metadata, icon, readme, license, and release notes are present.

## Publish a Stable Release

1. Commit the release changes.
2. Create and push a matching version tag.

```powershell
git tag v1.22.1
git push origin HEAD
git push origin v1.22.1
```

The `Publish` workflow runs for tags matching `v*`. For stable tags, the package version is taken from the tag name without the leading `v`.

3. Draft a new GitHub Release manually.
   - Go to GitHub Releases.
   - Draft a new release from the pushed tag, for example `v1.22.1`.
   - Use the matching `changelog.md` entry as the release notes.
   - Publish the GitHub Release after the `Publish` workflow succeeds.

## Publish a Prerelease

Push or move the `latest` tag when a prerelease package is needed.

```powershell
git tag -f latest
git push origin latest --force
```

The `Publish` workflow turns the project version into a prerelease version using the GitHub run number, for example `1.22.1-pre042`.

## GitHub Actions

- `CI` runs on pushes and pull requests. It restores, builds, tests, packs, and uploads the package artifact.
- `Publish` runs on `v*` and `latest` tags. It restores, builds, tests, packs, uploads the artifact, and pushes to NuGet.org when `NUGET_API_KEY` is configured.

## After Publishing

1. Check the GitHub Actions run completed successfully.
2. Confirm the package appears on NuGet.org.
3. Confirm the NuGet badge in `README.md` updates after NuGet indexing catches up.
