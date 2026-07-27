# HamsterHub architecture

## Stack

- ASP.NET Core MVC on .NET 10
- Razor views
- ASP.NET Identity with `ApplicationUser`
- Entity Framework Core 10
- SQL Server LocalDB for local development
- Bootstrap utilities plus project-owned CSS and JavaScript

## Domain model

### ApplicationUser

Extends the Identity user with `DisplayName`, `CreatedAt`, and `IsActive`.
Authentication credentials remain entirely owned by ASP.NET Identity.

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
active state.

### CareLog

An immutable historical record connecting a pet, care task, completing user,
completion time, awarded points, approval status, optional approving user, and
the point total immediately after approval.

## Important invariants

- A care task, its selected pet, and any custom category must belong to the
  same household.
- A care task's assigned member must be an active member of that household.
- The completing child must be an active member of that household.
- The approving user must be an active parent in that household.
- Point values cannot be negative.
- Historical `PointsAwarded` values are not recalculated.
- `PointsTotalAfterApproval` is assigned inside a serializable approval
  transaction and is not recalculated later.
- Child-to-child history requires both viewer permission and owner sharing
  permission. Only approved records are exposed; parents retain full access.
- Normal record removal should use `IsActive` where history must be retained.

Cross-record household invariants must be checked in application logic, ideally
in a dedicated service layer, because simple database constraints cannot express
all of them safely.

The authenticated dashboard checks both the global Identity role and the active
household membership. Child task submissions verify that the child, pet, and
care task all belong to the same household. Daily tasks can be submitted once
per UTC calendar day and weekly tasks once per rolling seven-day period; this
should become household-time-zone aware when household settings are introduced.

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

## Local database

The connection name is `DefaultConnection` and points to:

```text
Server=(localdb)\MSSQLLocalDB;Database=HamsterHub
```

Apply committed migrations with:

```powershell
dotnet ef database update --project .\HamsterHub\HamsterHub.csproj
```
