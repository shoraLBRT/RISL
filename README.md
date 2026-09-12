**English** · [Русский](README.ru.md)

# RISL — Russian Islamic Sign Language

A web dictionary of Islamic terms in Russian Sign Language: the project adds
signs that RSL did not have before. Every term comes with a definition and a
video recording of the sign. Visitors search and watch; a single administrator
fills in and edits the dictionary.

The application is made for the Zakat charity foundation (zakatfund.ru) and is
part of its ecosystem, but it is not the foundation's own website. The design
follows the foundation's brand palette, and the logo is used as a mark of
affiliation.

## Features

- Search across words and definitions with ranking: exact match → start of the
  word → occurrence inside the word → occurrence in the definition.
- Filtering by topic, alphabetical index, paged results.
- Word page with video slowed down to half speed and looped playback; the
  chosen speed is remembered.
- Favourites — stored in the visitor's browser, no sign-up.
- Feedback form with a submission rate limit.
- Admin area: editing words and topics, bulk dictionary import from CSV + a zip
  of videos, the transcoding queue, and messages from visitors.

The public part is served as ordinary server-rendered HTML: the Blazor
interactive mode is not enabled, and no guest request opens a WebSocket. The
site is indexed by search engines and works with JavaScript turned off — only
search-as-you-type, favourites, and the speed buttons are lost. The reasoning
behind this is in [docs/architecture.md](docs/architecture.md) (in Russian).

## Requirements

- .NET SDK 10.0
- ffmpeg and ffprobe on `PATH` — without them the application still starts, but
  uploaded recordings end up in the "Error" state

## Quick start

```bash
dotnet run --project RISL.Blazor
```

In the Development environment the database is seeded with samples; the login
and password are `admin` / `admin123` (see
`RISL.Blazor/appsettings.Development.json`). The admin panel lives at `/admin`.

```bash
dotnet test
```

## Running in Docker

The image already contains ffmpeg; the database and media files live on the
`./data` volume and survive a rebuild.

```bash
cp .env.example .env
```

Fill in `.env` — the password salt and hash are printed by a helper command:

```bash
dotnet run --project RISL.Blazor -- hash-password your-password
```

```bash
docker compose up --build
```

The application will be available at `http://localhost:8080`. Running it
together with an HTTPS proxy, configuring the environment, and backups are
described in [docs/operations.md](docs/operations.md) (in Russian).

## Solution layout

```
RISL.Domain          entities and text normalization, no external dependencies
RISL.Application     ports, search index, CSV parsing, password hashing
RISL.Infrastructure  EF Core/SQLite, file storage, ffmpeg, background services
RISL.Blazor          pages, form endpoints, static files
RISL.Tests           xUnit: search, import, storage, admin services
```

Dependencies point strictly in one direction:
`Blazor → Infrastructure → Application → Domain`.

## Documentation

The documents below are written in Russian.

- [docs/architecture.md](docs/architecture.md) — how the application is built,
  the decisions taken and their reasons, video processing, security.
- [docs/operations.md](docs/operations.md) — deployment, configuration, filling
  the dictionary, backups.

## License

The whole project — the code and the dictionary content (sign videos, word
definitions, topic names) — is distributed under the [MIT](LICENSE) license.
