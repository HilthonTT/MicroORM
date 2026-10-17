# Contributing to MicroORM

Thanks for helping. This guide covers setup, conventions and the release process.

## Before you start

- **Bugs:** open an issue using the bug template, with a reproduction and the generated SQL (`MicroOptions.Log`).
- **Features:** open an issue first to discuss the API. MicroORM deliberately stays small. Ideas that
  belong in raw SQL (complex joins, provider-specific features) usually stay there.
- **Security issues:** follow [SECURITY.md](SECURITY.md), never a public issue.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`global.json` pins the feature band)
- Optional: [Docker](https://www.docker.com/), for the PostgreSQL, SQL Server and MySQL integration tests

## Repository layout

```
├── src/MicroORM/                        The library (the only packed project)
├── tests/MicroORM.Tests/                Unit tests + integration tests (Integration/)
├── samples/MicroORM.Samples.QuickStart  Runnable example
├── benchmarks/MicroORM.Benchmarks       BenchmarkDotNet comparison with Dapper
├── website/                             Documentation site (Next.js, deployed to GitHub Pages)
├── assets/                              Logo (SVG source + PNG package icon)
├── .github/                             CI/CD workflows, issue and PR templates, Dependabot
├── MicroORM.slnx                        Solution
├── Directory.Build.props                Shared build settings and package metadata
└── Directory.Packages.props             Central package versions
```

## Build and test

```shell
dotnet build MicroORM.slnx
dotnet test MicroORM.slnx
dotnet format MicroORM.slnx            # CI runs this with --verify-no-changes
```

The build turns warnings into errors on CI (`CI=true`). To check locally:

```shell
dotnet build MicroORM.slnx -c Release -p:TreatWarningsAsErrors=true
```

### Integration tests

`tests/MicroORM.Tests/Integration/ProviderTests.cs` runs every scenario against each configured
database. SQLite (in memory) always runs. The others run when their connection string is set:

| Variable | Database |
|---|---|
| `MICROORM_TEST_POSTGRES` | PostgreSQL |
| `MICROORM_TEST_SQLSERVER` | SQL Server |
| `MICROORM_TEST_MYSQL` | MySQL |

Start all three with Docker (these are the same images CI uses):

```shell
docker run -d --rm --name microorm-pg    -e POSTGRES_PASSWORD=microorm -e POSTGRES_DB=microorm -p 55432:5432 postgres:17
docker run -d --rm --name microorm-mssql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=MicroOrm_Passw0rd!" -p 14330:1433 mcr.microsoft.com/mssql/server:2022-latest
docker run -d --rm --name microorm-mysql -e MYSQL_ROOT_PASSWORD=microorm -e MYSQL_DATABASE=microorm -p 53306:3306 mysql:8.4
```

Then set the variables and run the tests:

```shell
# bash
export MICROORM_TEST_POSTGRES="Host=localhost;Port=55432;Username=postgres;Password=microorm;Database=microorm"
export MICROORM_TEST_SQLSERVER="Server=localhost,14330;User Id=sa;Password=MicroOrm_Passw0rd!;TrustServerCertificate=true;Database=master"
export MICROORM_TEST_MYSQL="Server=localhost;Port=53306;User ID=root;Password=microorm;Database=microorm"
dotnet test MicroORM.slnx
```

```powershell
# PowerShell
$env:MICROORM_TEST_POSTGRES = "Host=localhost;Port=55432;Username=postgres;Password=microorm;Database=microorm"
$env:MICROORM_TEST_SQLSERVER = "Server=localhost,14330;User Id=sa;Password=MicroOrm_Passw0rd!;TrustServerCertificate=true;Database=master"
$env:MICROORM_TEST_MYSQL = "Server=localhost;Port=53306;User ID=root;Password=microorm;Database=microorm"
dotnet test MicroORM.slnx
```

Any change to SQL generation must pass on all four databases. CI runs them on every pull request.

## Coding conventions

- `.editorconfig` defines the style; `dotnet format` enforces it.
- Public APIs need XML doc comments. The build produces the documentation file.
- Library code uses `ConfigureAwait(false)` and has no dependencies beyond the BCL.
- Prefer throwing a clear exception over guessing. Error messages should say what to do next.
- One type per file, named after the type. Folders group files by responsibility.
- Every public operation needs an `...Async` and a synchronous form, both calling one
  `...Core(bool async, ...)` implementation (see [Architecture](https://hilthontt.github.io/MicroORM/docs/architecture/)).
- Name tests as behavior sentences: `Null_comparisons_use_is_null`.
- Performance-sensitive changes: run the benchmarks before and after (see [Benchmarks](https://hilthontt.github.io/MicroORM/docs/benchmarks/)).
- See [Architecture](https://hilthontt.github.io/MicroORM/docs/architecture/) for how the pieces fit together.

## Documentation site

The documentation lives in `website/`, a Next.js site with one MDX page per guide in
`website/app/docs/<page>/page.mdx`. Update it together with user-facing changes:

```shell
cd website
npm install
npm run dev        # http://localhost:3000
```

Pushing to `main` deploys it to GitHub Pages.

## Pull requests

1. Branch from `main`.
2. Keep each change focused, and add tests: unit tests for SQL shape, integration tests for behavior.
3. Update docs and add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
4. Make sure CI is green.

## Releasing (maintainers)

Releases are tag-driven. [`release.yml`](.github/workflows/release.yml) builds, tests, packs,
publishes to NuGet and creates a GitHub release.

1. Move the **Unreleased** changelog entries under a new version heading and update the compare links.
2. Bump `VersionPrefix` in `Directory.Build.props` so CI preview packages use the next version.
3. Commit, then tag and push:

   ```shell
   git tag v0.2.0
   git push origin v0.2.0
   ```

Tags with a hyphen (`v0.2.0-beta.1`) are published as pre-releases. Publishing to NuGet needs a
`NUGET_API_KEY` repository secret, scoped to push the `MicroORM` package. Without it, the workflow
still creates the GitHub release with the packages attached.
