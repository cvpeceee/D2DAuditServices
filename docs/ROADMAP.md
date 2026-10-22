# Roadmap

The customer demo is the constraint. Each milestone is a working slice. Security and the data model are not deferred.

## M1 — Foundation

Done. Monorepo, local Docker Compose, CI, control-plane and per-tenant databases, WorkOS behind `IIdentityProvider` (with a development-only local identity mode), SOC 2 and ISO 27001:2022 Annex A as data, the app shell (login, dashboard, frameworks, controls), an example workspace, and an auditor role that can only read its granted scope.

## M2 — Integrations, automated tests, and evidence

Done. Connect AWS, Google Workspace, Microsoft 365, and GitHub. Run automated tests from those connections. Store evidence as Parquet with a SHA-256 hash chain. The device agent stays out until a collector exists.

## M3 — Policies, auditor portal, and the audit package

Done. Policy workflow, the auditor's request and finding loop, and an exportable audit package. Auditor access stays scoped to the grant.

## M4a — Audit-critical gaps

Done. Risk register with scoring and a treatment-plan export, people synced from the directories with leaver access called out, the ISO 27001 statement of applicability, vendor reviews, access-review campaigns stored as evidence, and a readiness view of failing tests and upcoming due items.

## M4b — Realistic example client

Done. One fictional client is seeded the same way in the development database and the Pages demo. The default brand is Harborline, a cloud data platform of about 1,080 people, and the name, domain, and product live in `src/seed/brand.json`. Regenerate with `node src/seed/build.mjs` before a private rename. The workspace shows the workforce (paged), AWS, Azure, and Google Cloud plus the directories and GitHub, six months of test history, evidence, policies, risks, vendors, an open access review, the ISO 27001 statement of applicability, ISO 27017 and ISO 27018 catalogs, an active audit, activity, and notifications.

## M5 — Employee portal, training, assets, tasks, and roles

This milestone. My Tasks at `/me` covers policy acknowledgement, security training, MFA, a device note, and a background check. Completing a card stores evidence and refreshes the matching test. Security training has a built-in annual module (slides, quiz, certificate) and an external LMS link with a certificate upload. The asset inventory is filled from the connections, can be owned or marked out of scope, imports and exports CSV, and is included in the audit package. Tasks link to a control, test, risk, vendor, or policy, and the dashboard shows the signed-in person's work. Settings invites users, sets Admin, Editor, Viewer, Employee, or Auditor, deactivates an account, shows SSO status, and records per-audit auditor grants. The risk register adds a library and approval. Vendors add a questionnaire, a decision, and residual risk. Access reviews can be drafted, limited to selected apps, and closed with a remediation note. People show MFA, background check, training, device, hire and leave dates, and a status override with a reason.

## M4 — Demo polish

Tighten the walkthrough used with a customer.
