# Decisions

1. **v1 is IT compliance for the company under audit.** The first frameworks are ISO 27001 and SOC 2. Manufacturing (ISO 9001, IATF 16949, and related standards) and IoT/OT (IEC 62443, ETSI EN 303 645) come later on the same core, so the data model and evidence pipeline stay domain-agnostic from the start.

2. **Frameworks are data, not code.** A framework is a versioned tree of requirements, authored as YAML or JSON, with controls defined once and mapped many-to-many onto requirements (including partial mappings). Control tests are pluggable (automated, document, attestation, manual sampling), and collectors and evidence types are plugins, so a new framework is content rather than a code change. Stored content is clause numbers, titles, and our own guidance, which keeps copyrighted ISO/IEC wording out of the product.

3. **Cloud accounts connect through one provider interface.** `ICloudProvider` maps each cloud into a shared normalized resource model and keeps the raw payload as evidence, so tests and auditors share one shape without losing the source. AWS, Azure, and GCP are first. Oracle, IBM, and Alibaba use the same interface later.

4. **Each tenant gets its own PostgreSQL database.** A shared control-plane database holds tenants, billing, and routing, and Neon is an acceptable host for the tenant databases. A database per tenant matches the single-tenant database approach used by Drata and makes enterprise sales, data residency, and tenant deletion straightforward, with large or regulated tenants able to move to a dedicated cluster or region.

5. **Raw evidence is Parquet in object storage, queried with DuckDB.** Evidence stays cheap to retain and can be scanned without a standing analytical database. ClickHouse is reserved for the IoT phase, when telemetry volume needs it.

6. **Object storage sits behind `IStorageProvider`.** Implementations are S3, Cloudflare R2, and Google Cloud Storage, so evidence code does not depend on one vendor. Development defaults to Cloudflare R2, with a local S3-compatible server for offline work. Google Drive holds shared working documents; evidence uses this storage interface.

7. **Evidence is tamper-evident from the moment it is collected.** Each item is SHA-256 hashed with its metadata at collection and written to write-once object storage (Object Lock or bucket retention locks). An append-only audit log is hash-chained, and each tenant's chain head is signed and anchored daily. Auditors get a verify action and an integrity report.

8. **Retention is per tenant and can only grow.** Tenants set retention at or above a platform minimum of 3 years. Once a period is locked it can be extended, and it cannot be shortened, so evidence an auditor may still need cannot be dropped.

9. **Encryption keys are per tenant.** A cloud KMS key covers the tenant database, evidence, and any stored credentials, limiting blast radius to one tenant. Customer cloud access uses assumed read-only roles wherever the provider allows it, with stored secrets only as the fallback. Bring-your-own-key is part of the key boundary now and turns on later as an enterprise feature.

10. **Signup and offboarding are durable Temporal workflows.** They create or remove the tenant database, KMS key, and storage prefix, run migrations, and load frameworks, with retry and rollback so a failed run never leaves a half-built tenant. A control-plane registry records region, database location, schema version, key, and status and routes every request; customers choose a US or EU region at signup, with more regions later. Schema changes roll out in waves (internal, canary, all) and pause on errors, using expand/contract steps so the app runs on mixed schema versions. Offboarding exports the data, waits out a grace period, and drops the database; the tenant key stays disabled until the last evidence retention lock expires, then is deleted.

11. **v1 eventing is a simple queue beside Temporal.** Collectors publish evidence events through an `IEventBus`; IT evidence is collected in batches on a schedule, so minutes of latency are acceptable and Kafka and Flink stay deferred. In the IoT and manufacturing phase the same interface swaps in Kafka (or Redpanda) for continuous telemetry and Flink for windowed streaming control checks, leaving collectors and the control engine unchanged.

12. **The core is a modular monolith with separate worker pools.** One service holds strict internal modules for frameworks, controls, evidence, policies, risk, vendors, and audits; collectors and the control engine run in their own worker pools. An API gateway handles SSO (SAML/OIDC) and MFA, resolves the tenant from the registry, and hands business code a connection scoped to that tenant only, and the UI uses the same versioned, OpenAPI-documented public API that customers and auditors get. Collectors are sandboxed plugins (connect, discover, collect) with short-lived read-only credentials and per-integration rate limits and retries. Roles are admin, control owner, employee, and a read-only, time-limited external auditor scoped to the frameworks in audit.

13. **The backend is C# on modern .NET, and the UI is React with TypeScript.** C# matches the team's expertise, gives strong typing for the business logic, and has official AWS, Azure, GCP, and Temporal SDKs, with Entity Framework against per-tenant Postgres. Go is reserved for agents installed on customer devices, because it ships as a single small binary: the laptop and endpoint agent now, and IoT and factory gateway agents later. Python stays optional for later data-science or AI features.

Next: remaining middleware details (auth and SSO, public API), then UI.
