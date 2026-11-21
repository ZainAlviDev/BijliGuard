# BijliGuard: Technical Design (Web Application)

Status: Draft v0.1. Scope: the web application first. The mobile app (Phase 5) reuses the same API.

## 1. Purpose

BijliGuard helps Pakistani electricity consumers track daily usage, stay under the configured monthly unit target (default 200), estimate the end-of-cycle bill, and spot possible discrepancies in bills.

Core promise: **one bill upload, one meter photo a day, complete consumption intelligence.**

The MVP does not use any PITC or DISCO API. All data comes from user-uploaded bills and user-submitted meter photos, and the user confirms OCR results when needed.

## 2. System Context

```
 Browser (Next.js web app)        Mobile app (Expo, Phase 5)
          |                                  |
          +-------------- HTTPS / JSON ------+
                             |
                 ASP.NET Core Web API (.NET 10)
                 - Auth (JWT + refresh tokens)
                 - Households, meters, readings
                 - Billing cycles, bills, tariffs
                 - Consumption and prediction engines
                 - Notification dispatch
                             |
        +--------------------+---------------------+
        |                    |                     |
  PostgreSQL          Object storage          Google Cloud Vision
  (all data)          (bill and meter         (OCR, behind an
                       images, private)        OCR service interface)
                                                    |
                                            Firebase Cloud Messaging
                                            (push, Phase 3+)
```

- **Web app:** Next.js (App Router), React, TypeScript, Tailwind CSS, Recharts or Chart.js. Responsive, mobile-first layout.
- **API:** ASP.NET Core Web API on .NET 10, with Clean Architecture. Swagger/OpenAPI is exposed in development.
- **Database:** PostgreSQL via EF Core, with migrations.
- **Files:** Bill and meter images go to private object storage. They are never publicly addressable. Access uses short-lived signed URLs issued by the API.

## 3. Architecture (Backend)

| Project | Responsibility |
|---|---|
| `BijliGuard.Domain` | Entities, value objects, enums, domain rules (for example, the consumption formula). No external dependencies. |
| `BijliGuard.Application` | Use cases, service interfaces (`IOcrService`, `IBillExtractor`, `ITariffCalculator`, `IConsumptionPredictor`, `INotificationSender`), DTOs, FluentValidation validators. |
| `BijliGuard.Infrastructure` | EF Core `DbContext`, repositories, migrations, Vision OCR client, storage client, FCM client, Identity and JWT. |
| `BijliGuard.API` | Controllers (thin: validate, call a service, map the response), auth wiring, rate limiting, Swagger, Serilog. |

Rules:
- Controllers hold no business logic.
- Calculations (consumption, cycles, tariffs, predictions) live in Domain or Application services, never in controllers or in an LLM.
- OCR output is always treated as untrusted input. It is validated before it is stored.
- Each engine is a separate service: OCR, bill extraction, tariff calculation, consumption prediction.

## 4. Web Application: Screens and Flows

### 4.1 Pages

| Route | Purpose | Phase |
|---|---|---|
| `/register`, `/login` | Account creation, login | 1 |
| `/onboarding/provider` | Choose DISCO (LESCO, IESCO, MEPCO, FESCO, GEPCO, HESCO, SEPCO, PESCO, QESCO, K-Electric) and enter the consumer reference number | 1 |
| `/onboarding/bill` | Upload the latest bill (PDF or image). Show OCR results as an editable form. The user confirms, then saves. | 1 |
| `/dashboard` | Consumption overview, meter reading, monthly prediction, chart, notifications, bill summary | 2-3 |
| `/readings` | Reading history. Flags for invalid or duplicate readings. | 2 |
| `/readings/review/:id` | Manual confirmation when OCR confidence is low | 2 |
| `/bills` | Bill list, bill detail, comparison, discrepancy notes | 4 |
| `/settings` | Target (default 200 units), notification preferences, consent, household members | 1-3 |
| `/tariffs` | Admin only: tariff configuration with effective dates | 4 |

### 4.2 Registration and Initial Setup

1. User creates an account (email, password). The password is hashed by ASP.NET Core Identity.
2. User accepts consent for personal electricity-bill processing. The consent record is stored with a timestamp and version.
3. User selects the provider and enters the consumer reference number.
4. User uploads the latest bill. The API stores the image privately and runs OCR and bill extraction.
5. The web app shows the extracted fields (consumer reference, name, meter number, previous and current readings and dates, units, billing period, tariff category, amount). Any field with low confidence is highlighted.
6. User corrects anything wrong and confirms. The API then saves:
   - the `ElectricityMeter`,
   - the `BillingCycle` (start and end dates from the bill),
   - the baseline `MeterReading` (the current reading on the bill, with its date),
   - the `ElectricityBill` and its `BillLineItem`s.
7. Redirect to `/dashboard`.

The bill is required once. It is requested again only when there is no usable baseline or a reading cannot be verified.

### 4.3 Daily Meter Reading (Web Path)

The primary path is the mobile camera. The web app supports the same flow through a file picker or a camera capture on devices that support it.

1. User uploads a meter photo.
2. API stores the image, computes a SHA-256 hash, and rejects exact duplicates.
3. API runs a quality check (blur and brightness heuristics). A blurry image is rejected with a retake prompt.
4. OCR returns the cumulative reading and a confidence score. Every OCR attempt is written to `OCRProcessingLogs`.
5. Validation (section 6) runs against the previous reading and the active billing cycle.
6. If the result is valid and confidence is high, the reading is saved and the dashboard refreshes.
7. If confidence is low or validation raises a flag, the user is shown the photo and the proposed value, and asked to confirm or edit it.

### 4.4 Dashboard

The dashboard is built from the consumption engine's output (section 5). It is not computed in the browser.

- **Consumption overview:** current cycle units, target, remaining units, percentage used, daily average.
- **Meter reading:** latest reading, its date, daily consumption, and a "Scan meter" button (opens the upload dialog on the web).
- **Monthly prediction:** estimated end-of-cycle units, estimated bill, protected-eligibility estimate (always labeled as an estimate with the rule version used).
- **Charts:** daily consumption for the cycle, monthly comparison over at least six cycles, daily-average trend.
- **Notifications:** threshold alerts, high daily usage, invalid readings, new bill analysis.
- **Bills:** latest bill, earlier bills, comparison, and any discrepancy items to verify.

Every API response is scoped to the caller's household. A user never receives another household's data.

## 5. Consumption Engine

### 5.1 Daily consumption

```
Daily Consumption = Current Meter Reading - Previous Meter Reading
```

Example: previous 12,500, current 12,525 gives 25 units. This is pure arithmetic in Domain code and is unit-tested.

### 5.2 Billing cycles

Monthly consumption is counted by the bill's billing-cycle dates, not by the calendar month. A `BillingCycle` has a start date and end date. Every reading is assigned to the cycle that contains its reading date. Readings outside any known cycle are flagged, not guessed.

### 5.3 Metrics

| Metric | Definition |
|---|---|
| Daily consumption | Difference between consecutive valid readings, divided by days between them when readings are not daily (see note) |
| Cycle consumption | Latest valid reading minus the cycle's opening reading |
| Previous cycle consumption | Same, for the prior cycle |
| Average daily consumption | Cycle consumption divided by days elapsed in the cycle |
| Remaining units | Target minus cycle consumption, not below zero for display; the negative value is kept internally for alerts |
| Allowed daily average | Remaining units divided by days remaining in the cycle |
| Projection | Average daily consumption times total cycle days |

Note: if the user skips days, the gap is recorded as a multi-day interval. Daily averages are computed per interval, and the flag "missing days" is set.

## 6. Reading Validation Rules

A reading is accepted only if all applicable checks pass:

| Check | Behavior |
|---|---|
| Lower than previous reading | Rejected as a probable misread. If the difference is very large in the other direction, it is treated as a possible rollover or meter replacement and sent for manual review. |
| Duplicate photo (same hash) | Rejected |
| Duplicate reading (same value and date) | Flagged, not saved twice |
| Implausibly high daily jump | Flagged for review. The threshold is configurable. |
| Reading date in the future, or before the last saved date | Rejected |
| OCR confidence below threshold | Manual confirmation required |
| Meter number on the image differs from the registered meter | Rejected, with a prompt to check the meter |
| Possible rollover or replacement | Requires user confirmation with a reason. A new meter record or a rollover offset is then created. |

Every decision (accepted, rejected, flagged, user-confirmed) is stored with its reason.

## 7. 200-Unit Protection Monitoring

- Target is configurable per household. Default: 200 units per cycle.
- Thresholds: 80% and 90% of target, projected cycle consumption over target, daily usage above the household's average.
- Notification examples:
  - "You have consumed 165 units with 6 days remaining. Your remaining allowance is 35 units. Try to keep consumption below 5.8 units per day."
- Each alert is generated once per threshold per cycle, not on every reading.

### 7.1 Protected-consumer eligibility

Being under 200 units this month does **not** establish protected-consumer status. Eligibility depends on official tariff policy, historical billing periods, and the consumer category.

- Eligibility is computed by a rules engine driven by `TariffConfigurations` and an eligibility rule set. Each rule has an effective start and end date, a version, and a source note.
- The UI always shows the rule version and states that the result is an estimate. The result is not a guarantee.
- Rules are entered by an administrator from official NEPRA and DISCO notifications. They are not hardcoded.

## 8. Electricity Bill Analysis (Phase 4)

1. User uploads a PDF or image bill.
2. Extraction returns line items: previous and current reading, units, billing dates, energy charges, fixed charges, taxes, adjustments, fuel price adjustment, total payable, tariff category.
3. Cross-checks:
   - Units billed vs. units from the meter readings for the same dates.
   - Billing dates vs. known cycle boundaries.
   - Charges recomputed with the tariff active on the billing dates, compared with billed amounts.
4. Each difference is listed as an **item to verify**, with the reason and the figures used. The app never says a bill is fraudulent or definitely wrong. Output wording is "possible discrepancy, verify X."

## 9. Tariff Management

Rates are never hardcoded. A `TariffConfiguration` contains:

- provider, consumer category, protected or unprotected classification,
- unit slabs and per-unit rates,
- fixed charges,
- applicable taxes,
- effective start and end dates.

Old tariffs are kept. A bill is always analyzed against the tariff active on its billing dates. Tariff changes are made only through the admin page, and each change is audited.

## 10. Notifications

| Trigger | Channel |
|---|---|
| Daily reading reminder | Push (FCM, mobile), email or in-app on the web |
| Usage above household daily average | In-app, push |
| 80% and 90% of target | In-app, push |
| Projected cycle usage over target | In-app, push |
| New bill analysis ready | In-app, push |
| Reading validation failure | In-app, push |

Notifications are stored in `Notifications` with read status, and each one is deduplicated per trigger and cycle. Push delivery uses Firebase Cloud Messaging and starts in Phase 3. Until then the web app shows in-app notifications only.

## 11. API Design

- REST, JSON, versioned under `/api/v1`.
- One response envelope for every endpoint:
  ```json
  { "success": true, "data": { }, "error": null, "traceId": "..." }
  ```
  Errors return `success: false` with `error.code`, `error.message`, and `error.details` (validation field errors).
- Correct status codes: 200, 201, 204, 400, 401, 403, 404, 409, 413 (file too large), 422 (validation), 429 (rate limited), 500.

Illustrative endpoints:

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/v1/auth/register` | Create account |
| POST | `/api/v1/auth/login` | Issue access and refresh tokens |
| POST | `/api/v1/auth/refresh` | Rotate refresh token |
| POST | `/api/v1/households` | Create household, select provider, consumer reference |
| POST | `/api/v1/households/{id}/bills` | Upload bill, start extraction (returns a draft) |
| POST | `/api/v1/bills/{id}/confirm` | Confirm extracted bill data, save baseline |
| POST | `/api/v1/meters/{id}/readings` | Upload meter photo, start OCR and validation |
| POST | `/api/v1/readings/{id}/confirm` | User confirms or corrects a flagged reading |
| GET | `/api/v1/households/{id}/dashboard` | Dashboard summary |
| GET | `/api/v1/households/{id}/readings?from=&to=` | Reading history |
| GET | `/api/v1/households/{id}/cycles` | Billing cycles and monthly history |
| GET | `/api/v1/households/{id}/predictions/current` | End-of-cycle estimate |
| GET/PUT | `/api/v1/households/{id}/settings` | Target, thresholds, notification preferences |
| GET | `/api/v1/notifications` | Notifications for the caller |
| GET/POST/PUT | `/api/v1/admin/tariffs` | Tariff configuration (admin role) |

Uploads are asynchronous for OCR. The upload call returns `202 Accepted` with a processing ID, and the web app polls the status endpoint. Upload size is limited and the MIME type is checked by content, not extension.

## 12. Data Model Summary

Entities (details and indexes in the database schema document):

- Identity: `Users`, `RefreshTokens`, `ConsentRecords`
- Household: `Households`, `HouseholdMembers`
- Meters: `ElectricityMeters`, `MeterReadings`, `MeterReadingImages`
- Billing: `BillingCycles`, `ElectricityBills`, `BillLineItems`
- Tariffs: `TariffConfigurations`
- Analytics: `ConsumptionPredictions`
- System: `Notifications`, `OCRProcessingLogs`, `AuditLogs`

Common conventions:
- Primary keys are `uuid`.
- Every table has `created_at` and `updated_at` (UTC).
- Soft deletion (`deleted_at`) on user-owned records. Readings and bills are never hard-deleted once confirmed.
- Every household-owned table has `household_id`, and every query filters by it.

## 13. Security and Privacy

- **Authentication:** ASP.NET Core Identity for password hashing. JWT access tokens (short-lived, 15 minutes). Refresh tokens (7 days) stored hashed, rotated on each use, and revoked on reuse detection.
- **Authorization:** Role-based (`User`, `Admin`). Household-scoped checks on every endpoint, so a user cannot access another household by changing an ID.
- **Images:** Private storage only. Served through short-lived signed URLs. Stripped of EXIF metadata on upload.
- **Consumer reference numbers:** Encrypted at rest (column-level encryption). Shown masked in the UI. Excluded from logs.
- **Input validation:** FluentValidation on all commands. File type and size checked server-side.
- **Rate limiting:** Stricter limits on login, registration, and upload endpoints.
- **Consent:** Required before any bill or meter image is processed. Consent can be withdrawn, and withdrawal stops new processing and triggers deletion of stored images on request.
- **Transport and headers:** HTTPS only, HSTS, CORS restricted to the web app origin, standard security headers in Next.js.
- **Logging:** Serilog with structured logs. Personal data and tokens are never logged. Trace IDs link errors to requests.

## 14. OCR and AI Usage

- OCR is behind `IOcrService`. The first implementation uses Google Cloud Vision. Another provider can be added without changing callers.
- The AI or LLM step, where used, only interprets text and maps fields to a schema. It never performs arithmetic or billing calculations.
- Each OCR run records provider, model version, latency, confidence, and the raw output hash in `OCRProcessingLogs`.
- Extracted fields below the confidence threshold are never saved silently. The user confirms them.

## 15. Web App Technical Structure

```
web/
  app/                  Next.js routes (App Router)
    (auth)/login, register
    onboarding/
    dashboard/
    readings/
    bills/
    settings/
    tariffs/            admin only
  components/           reusable UI (cards, progress bar, charts, upload, forms)
  lib/api/              typed API client, one module per resource
  lib/auth/             token handling, refresh on 401
  lib/format/           number and date formatting (Pakistan locale, PKR)
  types/                shared DTO types, generated or mirrored from OpenAPI
```

- Auth tokens: access token in memory, refresh token in an HttpOnly, Secure, SameSite cookie. Local storage is not used for tokens.
- Data fetching: server components for initial load, client components for interactive parts (upload, charts).
- Forms: validated with the same rules as the API (Zod schemas mirroring FluentValidation).
- Accessibility: keyboard navigation, labels on all inputs, sufficient color contrast, and charts with text summaries.
- Responsive: designed at phone width first, 16px gutter, no horizontal scroll.

## 16. Testing Strategy

| Level | Scope |
|---|---|
| Unit (xUnit) | Daily and monthly consumption, billing-cycle assignment, reading validation, tariff calculation, 200-unit thresholds, predictions |
| Integration | EF Core with PostgreSQL (Testcontainers), repositories, auth flows |
| API | Endpoint tests for status codes, envelope shape, household isolation |
| Web | Component tests (Vitest and Testing Library) for critical flows; Playwright for onboarding and reading upload |
| Security | Authorization tests for cross-household access; upload validation tests |

Calculation tests must pass before any engine is merged.

## 17. Delivery Phases

| Phase | Deliverables |
|---|---|
| 1 | Solution structure, database schema, auth, household and meter setup, bill upload and OCR confirmation |
| 2 | Daily meter capture, OCR reading, validation, consumption calculations, dashboard |
| 3 | 200-unit alerts, monthly predictions, notifications, history |
| 4 | Bill analysis, tariff management, discrepancy detection |
| 5 | Expo mobile app, production deployment, performance and security testing |

The web app's Phase 1 screens are the first thing built. Each module is delivered and tested before the next starts.

## 18. Open Decisions

1. **Hosting and region:** where the API, database, and object storage run. Data residency for personal data should be decided before production.
2. **Object storage provider:** cloud bucket or PostgreSQL-backed storage for the MVP.
3. **OCR cost and quota:** Google Cloud Vision usage per reading is a recurring cost. Decide the image-resize limit and retry policy.
4. **Protected-consumer rules:** the exact rule set must be supplied from official NEPRA and DISCO documents before the eligibility feature is enabled.
5. **Languages:** English only for MVP, or Urdu UI as well.
6. **Household model:** one owner per household, or shared members with different roles.

## 19. Already Set Up in the Repository

The solution skeleton builds cleanly with .NET 10 (`dotnet build`: 0 warnings, 0 errors):

```
C:\My Bill\
  BijliGuard.sln
  docs\TECHNICAL_DESIGN.md
  src\BijliGuard.Domain
  src\BijliGuard.Application
  src\BijliGuard.Infrastructure
  src\BijliGuard.API
  tests\BijliGuard.Domain.Tests
```

Project references follow Clean Architecture: API → Application and Infrastructure; Infrastructure → Application; Application → Domain. The web app (`web/`) has not been created yet.
