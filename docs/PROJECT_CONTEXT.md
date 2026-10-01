# HamsterHub project context

## Product vision

HamsterHub is a family website and Android app for real-world tasks: homework,
cleaning a room, household chores, and pet care. Parents name and assign tasks,
optionally select a reusable category or create one, and optionally link a pet.
Completed tasks earn points toward parent-managed rewards. Existing pet-care
tasks keep their categories, pets, images, and history.

The core product loop is:

1. Complete an assigned task in real life.
2. Record the completed task in HamsterHub.
3. Optionally wait for parent approval.
4. Receive the task's historical point value.
5. Request a visible reward or ask a parent to record it directly.
6. Spend points only after parent approval.

## Users and permissions

- Parents create public accounts and manage the family household.
- Children do not self-register publicly. A parent will create or invite child
  accounts from a protected parent area.
- Parents and children belong to a household.
- Pets, custom care categories, and care tasks belong to a household.
- Every task is assigned to one active family member. Its category and pet are
  optional; the task title is independent of its category.
- A user may only access records belonging to one of their active household
  memberships.

## Current experience

- The early landing page is implemented.
- Login and parent registration are functional modal dialogs.
- New public registrations receive the `Parent` role.
- Russian is the default language, with a persistent English switch.
- Light and dark themes are available; the first visit follows the device
  preference and later uses the saved choice.
- The page includes responsive phone, tablet, and desktop layouts.
- Authenticated parents have a family dashboard for creating family accounts,
  pets, and care tasks and for approving or rejecting completed care.
- The parent dashboard uses a guided, attention-first layout: urgent approvals
  appear before management details, common actions use large visual cards, and
  a simple household summary helps caregivers with varied technical confidence.
- The default family name follows the current interface language. Parents may
  replace it with a custom household name under Account → Personal details.
- Authenticated children have a simplified dashboard for completing care tasks
  and viewing their current points, recent history, and available rewards.
- Parents manage a household reward catalog with a name, positive point cost,
  optional validated image, and an explicit per-child audience.
- Children may request rewards they can see and afford. A parent approves or
  rejects each request, or directly records a reward purchase for a child.
- Pet creation supports an optional birth date and validated photo upload.
- Parents can use built-in localized care categories or create household-specific
  categories in Cyrillic or Latin text.
- Parents can edit and remove family members, pets, and care tasks. Removal is
  archival so existing care history remains valid.
- Family members can have an optional profile photo managed by a parent.
- New task forms default to no pet and no category; the website remembers the
  last task assignee.
- Parents have a unified, localized account area for changing their display
  name, sign-in email, password, and authenticator-based two-step security.
- Parents can open any active family member's care history, including report
  time, approval time, awarded task points, and the member's point total
  immediately after that approval.
- Parent-visible member history is a chronological point timeline that also
  includes approved reward deductions and the balance after each redemption.
- Child history sharing is private by default. Parents independently control
  whether a child may view other children's approved history and whether that
  child's approved history may be shared with other children.
- The child's “Our friends” area shows active parents and privacy-permitted
  children with their current point balances, but no ranking or competitive
  scoreboard. Balances use large star badges for easy recognition. Tapping a
  person opens only their approved care memories and completion photos.
- Parents may complete tasks assigned to themselves. These records are approved
  automatically by that parent and receive the task's current point value.
- Built-in care categories have dedicated hamster illustrations. Parents can
  optionally replace the image for an individual task, and custom tasks fall
  back to a pet image or a neutral checklist illustration.
- Children can report a task without a photo or attach up to eight completion
  photos. Parents see those photos in the approval queue and family history.
- The child dashboard includes a tappable pet gallery, live camera capture,
  multi-photo previews, and an accessible in-page viewer for pet and care
  photos. Photo viewing never requires navigating away from the dashboard.
- Authenticator setup provides both a scannable QR code and a manual setup key.
- Passwords require upper- and lowercase letters and a number; punctuation and
  symbols are optional.
- The child dashboard is designed picture-first for children around ages 3–6,
  with Лилит—a creative 4–5-year-old who loves animals and drawing—as the
  primary design persona. It uses minimal copy, oversized pet imagery, familiar
  symbols, large tap targets, and an art-book visual language.

## Durable product decisions

- The Android phone/tablet app uses .NET MAUI. It shares the existing ASP.NET
  backend, Identity accounts, and database with the website through a versioned
  API. Core business rules stay in common server services.
- The first mobile increment covers login, assigned care tasks, optional photos,
  parent care approvals, care history, and current points. Registration, family
  management, rewards, and account management remain website features initially.
- Mobile UI keeps Russian/English and light/dark parity. The self-hosted preview
  permits HTTP connections so it can use the current direct-IP Docker server;
  no offline write queue is implemented initially.

- The design should feel warm, calm, playful, and trustworthy rather than
  competitive or overstimulating.
- Child-facing screens may be substantially more expressive than parent tools,
  but must remain calm, accessible, gender-inclusive in palette, and usable
  without strong reading skills.
- Care points reward real care and consistency. They are not purchased.
- A care log stores `PointsAwarded` so later task-value changes never alter
  history.
- An approved care log stores `PointsTotalAfterApproval` so the historical net
  balance after that approval remains stable and auditable.
- An approved reward redemption snapshots its name, image path, point cost, and
  `PointsBalanceAfterApproval`; later catalog changes never rewrite history.
- The current points balance is derived from approved care awards minus approved
  reward redemptions; do not add a mutable balance column.
- Pending reward requests do not reserve points. Approval and direct purchase
  re-check the current balance inside a serializable transaction.
- Reward visibility is an explicit per-child allowlist. Parents may directly
  purchase any active household reward for an active child even when that reward
  is not visible in the child's catalog.
- Parent approval states are `Pending`, `Approved`, and `Rejected`.
- Email confirmation is temporarily disabled during early local development
  because no email provider is configured. Enable it before public deployment.
- Russian and English must remain feature-equivalent.
- Child accounts cannot open the Identity account-management area. Parents
  manage family accounts during this early phase.
- Until email invitations exist, parents create family accounts with a
  temporary password and share it in person.

## Working with future chats

This file is the canonical product summary. `AGENTS.md` tells Codex to read it
automatically for future work in this repository. Record new durable decisions
here rather than relying only on conversational memory.

- The website provides a direct Android preview APK download for phones and tablets when the deployment includes the signed app artifact.
- Mobile login defaults to the self-hosted HTTP address, keeps server selection in
  Advanced settings, and asks for an authenticator code only when the API requires it.
- The homelab deployment transaction builds, versions, backs up, and publishes the
  Android APK from the same commit after the website passes its health check.
- The Android app presents separate role-specific dashboards: an attention-first
  parent view for approvals and assigned care, and a picture-first child view for
  tasks, points, waiting items, and recent care. Its palette and hierarchy follow
  the corresponding website dashboards.
- Mobile connections use `http://95.165.103.141:5080/` unless a user explicitly
  saves a different server from Advanced settings.
- The native Android app includes household management rather than requiring a
  parent to return to the website: members, pets, tasks, their pictures, and rewards
  are managed through bearer-authenticated, household-scoped API operations.

## General tasks and media (2026-09-30)

- Task titles support any household activity; pet selection and category selection
  are independent optional fields. Categories can be reused or created inline.
- The app groups language, theme, and logout under Settings.
- Image controls offer Camera and Gallery. Reports accumulate up to eight photos,
  support multi-selection from the gallery, and show removable previews.
- Unit tests and emulator checks were explicitly skipped for this increment.

## Daily-use improvements (2026-09-30)

- The website and API use the same recurrence projection for a child's task:
  daily and weekly work already pending or approved in the current period is
  unavailable for another completion. Rejected work can be attempted again.
- The child website counts only available tasks and shows waiting/done states.
  The Android app refreshes on pull, a visible refresh action, and resume, and
  shows when its dashboard last updated.
- Android history shows signed point changes only for approved records. Pending
  and rejected records show prospective or unawarded amounts separately.
- Parent approval queues precede management content on both surfaces. The
  Android parent count includes care reports and reward requests.
- Android Back returns from Settings, management forms, and task completion to
  the dashboard. Native login links first-time parents to website registration;
  children sign in with credentials created by a parent.
- Android pet birth dates use a calendar with an explicit optional control.
  Task points and reward costs are validated before submission. If a photo
  upload fails after a new management record is created, retrying the form
  reuses that record instead of creating another.

## Task reminders (2026-10-01)

- Parents can configure optional task reminders in website and Android create/edit
  forms. Enabling reminders reveals a time and first date. The first date selects
  the weekday for weekly reminders; daily reminders repeat each day; one-time
  tasks notify once and cannot be completed again after a pending/approved report.
  Unlimited (`AsNeeded`) tasks cannot have scheduled reminders.
- The parent's local time zone is saved with the task. Calendar repetition keeps
  the chosen clock time through daylight-saving changes. Missing times move to the
  first valid minute, and repeated times fire once at the later offset.
- A signed-in child's Android device schedules notifications with AlarmManager,
  independent of the foreground screen, and restores them after reboot/app update.
  Notification permission is required on Android 13+. Optional precise alarm access
  controls exact delivery; otherwise Android may defer the notification.
- Reminders sync when the dashboard refreshes and through an Android periodic job
  (requested every 15 minutes; actual background timing is controlled by Android).
  These are local notifications, not immediate cloud push. A newly created/edited
  task must reach the child's device before its reminder can fire.
- Submitted work suppresses reminders for its current recurrence period. Archived,
  reassigned, disabled, or removed schedules disappear at the next successful sync.
  Signing out or switching to a parent clears the device's schedules immediately.
  Offline devices retain their last successfully synced schedule; missed reminders
  are not replayed. Force-stop blocks delivery until the app is opened again.

## Self-hosted app updates (2026-10-01)

The app checks its configured server for newer APK releases and shows one Update
button, with download progress and Android's installation confirmation. The first
installation enables per-app installation permission; returning from Settings
continues the installer. Accounts and settings remain intact. Release metadata,
artifact checksums and matching package/signing identity protect the update path.
Existing clients require one manual upgrade to receive this updater. Android
real-device installation acceptance is still pending.
