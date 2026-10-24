# Contributing

Contributions are welcome. Please keep changes focused and preserve the separation between CAD/domain logic, persistence, and Avalonia UI code.

## Development workflow

1. Create a topic branch from `main`.
2. Add or update tests for behavior changes.
3. Run `dotnet format DatumForge.slnx --verify-no-changes`, `dotnet build DatumForge.slnx -c Release`, and `dotnet test DatumForge.slnx -c Release --no-build`.
4. Document public file-format changes and increment the relevant format version when compatibility changes.
5. Open a pull request describing the user-visible result and validation performed.

Never commit real user projects, drawing history, credentials, or generated build output. New dependencies should have a clear purpose and a license compatible with MIT distribution.

## Commit style

Use short imperative subjects, for example `Add project history snapshots`. Prefer a small sequence of reviewable commits over one mixed commit.
