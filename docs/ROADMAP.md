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

Done. My Tasks at `/me` covers policy acknowledgement, security training, MFA, a device note, and a background check. Completing a card stores evidence and refreshes the matching test. Security training has a built-in annual module (slides, quiz, certificate) and an external LMS link with a certificate upload. The asset inventory is filled from the connections, can be owned or marked out of scope, imports and exports CSV, and is included in the audit package. Tasks link to a control, test, risk, vendor, or policy, and the dashboard shows the signed-in person's work. Settings invites users, sets Admin, Editor, Viewer, Employee, or Auditor, deactivates an account, shows SSO status, and records per-audit auditor grants. The risk register adds a library and approval. Vendors add a questionnaire, a decision, and residual risk. Access reviews can be drafted, limited to selected apps, and closed with a remediation note. People show MFA, background check, training, device, hire and leave dates, and a status override with a reason.

## M6 — Background checks, computers, vulnerabilities, trust, automation, and the auditor package

Done. The person drawer records a background-check provider, link, upload, completed date, and an exclusion with a reason. The test "New hires have a background check" follows from that. Computers at `/people/computers` list serial, OS, owner, encryption, screen lock, antivirus, password manager, auto-update, last check-in, and MDM or manual source, with a screenshot upload. Vulnerabilities at `/vulnerabilities` group by asset, by vulnerability, ignored, and SLA history. The trust center has a public page and an admin page for documents and access requests. Settings automation rules call fake Jira, Slack, Teams, and email implementations. HIPAA and GDPR are framework catalogs mapped to the existing controls, and controls can be created or imported from CSV. The auditor portal filters evidence to the engagement dates and the package adds control mapping, connections, vendors, assets, infrastructure access, version control, and manual evidence.

## M7 — Competitor gaps for the Nov 21 demo

In progress. The ordered list, priority, and status live in the gap tracker in [FEATURES.md](FEATURES.md). P0 is the Harborline walkthrough: live Azure and GCP collection, per-framework readiness and an executive PDF, incidents, change management, questionnaire automation, the SOC 2 system description, business continuity, and live vulnerability ingest. P1 through P3 follow that list. Each pull request updates this file, the tracker, and [ARCHITECTURE.md](ARCHITECTURE.md).

## M4 — Demo polish

Tighten the walkthrough used with a customer. The Nov 21, 2026 demo is a fictional multi-cloud data platform (Harborline).
