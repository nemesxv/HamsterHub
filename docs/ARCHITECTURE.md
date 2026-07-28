# HamsterHub architecture

## Stack

- ASP.NET Core MVC on .NET 10
- Razor views
- ASP.NET Identity with `ApplicationUser`
- Entity Framework Core 10
- SQL Server LocalDB for local development
- Bootstrap utilities plus project-owned CSS and JavaScript
- xUnit v3 tests with SQLite in-memory relational test databases

## Domain model

### ApplicationUser

Extends the Identity user with `DisplayName`, optional `ProfilePhotoPath`,
`CreatedAt`, and `IsActive`. Authentication credentials remain entirely owned
by ASP.NET Identity.

### Household

The security and ownership boundary for a family. It owns members, pets, and
care tasks.

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

A parent-configured care action linked to exactly one pet, one active household
member, and one category, with a non-negative point value, frequency, and
active state. It may store an optional custom image path. When absent,
presentation resolves a built-in category illustration or a pet-photo fallback
without persisting the derived path.

### CareLog

An immutable historical record connecting a pet, care task, completing user,
completion time, awarded points, approval status, optional approving user, and
the point total immediately after approval.

### CareLogPhoto

An optional, append-only completion image linked to one care log. A care log
may have zero to eight uploaded photos in the current UI. Paths are stored in
the database while files remain under the web root in local development.

## Important invariants

- A care task, its selected pet, and any custom category must belong to the
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
clock. The dashboard controller remains responsible for HTTP behavior,
household-scoped database queries, transactions, and file handling.

The authenticated dashboard checks both the global Identity role and the active
household membership. Task submissions verify that the completing user is the
task assignee and that the member, pet, and care task belong to the same
household. Daily tasks can be submitted once per UTC calendar day and weekly
tasks once per rolling seven-day period; this should become household-time-zone
aware when household settings are introduced.

The Identity account-management area requires the `Parent` role. Children can
sign in, use their care dashboard, and sign out but cannot directly change
account settings in the early product.

The last selected pet and task assignee are convenience-only ASP.NET Session
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

## Automated testing

`HamsterHub.Tests` contains fast unit tests for care-log policy and controller
role routing plus SQLite-backed component tests for dashboard care-log flows
and relational tests for the EF Core model. SQLite tests use a fresh, kept-open
in-memory database per test so foreign keys, unique indexes, check constraints,
transactions, and cascades are exercised.

SQLite is not treated as proof of SQL Server-specific behavior. Provider
differences such as collations, `DateTimeOffset` translation, and concurrent
serializable approvals should receive LocalDB or production-provider integration
coverage when that test tier is introduced.
