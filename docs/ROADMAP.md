# Roadmap

The customer demo is the constraint. Each milestone is a working slice. Security and the data model are not deferred.

## M1 — Foundation

Done. Monorepo, local Docker Compose, CI, control-plane and per-tenant databases, WorkOS behind `IIdentityProvider` (with a development-only local identity mode), SOC 2 and ISO 27001:2022 Annex A as data, the app shell (login, dashboard, frameworks, controls), an example workspace, and an auditor role that can only read its granted scope.

## M2 — Integrations, automated tests, and evidence

Done. Connect AWS, Google Workspace, Microsoft 365, and GitHub. Run automated tests from those connections. Store evidence as Parquet with a SHA-256 hash chain. The device agent stays out until a collector exists.

## M3 — Policies, auditor portal, and the audit package

This milestone. Policy workflow, the auditor's request and finding loop, and an exportable audit package. Auditor access stays scoped to the grant.

## M4 — Demo polish

Tighten the example workspace, empty and error states, and the walkthrough used with a customer. No new product surface that the earlier milestones did not already name.
