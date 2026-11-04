# Verified local development environment

The user accepted Candidates + manually confirmed observations, repository creation, and a side-by-side SDK on 2026-11-03. Live scanning is a separately deferred feature.

- Repository: `<repository>`.
- Selected SDK: `~/.hermes/tools/dotnet-10/dotnet`, version **10.0.403**. System PATH still resolves to `/usr/local/share/dotnet/dotnet` with older SDKs; use the selected executable explicitly or prepend its directory for this shell only.
- SDK package SHA512 was checked against Microsoft's published release metadata before extraction. No existing SDK was removed, and no administrator installer was run.
- Project runtime: C# / .NET + Avalonia only. The Python scripts retained under research evidence are disposable external research tools, not bundled runtime dependencies.
- Mac GUI smoke passing means only that Avalonia opened and closed the application's own window on macOS. It does not establish Windows overlay or Linux compositor capability.
- No remote repository, push, tag, release, support email, game-account authorization or game interaction is authorized by local development.

## Common commands

```sh
export DOTNET_ROOT="$HOME/.hermes/tools/dotnet-10"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$HOME/.hermes/projects/poe-flip-overlay"
dotnet --version
dotnet restore FlipOverlay.slnx --locked-mode
dotnet build FlipOverlay.slnx -c Release --no-restore
dotnet test FlipOverlay.slnx -c Release --no-build --no-restore
dotnet format FlipOverlay.slnx --verify-no-changes --no-restore
```

Target runtime identifiers are declared in Directory.Build.props so the ordinary restore and RID-specific self-contained restore use the same lock graph. Do not let an ad hoc `publish -r` silently rewrite lock files into a different RID graph; verify locked restore again after publishing.

Never run Windows-only verification on macOS and label it passed. `tools/verify.ps1` deliberately refuses a non-Windows host.
