# HamsterHub architecture

## Stack

- ASP.NET Core MVC on .NET 10
- Razor views
- ASP.NET Identity with `ApplicationUser`
- Entity Framework Core 10
- SQL Server LocalDB for local development
- Bootstrap utilities plus project-owned CSS and JavaScript
- xUnit v3 tests with SQLite in-memory relational test databases
- .NET MAUI Android app, shared transport contracts, and a platform-independent HTTP client

## Website and mobile boundary

The MVC website and `/api/v1` mobile controllers share one database and Identity
user store. The website uses cookies and antiforgery; the app uses explicitly
selected Identity bearer authentication and device SecureStorage. Mobile requests
validate active users/security stamps and current role/membership ownership.
No EF entities or database access are shipped to the device.

`CareWorkflowService` is the common completion/review transaction boundary for
the website and app. `UploadedImageService` centralizes image validation and file
handling. Existing care, points, and reward policy services stay server-side.
See [MOBILE.md](MOBILE.md) for projects, endpoints, setup, and limitations.

The mobile household API also exposes the parent-managed family, pet, task, image,
and reward operations used by the native management screens. Every operation first
resolves the signed-in user's active membership and then scopes related records to
that household. Reward requests and reviews continue to use `RewardService` and
serializable transactions so website and app balances follow the same rules.

## Domain model

### ApplicationUser

Extends the Identity user with `DisplayName`, optional `ProfilePhotoPath`,
`CreatedAt`, and `IsActive`. Authentication credentials remain entirely owned
by ASP.NET Identity.

### Household

The security and ownership boundary for a family. It owns members, pets, care
tasks, rewards, and reward redemptions. `IsNameCustomized` distinguishes a
parent-chosen household name from the localized default shown in the current
interface language.

### HouseholdMember

Links a user to a household with a `Parent` or `Child` member role. The database
prevents duplicate membership for the same user and household. Two conservative
child privacy flags separately control viewing others' history and sharing the
child's own approved history.

### Pet

Represents a real animal and belongs to one household. It includes the name,
species, optional birth date and photo path, active state, and creation time.

### CareCategory

A reusable kind of care. Built-in categories have a stable localization code
and are shared across households. Parent-created categories contain a Unicode
custom name and belong to one household.

### CareTask

A named family task assigned to one active household member, with optional
`PetId` and `CareCategoryId`, a non-negative point value, frequency, and active
state. Legacy tasks without a stored title retain their localized category name. It may store an optional custom image path. When absent,
presentation resolves a built-in category illustration or a pet-photo fallback
without persisting the derived path.

`CareTask` also stores nullable `ReminderTime`, `ReminderStartDate`, and
`ReminderTimeZoneId`, added by the `TaskReminders` EF migration. `Once` is a new
frequency value; its recurrence cutoff covers all prior reports for the assignee.
The shared `ReminderSchedule` utility validates and calculates calendar occurrences
using the stored zone. MVC and mobile management use the same reminder policy.
The child-only `/api/v1/memberships/{memberId}/reminders` projection includes only
active, authorized assignments and carries a suppression boundary from non-rejected
care reports. It exposes no device tokens or other family members' reminders.

Android stores the selected child and schedule locally, delivers through a private
alarm receiver, restores alarms via boot/update receivers, and uses a persisted
JobScheduler job for authenticated background sync. Bearer tokens remain in
SecureStorage. Sync failures retain the cached schedule; invalid sessions clear it.
There is no FCM dependency or foreground polling timer. Device acceptance for
background delivery, Doze, reboot, permission denial and OEM battery controls is
still required separately from the build and scheduling tests.

### CareLog

An immutable historical record connecting an optional historical pet, task, completing user,
completion time, awarded points, approval status, optional approving user, and
the point total immediately after approval.

### CareLogPhoto

An optional, append-only completion image linked to one care log. A care log
may have zero to eight uploaded photos in the current UI. Paths are stored in
the database while files remain under the web root in local development.

### Reward

A parent-configured household reward with a name, positive point cost, optional
uploaded image, active state, and creation time. Presentation uses a localized
gift placeholder when no image was supplied.

### RewardVisibility

An explicit many-to-many allowlist between a reward and the active child
members who may see and request it. Household ownership and child role are
validated in application logic.

### RewardRedemption

An immutable point debit linked to a household reward and recipient membership.
It snapshots the reward name, image path, and point cost, records the requester,
review decision, and historical net balance after approval. A child request is
pending; a parent-direct purchase is approved immediately.

## Important invariants

- A task, its pet when selected, and any custom category must belong to the
  same household.
- A care task's assigned member must be an active member of that household.
- The completing user must be the active household member assigned to the task.
- Parent completions are approved immediately by that same parent; child
  completions remain pending for parent review.
- The approving user must be an active parent in that household.
- Point values cannot be negative.
- Historical `PointsAwarded` values are not recalculated.
- `PointsTotalAfterApproval` is assigned inside a serializable approval
  transaction and is not recalculated later.
- Current balance is approved `CareLog.PointsAwarded` minus approved
  `RewardRedemption.PointsCost`; pending and rejected records do not affect it.
- Reward request approval and parent-direct purchase re-check affordability in
  a serializable transaction and persist `PointsBalanceAfterApproval`.
- A child can request only an active reward explicitly visible to their active
  membership in the same household. Parents may directly purchase an active
  household reward for any active child in that household.
- Reward names, image paths, and costs are snapshotted on redemption and are not
  recalculated from the live catalog.
- Child-to-child history requires both viewer permission and owner sharing
  permission. Active parents remain visible to children in the same household.
  Child-facing history exposes only approved records; parents retain full access.
- Normal record removal should use `IsActive` where history must be retained.
- Task, member-profile, and completion images accept validated JPG, PNG, or
  WebP files up to 5 MB each. Extension, declared content type, and file
  signature must agree.

Cross-record household invariants must be checked in application logic, ideally
in a dedicated service layer, because simple database constraints cannot express
all of them safely.

`CareLogService` is the domain-policy boundary for care completion and review.
It validates task assignment and household ownership, snapshots the task's
point value into new care logs, applies approval metadata and historical running
totals, and centralizes daily and weekly recurrence cutoffs. It uses the
framework `TimeProvider` so time-sensitive behavior can be tested with a fixed
clock. The dashboard controller remains responsible for MVC behavior and
household-scoped dashboard queries. Care submission/review transactions live in
`CareWorkflowService` and image handling lives in `UploadedImageService`, shared
with the mobile API.

`PointBalanceService` derives live balances from immutable award and redemption
history. `RewardService` enforces visibility, role, household, affordability,
snapshot, and review rules using the framework `TimeProvider`.

The authenticated dashboard checks both the global Identity role and the active
household membership. Task submissions verify that the completing user is the
task assignee and that the member, optional pet, and task belong to the same
household. Daily tasks can be submitted once per UTC calendar day and weekly
tasks once per rolling seven-day period; this should become household-time-zone
aware when household settings are introduced.

The Identity account-management area requires the `Parent` role. Children can
sign in, use their care dashboard, and sign out but cannot directly change
account settings in the early product.

The last selected task assignee is a convenience-only ASP.NET Session
values. They are validated against active household records on every dashboard
load and are never treated as authoritative domain data.

## Localization and presentation

ASP.NET request localization supports `ru` and `en`, with Russian as the
default. Shared strings live in `Resources/SharedResource.*.resx`.

The theme is selected before initial rendering to avoid flashing the wrong
theme. The explicit choice is device-local and stored under
`hamsterhub-theme`.

The child dashboard sets the `kid-world` body class so its picture-first,
art-book presentation remains isolated from the denser parent-management UI.
Meaningful controls retain localized accessible names even when their visible
presentation is primarily photographic or symbolic.

Approved reward deductions are shown as prominent star badges in the child's
reward history. Privacy-permitted family balances use the same large star
language on the right side of friend cards. Parent member history combines care
awards and approved reward deductions into one chronological point timeline.

Pet and care photos open in a shared, keyboard-accessible overlay owned by the
layout. Child camera capture uses `getUserMedia` where available and falls back
to a mobile file input with environment-camera capture. Selected images remain
local previews until the child submits the care-log form.

## Local database

The connection name is `DefaultConnection` and points to:

```text
Server=(localdb)\MSSQLLocalDB;Database=HamsterHub
```

Apply committed migrations with:

```powershell
dotnet ef database update --project .\HamsterHub\HamsterHub.csproj
```

Development startup also applies committed pending migrations automatically so
an existing LocalDB remains compatible after pulling a schema change. Non-
development deployments must run migrations as an explicit release step.

## Automated testing

`HamsterHub.Tests` contains fast unit tests for care-log and reward policy and
controller role routing plus SQLite-backed component tests for dashboard
care-log flows, derived point balances, and relational EF Core behavior. SQLite
tests use a fresh, kept-open in-memory database per test so foreign keys, unique
indexes, check constraints, transactions, and cascades are exercised.

SQLite is not treated as proof of SQL Server-specific behavior. Provider
differences such as collations, `DateTimeOffset` translation, and concurrent
serializable approvals should receive LocalDB or production-provider integration
coverage when that test tier is introduced.


`GeneralFamilyTasks` makes task pet/category links and report pet links nullable,
and adds `CareTask.Name`. Existing rows are preserved. Reports are scoped by their
task household and (when present) historical pet household. They are not filtered
against the task's current pet, so later edits do not erase earned balances.
Recurrence is checked by task and user regardless of pet changes. The down
migration refuses to invent pet/category IDs for general tasks; use the backup
when returning to the previous schema after general tasks have been created.

## Android release discovery and installation

`GET /download/android/version` is anonymous and returns `AndroidReleaseDto` only
when the metadata sidecar and mounted APK match in size/checksum. It has no-store
cache headers. `AndroidUpdateClient` is separate from authenticated household APIs:
it checks release metadata, downloads without tokens/redirects, limits size,
verifies SHA-256, and moves a valid artifact into the cache after a complete download.
Android's `AppUpdateInstaller` checks archive package/version and signing identity,
shares only the private update cache using a dedicated non-exported FileProvider,
and opens the system installer. REQUEST_INSTALL_PACKAGES is declared; Android 8+
requires per-app consent. Startup/resume checks are throttled and optional network
failure leaves the normal app usable. Deployment generates metadata from the signed
APK with `scripts/Write-AndroidRelease.ps1` and verifies/restores APK+metadata together.
