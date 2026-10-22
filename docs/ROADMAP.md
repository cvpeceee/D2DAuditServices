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

This milestone. One fictional client, Harborline, is seeded the same way in the development database and the Pages demo. The workspace shows people, integrations, six months of test history, evidence, policies, risks, vendors, an open access review, the statement of applicability, an active audit, activity, and notifications.

## M4 — Demo polish

Tighten the walkthrough used with a customer.
