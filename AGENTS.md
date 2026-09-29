# HamsterHub agent guide

HamsterHub is an ASP.NET Core MVC application that helps children build healthy
real-world habits through homework, chores, pet care, parent-managed tasks and points.

Before making product or architectural changes, read:

- `docs/PROJECT_CONTEXT.md` for the product vision, current state, and durable decisions.
- `docs/ARCHITECTURE.md` for the data model and technical boundaries.
- `docs/ROADMAP.md` for planned, active, and completed work.

## Working conventions

- Keep the interface child-friendly while giving parents control of accounts,
  tasks, approvals, and rewards.
- Russian is the default interface language. Every new user-facing string must
  be added to both `Resources/SharedResource.ru.resx` and
  `Resources/SharedResource.en.resx`.
- Support light and dark themes and responsive layouts from 320px phone screens
  through tablets and desktops.
- Keep controllers thin and validate authorization and household ownership on
  every operation involving users, pets, tasks, or care logs.
- Use ASP.NET Identity for authentication and roles. Never implement custom
  password storage.
- Use Entity Framework Core migrations for database changes. Do not edit the
  production schema manually.
- Preserve historical awarded points in `CareLog.PointsAwarded`; changing a
  task's current value must not rewrite history.
- Update the relevant context document when a product decision, architecture
  decision, or roadmap item changes.

## Verification

Run this before handing off code changes:

```powershell
dotnet test .\HamsterHub.slnx --no-restore
dotnet build .\HamsterHub\HamsterHub.csproj --no-restore
```

For UI work, also verify Russian and English, light and dark themes, keyboard
navigation, and phone/tablet layouts.
