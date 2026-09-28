# HamsterHub project context

## Product vision

HamsterHub is a family web application that helps a child care for a real
animal, initially focused on hamsters. The child records real actions such as
feeding, refreshing water, cleaning, playing, and health checks. Completed
actions earn care points. Parents define safe household rewards that children
can request with those points.

The core product loop is:

1. Care for the pet in real life.
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
- Every care task is assigned to one pet, one active family member, and one
  reusable category.
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
- The task form remembers the last selected pet and family member for the
  current browser session.
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
  back to the pet image.
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
