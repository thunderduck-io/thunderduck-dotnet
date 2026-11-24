# Contributing

Thanks for taking the time to look at `Thunderduck.Data`.

## This repository is a mirror

The source of truth for this SDK lives in Thunderduck's internal monorepo.
This repository is a one-way snapshot: each update replaces the tree with the
current state of the source, as a single commit. That means the history here is
not the development history of the project, and a pull request cannot be merged
directly into it.

## Issues are welcome

Bug reports and feature requests are read here. Please include:

- the package version (`dotnet list package`) and the target framework;
- your .NET SDK version and operating system;
- what you did, what you expected, and what happened;
- the connection string with the token and hostname removed, and the smallest
  SQL statement or code sample that reproduces the problem.

For anything that looks like a security problem, use
[SECURITY.md](SECURITY.md) instead of a public issue.

## Pull requests

Pull requests are welcome as a place to discuss a change, and small fixes are
genuinely useful. If a change is accepted, it is applied to the internal source
and appears in the next snapshot; the pull request is then closed rather than
merged, so don't be surprised if your commit does not appear here verbatim.

Please keep a change focused: one behaviour per pull request, with a test that
fails before it and passes after.

## Development

```bash
dotnet build -c Release
dotnet test tests/Thunderduck.Data.Tests -c Release --no-build
```

The integration tests under `tests/Thunderduck.Data.IntegrationTests` talk to a
real deployment and need an API token; the unit tests do not.

## License

By contributing you agree that your contribution is licensed under the Apache
License 2.0, the license this project uses. See [LICENSE](LICENSE).
