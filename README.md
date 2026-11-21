# BijliGuard

BijliGuard is a simple web app for Pakistani households. It answers one daily question: **how many electricity units have I used, and how many can I still use this month to stay under 200?**

## Components

| Folder | What it is |
|---|---|
| `src/` | .NET backend (clean architecture): `BijliGuard.API`, `BijliGuard.Application`, `BijliGuard.Domain`, `BijliGuard.Infrastructure` |
| `tests/` | Unit tests for the Application and Domain layers |
| `web/` | Next.js web frontend (its own git repository) |
| `mobile/` | Expo / React Native mobile app (its own git repository) |
| `docs/` | Technical design documents (`TECHNICAL_DESIGN_v2.md` is the current MVP scope) |

`web/` and `mobile/` are separate git repositories and are excluded from this repository via `.gitignore`.

## Build

Backend (requires the .NET SDK):

```
dotnet build BijliGuard.slnx
dotnet test BijliGuard.slnx
```

Web and mobile: see the README and `AGENTS.md` inside each folder.

## Design

See `docs/TECHNICAL_DESIGN_v2.md` for product scope, and `docs/TECHNICAL_DESIGN.md` for the original broader design.
