# BijliGuard: Technical Design v2 (MVP)

Status: Draft v2.0. This version replaces the broader v0.1 design for the MVP. Features outside this scope are listed in section 16 as later phases.

## 1. Product Summary

BijliGuard is a simple web app for Pakistani households. It answers one question every day: **how many units have I used, and how many can I still use this month to stay under 200?**

### 1.1 The MVP in one flow

1. **One-time setup.** The user gives a starting point: either uploads the latest electricity bill (the app reads the last meter reading and its date), or types the last meter reading and date by hand.
2. **Daily entry.** Each day the user takes a photo of the meter and enters the exact reading shown. The app checks the reading and saves it.
3. **Instant analysis.** The app calculates:
   - units used today,
   - units used in the current billing cycle,
   - average units per day,
   - units remaining before the target (default 200),
   - the allowed units per day for the rest of the cycle.

There is no history view, no six-month chart, no bill comparison, and no tariff engine in the MVP. Each screen shows the current state, calculated from the baseline plus the readings entered so far.

### 1.2 Core promise

> One bill upload (or one reading), one meter photo a day, and a clear daily unit count.

### 1.3 Interpretation notes

Two requirements are interpreted as follows. Please confirm them.

- "Send the exact units": the user types the reading number shown on the meter. The photo is kept as proof. OCR may suggest the number, but the user's confirmed number is what is saved.
- "One-time refresh": the baseline is entered once. After that, the dashboard always recalculates from the stored baseline and readings. It does not need re-setup.

## 2. Architecture Decision: Modular Monolith vs Microservices

### 2.1 Options

| Criterion | Microservices | Modular monolith (recommended) |
|---|---|---|
| Team size needed | Several teams to own services | One small team |
| Deployment | Many pipelines, containers, service discovery | One API container and one web app |
| Data consistency | Distributed transactions or eventual consistency | Single PostgreSQL transaction |
| Local development | Many services must run together | One API, one database, one frontend |
| Operational cost | High (tracing, networking, versioning per service) | Low |
| Clean boundaries | Enforced by network | Enforced by project references and module rules |
| Future extraction | Already split | Possible later, because modules are separated |
| Fit for MVP scale | Over-engineered | Right size |

### 2.2 Recommendation

**Use a modular monolith on .NET 10 with Clean Architecture, and a React (Next.js) web app.**

Reasons:
- The MVP has one database, a small number of features, and a small team. Microservices would add cost without benefit.
- Clean Architecture already gives the separation that microservices would enforce. Each module can be extracted later if needed.
- Calculations must be consistent. A reading save and the dashboard recalculation should happen in one transaction.

### 2.3 Future extraction candidates

When load or ownership requires it, these components can be moved to separate services without redesigning the domain:

1. **OCR service:** calls external APIs, has the highest latency and cost, and scales independently.
2. **Notification service:** push and email delivery (Phase 3).
3. **Bill analysis service:** heavy document processing (Phase 4).

Each one sits behind an interface in the Application layer from day one, so the change stays local.

## 3. Technology Stack

| Layer | Technology | Notes |
|---|---|---|
| Web frontend | React with Next.js, TypeScript, Tailwind CSS | Responsive, mobile-first |
| Charts | Recharts | Simple daily-units bar chart |
| Forms and validation (client) | React Hook Form and Zod | Rules mirror the API |
| Backend | ASP.NET Core Web API, .NET 10 | Modular monolith |
| Architecture | Clean Architecture, CQRS-lite (command and query handlers, no MediatR required) | Keep it simple |
| Data access | Entity Framework Core, PostgreSQL | Migrations in Infrastructure |
| Auth | ASP.NET Core Identity, JWT access tokens, refresh token rotation | |
| Validation (server) | FluentValidation | |
| Logging | Serilog (structured), enriched with correlation IDs | See section 11 |
| API docs | Swagger / OpenAPI | Development and staging |
| OCR | Google Cloud Vision behind `IMeterReadingOcr` | Only used for suggestions |
| File storage | Private object storage (S3-compatible or Azure Blob) | Signed, short-lived URLs |
| Tests | xUnit, FluentAssertions, Testcontainers (PostgreSQL), Vitest, Playwright | |
| Mobile (later) | Expo React Native, same API | Phase 5 |

## 4. Solution Structure

```
BijliGuard/
  BijliGuard.sln
  src/
    BijliGuard.Domain/            Entities, value objects, domain services, domain errors
    BijliGuard.Application/       Use cases (commands/queries), interfaces, DTOs, validators
    BijliGuard.Infrastructure/    EF Core, repositories, OCR client, storage, Identity, logging sinks
    BijliGuard.API/               Controllers, middleware, auth setup, Swagger, Program.cs
  tests/
    BijliGuard.Domain.Tests/      Consumption and validation rules (pure unit tests)
    BijliGuard.Application.Tests/ Use case tests with fake repositories
    BijliGuard.API.Tests/         Endpoint tests with WebApplicationFactory
    BijliGuard.Infrastructure.Tests/ Repository and migration tests (Testcontainers)
  web/                            Next.js application
  docs/                           Design documents
```

### 4.1 Dependency rules

- Domain references nothing.
- Application references Domain only.
- Infrastructure references Application (implements its interfaces).
- API references Application and Infrastructure (only for registration in DI).
- Controllers never reference Domain entities directly. They use DTOs from Application.
- Web app talks to the API only through HTTP.

### 4.2 Module layout inside Application

Each feature folder contains its commands, queries, validators, and handlers:

```
Application/
  Identity/        Register, Login, Refresh, ConsentRecord
  Households/      CreateHousehold, SetProvider, GetHousehold
  Setup/           StartBaseline (from bill or manual), ConfirmBaseline
  Readings/        SubmitReading, ConfirmFlaggedReading, ListReadings
  Consumption/     GetDailySummary (calculation orchestration)
  Ocr/             ExtractReadingFromPhoto, ExtractBaselineFromBill
  Common/          Result type, errors, pagination, logging abstractions
```

## 5. Domain Model

### 5.1 Entities

| Entity | Purpose | Key fields |
|---|---|---|
| `User` | Login account | Id, Email, PasswordHash (via Identity), Role, CreatedAt |
| `RefreshToken` | Session rotation | Id, UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedById |
| `ConsentRecord` | Permission to process bills and photos | Id, UserId, ConsentVersion, AcceptedAt, WithdrawnAt |
| `Household` | Owner of meters and cycle settings | Id, OwnerUserId, Provider (enum), ConsumerReferenceEncrypted, ConsumerReferenceMasked, TargetUnits (default 200), CreatedAt |
| `ElectricityMeter` | Physical meter | Id, HouseholdId, MeterNumberEncrypted, IsActive |
| `Baseline` | Starting point for all calculations | Id, MeterId, ReadingValue, ReadingDate, Source (Bill or Manual), SourceBillId?, CycleStartDate, CycleEndDate, ConfirmedAt |
| `MeterReading` | One confirmed daily reading | Id, MeterId, ReadingValue, ReadingDate, Status (Accepted, Flagged, Rejected), FlagReason?, RolloverOffset, PhotoId?, EnteredBy (User or OCR), CreatedAt |
| `ReadingPhoto` | Proof image | Id, ReadingId?, StorageKey, Sha256, ContentType, SizeBytes, Exif stripped flag, CreatedAt |
| `BillUpload` | Uploaded bill used only for baseline | Id, HouseholdId, StorageKey, Sha256, Status, CreatedAt |
| `OcrLog` | Record of every OCR attempt | Id, Purpose (MeterPhoto, Bill), ProviderName, ConfidenceScore, ExtractedValue, Succeeded, DurationMs, CreatedAt |
| `AuditLog` | Who did what | Id, ActorUserId, Action, EntityType, EntityId, OccurredAt, CorrelationId, Details (JSON, no personal data) |

### 5.2 Value objects and rules

- `MeterValue`: non-negative integer, maximum digits configured per meter (for example, 5 or 6).
- `ReadingDate`: a calendar date in Pakistan Standard Time (UTC+5). Stored as date, not timestamp.
- `ConsumerReference`: validated format, encrypted at rest, masked in responses (for example, `XXXX-XX1234`).
- `UnitsPerDay`: decimal with two decimal places, rounded only at presentation.

### 5.3 Simplifications from v0.1

- No `BillingCycles` table. The cycle is a property of the baseline (start and end date from the bill, or entered by the user). It is changed only through a new setup.
- No `ConsumptionPredictions` table. Predictions are computed on request, not stored.
- No `Notifications` table in MVP.
- No `TariffConfigurations` or `BillLineItems` in MVP.

## 6. Calculation Rules

### 6.1 Core formulas

```
Daily Units        = Reading(today) - Reading(previous accepted reading)
Days Elapsed       = ReadingDate(today) - ReadingDate(previous accepted reading)
Units per Day      = Daily Units / Days Elapsed            (average over the interval)
Cycle Used         = Latest Accepted Reading - Baseline.ReadingValue
Cycle Days         = CycleEndDate - CycleStartDate + 1
Days Elapsed Cycle = Latest ReadingDate - CycleStartDate  (minimum 0)
Days Remaining     = CycleEndDate - Latest ReadingDate
Average per Day    = Cycle Used / Days Elapsed Cycle      (when Days Elapsed Cycle > 0)
Remaining Units    = TargetUnits - Cycle Used             (may be negative; shown as 0 with an over-target flag)
Allowed per Day    = Remaining Units / Days Remaining     (when Days Remaining > 0)
Projected Cycle    = Average per Day x Cycle Days
```

Example from the product brief: previous 12,500, current 12,525 gives 25 units. The worked example in section 6.4 uses this.

### 6.2 Rules of thumb

- All arithmetic is done in code with `decimal` or integer math. No LLM or OCR component performs calculations.
- Reading values are integers. Calculations never use floating point.
- If the same day already has an accepted reading, the new reading must replace it only through an explicit correction action. Corrections are audited.
- If a gap of several days exists between readings, the units are spread evenly across the gap and the response is marked `MissingDays = true`.

### 6.3 Validation of a new reading

Checked in this order; the first failure stops the process.

| # | Check | Result on failure |
|---|---|---|
| 1 | Value is a non-negative integer with no more digits than the meter allows | Rejected, 422 |
| 2 | Reading date is not in the future (PKT) | Rejected, 422 |
| 3 | Reading date is after the last accepted reading date, or equals it (correction) | Rejected unless correction |
| 4 | Photo SHA-256 is not already used by another reading | Rejected, 409 |
| 5 | Value is not lower than the previous accepted reading | If the drop is large (rollover-sized), flagged for rollover confirmation. If small, rejected as a probable misread. |
| 6 | Daily units above a configured ceiling (default 100 units per day) | Flagged for confirmation, saved as Flagged |
| 7 | OCR suggestion differs from confirmed value by more than the tolerance | Logged to `OcrLog` with the difference, not blocked |

Rollover handling: if the new value is lower than the previous one and the drop is close to the meter's maximum (for example, previous 99,980 and new 00,015), the app asks the user to confirm rollover. On confirmation, the stored `RolloverOffset` is increased by 10^digits, so daily math stays correct.

### 6.4 Worked example

Setup: baseline reading 12,500 on 1 Nov, cycle 1 Nov to 30 Nov (30 days).

| Date | Reading | Daily units | Days since prev | Units per day (interval) |
|---|---|---|---|---|
| 1 Nov | 12,500 (baseline) | | | |
| 2 Nov | 12,525 | 25 | 1 | 25.0 |
| 3 Nov | 12,545 | 20 | 1 | 20.0 |

On 3 Nov: Cycle Used = 45. Days Elapsed Cycle = 2. Average per Day = 22.5. Remaining = 155. Days Remaining = 27. Allowed per Day = 5.74. Projected Cycle = 675 units (for this example; the projection assumes the same average continues).

Note that projection values are informational. The app does not state that a bill will be a certain amount.

## 7. API Design

### 7.1 Conventions

- REST over HTTPS, versioned under `/api/v1`.
- JSON, camelCase.
- Every request carries an `X-Correlation-Id` header. If absent, the server creates one and returns it.
- Standard response envelope:

```json
{
  "success": true,
  "data": {},
  "error": null,
  "correlationId": "7f3c..."
}
```

Failure:

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "READING_LOWER_THAN_PREVIOUS",
    "message": "The reading is lower than the last accepted reading.",
    "details": [{ "field": "readingValue", "message": "Must be at least 12,545." }]
  },
  "correlationId": "7f3c..."
}
```

- Status codes: 200 OK, 201 Created, 202 Accepted (OCR processing), 204 No Content, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict (duplicate photo or reading), 413 Payload Too Large, 422 Unprocessable (validation), 429 Too Many Requests, 500 Internal Server Error.
- Errors use stable `code` values so the web app can show the right message.

### 7.2 Endpoints

| Method | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Create account | Anonymous |
| POST | `/api/v1/auth/login` | Issue tokens | Anonymous |
| POST | `/api/v1/auth/refresh` | Rotate refresh token | Refresh cookie |
| POST | `/api/v1/auth/logout` | Revoke refresh token | User |
| POST | `/api/v1/consent` | Record consent version | User |
| POST | `/api/v1/household` | Create household with provider and consumer reference | User |
| GET | `/api/v1/household` | Get the caller's household | User |
| PUT | `/api/v1/household/target` | Change target (default 200) | User |
| POST | `/api/v1/setup/baseline/manual` | Save baseline from typed reading, date, and cycle dates | User |
| POST | `/api/v1/setup/baseline/bill` | Upload bill, start extraction, returns `202` with an upload ID | User |
| GET | `/api/v1/setup/baseline/bill/{uploadId}` | Poll extraction status and proposed values | User |
| POST | `/api/v1/setup/baseline/bill/{uploadId}/confirm` | Confirm proposed values and save baseline | User |
| POST | `/api/v1/readings/photo` | Upload meter photo, returns `202` with an upload ID and OCR suggestion | User |
| POST | `/api/v1/readings` | Submit confirmed reading value and date (links to photo) | User |
| POST | `/api/v1/readings/{id}/confirm-rollover` | Confirm a flagged rollover | User |
| POST | `/api/v1/readings/{id}/correct` | Replace a same-day reading (audited) | User |
| GET | `/api/v1/readings?from=&to=` | List readings for the current cycle | User |
| GET | `/api/v1/dashboard` | Daily summary (section 8) | User |
| GET | `/api/v1/logs/me` | The caller's own recent activity (audit view) | User |
| GET | `/api/v1/health` | Liveness and database check | Anonymous, restricted in production |

### 7.3 Upload handling

- Maximum image size: 5 MB. Bill PDF: 10 MB.
- Content type is checked by file signature (magic bytes), not extension.
- Images are re-encoded and stripped of EXIF metadata before storage.
- Storage key is a random GUID. Original filenames are not stored.
- Photos are served only through signed URLs valid for 5 minutes.

## 8. Dashboard Contract

`GET /api/v1/dashboard` returns:

```json
{
  "meter": { "id": "...", "lastReadingValue": 12545, "lastReadingDate": "2026-11-03" },
  "today": { "date": "2026-11-03", "units": 20, "hasReading": true, "missingDays": false },
  "cycle": {
    "startDate": "2026-11-01",
    "endDate": "2026-11-30",
    "usedUnits": 45,
    "targetUnits": 200,
    "remainingUnits": 155,
    "percentUsed": 22.5,
    "daysElapsed": 2,
    "daysRemaining": 27,
    "averagePerDay": 22.5,
    "allowedPerDay": 5.74,
    "projectedUnits": 675,
    "overTarget": false
  },
  "alerts": [
    { "code": "BELOW_TARGET_RISK", "severity": "warning", "message": "Your projected usage is above the target. Try to keep below 5.7 units per day." }
  ],
  "dailySeries": [
    { "date": "2026-11-02", "units": 25 },
    { "date": "2026-11-03", "units": 20 }
  ]
}
```

- `dailySeries` covers the current cycle only.
- Alerts are computed on each request. They are not stored in MVP.
- Alert codes: `APPROACHING_TARGET` (80%), `NEAR_TARGET` (90%), `OVER_TARGET`, `PROJECTED_OVER_TARGET`, `HIGH_DAILY_USE`, `NO_READING_TODAY`, `INVALID_READING`.

### 8.1 Alert wording

Alerts use "may" and "projected," and never state a bill amount or eligibility result.

Example: "You have used 165 units with 6 days remaining. Your remaining allowance is 35 units. Try to keep consumption below 5.8 units per day."

## 9. Web Application Design

### 9.1 Screens

| Route | Purpose |
|---|---|
| `/register`, `/login` | Account |
| `/setup/consent` | Consent screen (shown once, version recorded) |
| `/setup/household` | Provider and consumer reference |
| `/setup/baseline` | Choose: upload bill, or enter reading manually |
| `/setup/baseline/confirm` | Review proposed values (from bill) and confirm |
| `/dashboard` | Today, cycle summary, daily chart, alerts |
| `/reading/new` | Take or upload a photo, see OCR suggestion, type exact reading, submit |
| `/reading/flagged/[id]` | Confirm rollover or correction |
| `/settings` | Target, cycle dates (view), log view |
| `/activity` | Own audit log entries |

Pages not in MVP: history, bill analysis, tariffs, notifications, and admin.

### 9.2 Reading submission flow

1. User opens `/reading/new`.
2. User selects a photo. The browser compresses it to at most 2048 px on the long side.
3. The app uploads the photo. It returns `202` and an upload ID.
4. The page polls the status until OCR returns a suggestion (or a failure).
5. The page shows the photo and the suggestion in a field, but the user must type or confirm the exact number.
6. User submits. The API validates. Outcomes:
   - Accepted: the dashboard shows the new day's units.
   - Flagged: the page explains why and offers the confirmation step.
   - Rejected: the page shows the error code and message and keeps the typed value.

OCR failure is not a blocker. The user can type the reading directly.

### 9.3 Frontend structure

```
web/
  app/                      Next.js App Router pages
  components/
    dashboard/              SummaryCard, DailyBarChart, AlertList
    reading/                PhotoPicker, ReadingForm, ValidationMessage
    setup/                  BaselineChoice, BillPreview
    common/                 Button, Card, Field, ErrorBanner, Spinner
  lib/
    api/                    Typed client per resource (auth, household, readings, dashboard)
    auth/                   Token state, silent refresh, logout
    logging/                Client logger (see section 11.5)
    format/                 Units, dates (PKT), numbers
  types/                    DTO types mirrored from the API OpenAPI document
  tests/                    Component and end-to-end tests
```

### 9.4 Frontend rules

- Access token kept in memory. Refresh token stored in an HttpOnly, Secure, SameSite=Strict cookie.
- Server components for initial page data; client components only where interaction requires it.
- Every API call goes through one client module that adds the correlation ID and logs failures.
- Layout designed at 375 px width first. 16 px side gutters, no horizontal scroll.
- Numbers use Western digits with thousands separators, and units are always labeled "units" or "kWh" consistently (the bill uses units; the app uses "units").
- Accessible: labeled inputs, visible focus, color is never the only signal of a warning.

## 10. Security

| Area | Design |
|---|---|
| Passwords | ASP.NET Core Identity (PBKDF2 or Argon2 via a hasher implementation). Minimum length 10. |
| Tokens | Access JWT 15 minutes. Refresh token 7 days, stored as hash, rotated on use. Reuse of a rotated token revokes the whole family. |
| Authorization | Role `User` and `Admin`. Every household query is filtered by the caller's household ID, enforced in the Application layer, not only in the controller. |
| Consumer reference and meter number | Encrypted at rest with AES-GCM using a key from the secrets store. Masked in responses. Never logged. |
| Images | Private storage. Signed URLs. EXIF stripped. Deleted when the reading is deleted, or on account deletion. |
| Input | FluentValidation on commands. File signature check. Size limits. |
| Rate limits | Login: 5 per minute per IP. Upload endpoints: 10 per minute per user. General: 120 per minute per user. |
| Transport | HTTPS only, HSTS, CORS restricted to the web origin. |
| Headers | CSP, X-Content-Type-Options, Referrer-Policy on the web app. |
| Consent | Recorded before any upload is accepted. Withdrawal blocks new uploads and deletes stored images on request. |
| Data isolation | Tested with two users: user B cannot read or change user A's data. |

## 11. Logging Design

Logging is a requirement for every feature. Each feature has defined events, levels, and fields. Nothing is logged without a clear reason.

### 11.1 Principles

1. **Structured.** Every log is a message template with named properties, not a string concatenation. Serilog is used throughout.
2. **Correlated.** Every request has a correlation ID. It is passed to logs, audit records, OCR logs, and returned to the client. The web app shows it on errors so support can trace it.
3. **Privacy-safe.** Consumer reference, meter number, tokens, passwords, photo bytes, and full bill text are never logged. Use masked or hashed identifiers where a correlation is needed (for example, the first 8 characters of a SHA-256 of the user ID).
4. **Complete for decisions.** Every accept, reject, flag, and confirmation of a reading logs the reason.
5. **Level discipline.** Debug for developer detail, Information for business events, Warning for recoverable problems the user sees, Error for failures, never for expected validation outcomes.

### 11.2 Sinks and enrichment

| Environment | Sinks |
|---|---|
| Development | Console (colored), rolling file in `logs/`, Seq optional |
| Staging and Production | Console (JSON), centralized sink (Seq, Elasticsearch, or cloud logging) |

Enrichers: `MachineName`, `Application`, `Environment`, `CorrelationId`, `UserIdHash`, `HouseholdIdHash`, `RequestPath`, `StatusCode`, `ElapsedMs`.

Retention: application logs 30 days. Audit logs 2 years (stored in the database, see 11.4).

### 11.3 Event catalogue

Every event has a stable name. Logs use these names so searching and alerting are reliable.

| Feature | Event name | Level | Key properties |
|---|---|---|---|
| Auth | `Auth.Registered` | Information | UserIdHash |
| Auth | `Auth.LoginSucceeded` | Information | UserIdHash, IpHash |
| Auth | `Auth.LoginFailed` | Warning | EmailHash, IpHash, Reason |
| Auth | `Auth.RefreshTokenReused` | Warning | UserIdHash, FamilyId |
| Consent | `Consent.Recorded` | Information | UserIdHash, ConsentVersion |
| Consent | `Consent.Withdrawn` | Information | UserIdHash |
| Household | `Household.Created` | Information | HouseholdIdHash, Provider |
| Household | `Household.TargetChanged` | Information | HouseholdIdHash, OldTarget, NewTarget |
| Setup | `Setup.BaselineProposed` | Information | HouseholdIdHash, Source, Confidence |
| Setup | `Setup.BaselineConfirmed` | Information | HouseholdIdHash, Source, ReadingDate |
| Setup | `Setup.BaselineFailed` | Warning | HouseholdIdHash, Reason |
| Upload | `Upload.Received` | Information | UploadId, ContentType, SizeBytes |
| Upload | `Upload.Rejected` | Warning | UploadId, Reason (signature, size, type) |
| Upload | `Upload.Duplicate` | Information | UploadId, Sha256Prefix |
| OCR | `Ocr.Started` | Debug | UploadId, Purpose, Provider |
| OCR | `Ocr.Succeeded` | Information | UploadId, Purpose, Confidence, DurationMs |
| OCR | `Ocr.LowConfidence` | Warning | UploadId, Purpose, Confidence |
| OCR | `Ocr.Failed` | Error | UploadId, Purpose, ProviderErrorCode, DurationMs |
| Reading | `Reading.Accepted` | Information | ReadingId, DailyUnits, DaysElapsed |
| Reading | `Reading.Flagged` | Warning | ReadingId, FlagCode |
| Reading | `Reading.Rejected` | Information | HouseholdIdHash, ErrorCode |
| Reading | `Reading.RolloverConfirmed` | Information | ReadingId, RolloverOffset |
| Reading | `Reading.Corrected` | Information | ReadingId, OldValue, NewValue, ActorRole |
| Dashboard | `Dashboard.Calculated` | Debug | HouseholdIdHash, DurationMs, AlertCount |
| Dashboard | `Dashboard.AlertRaised` | Information | HouseholdIdHash, AlertCode |
| API | `Http.RequestCompleted` | Information | Method, Path (route template), StatusCode, ElapsedMs |
| API | `Http.UnhandledException` | Error | Path, CorrelationId (stack trace) |
| Rate limit | `RateLimit.Exceeded` | Warning | Policy, UserIdHash or IpHash |

Note: `Reading.Rejected` is logged at Information because it is an expected user outcome. Only unexpected failures are Error.

### 11.4 Audit log

The `AuditLog` table records user-visible business actions, separate from application logs:

- Baseline confirmed or changed
- Reading accepted, flagged, rejected, confirmed, corrected
- Target changed
- Consent given or withdrawn
- Login and logout
- Image deleted

Audit rows contain: actor user ID, action, entity type and ID, timestamp (UTC), correlation ID, and a small JSON detail. No personal values beyond the reading value itself, which is needed for the record.

Audit rows are append-only. Corrections create a new row; they never edit the old one.

### 11.5 Frontend logging

- A client logger records user-facing errors with the correlation ID, the page, and the error code.
- It never sends a full request body, a token, or a photo.
- Client logs are sent to `POST /api/v1/logs/client` (batched, rate-limited) and stored with the same event names prefixed `Client.`.
- Failed uploads, OCR timeouts, and validation rejections are logged on the client as events, not just shown on screen.

### 11.6 Logging in code

Pattern used in handlers:

```csharp
using (logger.BeginScope(new Dictionary<string, object> { ["HouseholdIdHash"] = householdHash }))
{
    logger.LogInformation("Reading.Accepted {ReadingId} {DailyUnits} {DaysElapsed}",
        reading.Id, dailyUnits, daysElapsed);
}
```

Rules:
- Use `LoggerMessage` source generators for hot paths (readings, dashboard).
- Never interpolate strings into log messages.
- Each handler logs its outcome once at the boundary. Helpers do not log the same event again.

### 11.7 Monitoring

- Dashboard alerts (in the log platform) for: error rate above 2% over 5 minutes, OCR failure rate above 10% over 15 minutes, refresh-token reuse events, login failure spikes.
- Health endpoint checks database connectivity and OCR provider reachability.

## 12. Infrastructure and Deployment

| Component | Development | Production (initial) |
|---|---|---|
| API | `dotnet run` | Container (Docker), one instance, scale later |
| Web | `next dev` | Container or static host with server functions |
| Database | Local PostgreSQL in Docker | Managed PostgreSQL with daily backups |
| Storage | Local folder behind the storage interface | Private object storage bucket |
| OCR | Stub implementation for tests, Vision for manual testing | Google Cloud Vision |
| Secrets | `dotnet user-secrets` | Environment or secrets manager |
| CI | Build, unit tests, format check | Plus integration tests, migration check, container build |

Migrations run as a separate step during deployment, not on API startup.

## 13. Testing Plan

### 13.1 Unit tests (Domain and Application)

- Daily units and units per day, including gaps.
- Cycle used, days elapsed, days remaining, average per day, allowed per day, projection.
- Reading validation order and each rule in section 6.3.
- Rollover offset calculation.
- Alert thresholds (80%, 90%, over target, projected over target, high daily use).
- Baseline cycle date validation (end after start, length 28 to 35 days).

### 13.2 Integration tests

- EF Core mappings and unique indexes with PostgreSQL in Testcontainers.
- Refresh token rotation and reuse detection.
- Upload pipeline with a stub OCR provider.

### 13.3 API tests

- Envelope shape and correlation ID on success and failure.
- Status codes for each error code.
- Two-user isolation tests on every household endpoint.

### 13.4 Web tests

- Component tests for the reading form and dashboard summary.
- Playwright: register, setup manually, submit three readings, check dashboard numbers.
- Accessibility check on the main pages.

### 13.5 Logging tests

- Each event in 11.3 is emitted with the expected level and properties in its scenario.
- Sensitive values (consumer reference, token, meter number) do not appear in captured logs.

## 14. Development Plan (Module by Module)

| Step | Module | Done when |
|---|---|---|
| 1 | Solution, logging, correlation IDs, health endpoint | Requests show correlation ID in logs and responses |
| 2 | Identity: register, login, refresh, logout, consent | Auth tests pass, rotation and reuse detection tested |
| 3 | Household and target | Create, read, change target; isolation tests pass |
| 4 | Baseline (manual) | Cycle dates validated; dashboard shows zero usage |
| 5 | Readings: submit, validate, list | All rules in 6.3 covered by unit tests |
| 6 | Dashboard calculations and alerts | Worked example in 6.4 reproduces exactly |
| 7 | Photo upload and OCR suggestion | Upload rules tested; OCR failure does not block |
| 8 | Baseline from bill | Proposed values shown and confirmed by user |
| 9 | Web app screens for each step | Playwright happy path passes on 375 px and desktop |

Each step ends with tests, logging check, and a short review before the next starts.

## 15. Key Decisions Summary

1. Modular monolith on .NET 10, not microservices.
2. React with Next.js for the web app; Expo for mobile later.
3. Baseline from bill or manual entry; the user always confirms the final number.
4. Users type the exact reading. OCR only suggests.
5. Cycle dates belong to the baseline. No separate billing-cycle table in MVP.
6. Projections and alerts are computed on request, not stored.
7. Logging uses named events, correlation IDs, and privacy-safe identifiers, with a separate audit table.

## 16. Later Phases (Out of MVP Scope)

- Notifications and daily reminders (push, FCM).
- Reading history beyond the current cycle, and six-month comparison.
- Monthly prediction with confidence bands.
- Protected-consumer eligibility rules (configurable, sourced from official notices).
- Bill analysis and discrepancy detection.
- Tariff configuration with effective dates.
- Mobile app (Expo) with camera capture.

## 17. Open Questions

1. **Cycle dates:** should the user always enter the cycle start and end from the bill, or should the app assume the cycle starts on the baseline date?
2. **Target scope:** is the 200-unit target per meter, or per household?
3. **Language:** English only, or Urdu UI too?
4. **Hosting:** where will the API, database, and storage run? Data residency should be decided before production.
5. **OCR budget:** how many photo uploads per user per day should be allowed?
6. **Protected-consumer rules:** the source documents are needed before the eligibility feature is designed in detail.
