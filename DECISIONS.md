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

Next: tenant provisioning and migrations, then middleware, then UI.
