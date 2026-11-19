# Features

What is on each screen, who can use it, and how it compares with a Vanta, Drata, Sprinto, or Oneleet program. "Built" means the screen works against the API and the Pages demo with Harborline data. "Later" means the decision is recorded and the screen does not pretend the capability exists.

## Gap tracker

Source of truth for the Nov 21, 2026 Harborline demo and the remaining competitor gaps. Status is Done, In progress, Not started, or Faked-for-demo. A pull request link is filled when the item merges.

| # | Priority | Item | Status | PR |
| --- | --- | --- | --- | --- |
| 1 | P0 | Live Azure and GCP collectors (Entra ID users and MFA, storage public access, Key Vault or KMS, logging, network rules; GCP IAM, buckets, KMS, audit logs) with tests mapped to controls | Done | https://github.com/cvpeceee/D2DAuditServices/pull/12 |
| 2 | P0 | Framework readiness percent on the dashboard and framework pages, plus a one-page executive PDF | Done | https://github.com/cvpeceee/D2DAuditServices/pull/13 |
| 3 | P0 | Security incident register, employee report on `/me`, and the test "incidents resolved within SLA" | Done | https://github.com/cvpeceee/D2DAuditServices/pull/14 |
| 4 | P0 | Change management from GitHub pull requests and Jira, with the test "production changes are reviewed" | Done | https://github.com/cvpeceee/D2DAuditServices/pull/15 |
| 5 | P0 | Security questionnaire automation with a knowledge base, cited suggestions, review, and export | Faked-for-demo | https://github.com/cvpeceee/D2DAuditServices/pull/16 |
| 6 | P0 | SOC 2 system description drafted from tenant data, editable, and included in the audit package | Done | https://github.com/cvpeceee/D2DAuditServices/pull/17 |
| 7 | P0 | Business continuity: impact analysis, plans, and exercise log linked to controls | Done | https://github.com/cvpeceee/D2DAuditServices/pull/18 |
| 8 | P0 | Live AWS Inspector and GitHub Dependabot ingest when connected, plus pentest report tracking | Done | https://github.com/cvpeceee/D2DAuditServices/pull/19 |
| 9 | P1 | Device agent MVP and MDM import (Jamf, Intune, Kandji) | Done | https://github.com/cvpeceee/D2DAuditServices/pull/20 |
| 10 | P1 | Okta, HRIS, Jira, and Slack integrations, with real Slack and email delivery when configured | Not started | |
| 11 | P1 | PCI DSS 4.0, ISO 42001, NIST CSF 2.0, DORA, NIST AI RMF, EU AI Act, IEC 62443, custom framework upload & What-If Gap Simulator | Done | M9 |
| 12 | P1 | Public REST API keys, OpenAPI (`/openapi/v1.json`), and outbound webhooks | Done | M9 |
| 13 | P1 | Checkr background checks and KnowBe4 training | Not started | |
| 14 | P2 | Multi-entity workspaces inside one customer | Not started | |
| 15 | P2 | Trust Center v2 (real file upload/download, clickwrap NDA, buyer-verifiable Cryptographic Trust Receipts, Trust Center AI Q&A) | Done | M9 |
| 16 | P2 | Grounded AI assistant over product docs and role-scoped tenant data (`IAssistantProvider` + live side panel) | Done | M9 |
| 17 | P2 | Terraform for ECS Fargate, RDS, and S3 Object Lock, with an EU region option | Not started | |
| 18 | P3 | RoPA and DPIA, AI governance registry, phishing simulations, attack surface monitoring, auditor network | Not started | |

Harborline already shows fixture Azure, GCP, Inspector, and Dependabot data. Live Inspector and Dependabot ingest runs only for a connected workspace that is not the example fixture. Sync on Harborline leaves those rows in place.

Roles: Admin, Control owner, Editor, Viewer, Employee, Auditor. Auditor is read-only on customer records and limited to the frameworks on the grant.

## Screens

| Route | What it does | Key actions | Who |
| --- | --- | --- | --- |
| `/login` | Email and password, or the start of a WorkOS redirect. | Sign in. | Everyone |
| `/` | Counts for controls, failing tests, and the signed-in person's work. | Open a failing area. | All workspace roles |
| `/frameworks` | SOC 2, ISO 27001, ISO 27017, ISO 27018, HIPAA, and GDPR as loaded content. | Open a framework. | Auditor sees granted frameworks |
| `/frameworks/:key` | Requirement tree with mapped controls. | Jump to a control. | Same as frameworks |
| `/controls` | Control list, owner, status, mappings, tests. | Assign an owner, create a control, import CSV. | Write: Admin, Control owner, Editor |
| `/tests` | Automated checks from the connected accounts. | Read the failing detail. | Auditor sees in-scope checks |
| `/evidence` | Chain items, source, hash, retention. | Upload a file, verify the chain. | Auditor sees evidence collected inside the audit period |
| `/policies` | Draft, review, approve, acknowledge. | Approve, assign, remind. | Approve is Admin |
| `/risks` | Register, score, treatment, library, approval. | Save, adopt a library risk, export. | Write roles |
| `/soa` | ISO 27001 statement of applicability. | Mark applicability and export. | Write roles |
| `/vendors` | Criticality, questionnaire, decision, residual risk. | Save a review. | Write roles |
| `/people` | Directory, MFA, training, device, background check. | Sync, set status, record a background check. | Write roles |
| `/people/computers` | Serial, OS, owner, encryption, screen lock, antivirus, password manager, auto-update, check-in, MDM or manual. | Add a computer, upload a screenshot as evidence. | Write roles |
| `/vulnerabilities` | By asset, by vulnerability, container CVEs & OSS licenses, baked-in secrets (file & line), SARIF 2.1.0 import, ignored, SLA history. | Ignore with a reason, set SLA days, upload pentests, import SARIF, resolve scan findings. | Write roles |
| `/exposure` | Internet-exposed security groups, NSGs, firewalls, public buckets, and databases with 1-click Terraform HCL and CLI remediation. | Scan snapshots, copy IaC/CLI patches, mark resolved with note. | Write roles |
| `/attack-paths` | Unified multi-hop blast-radius attack graph correlating internet exposure, secrets/CVEs, identity gaps, and PII data stores. | Inspect chokepoint remediation and jump to linked controls/findings. | All workspace roles |
| `/dspm` | Data Security Posture Management (DSPM): PII/PHI/PCI discovery, encryption, residency, and risk scoring across cloud stores. | Scan cloud data stores, register/update store classification. | Write roles |
| `/finops` | Cloud cost optimization correlated directly with compliance control failures (`CFG-01`, `CR-02`, `BC-01`). | Track projected/realized savings, apply or dismiss recommendations. | Write roles |
| `/incidents` | Severity, SLA, timeline, root cause, and a linked control. | Report, update status, record a root cause. | Report: any member. Update: write roles |
| `/changes` | GitHub pull requests, Jira issues, and manual changes, with approval and the deploy they shipped in. | Record, approve, reject, link a deploy, sync. | Write roles record and update. Anyone in the workspace can read. |
| `/questionnaires` | Customer security questionnaires. Suggestions cite a policy, control, or evidence item. A person accepts or edits each answer, then exports CSV. | Upload CSV or XLSX, accept, export. | Write roles upload and review. Anyone in the workspace can read and export. |
| `/system-description` | SOC 2 system description drafted from the workspace. The saved text is `system-description.txt` in the audit package. | Edit, save, draft again. | Write roles edit. Anyone in the workspace can read. |
| `/continuity` | Business impact, continuity and disaster-recovery plans, and exercise results linked to a control. | Add a service, plan, or exercise. | Write roles change records. Anyone in the workspace can read. |
| `/me` | The signed-in person's policy, training, MFA, device, background cards, and incident report. | Complete a card. Report an incident. | The signed-in person |
| `/training` | Annual module and an external course with a certificate upload. | Take the quiz, upload a certificate. | Assigned people |
| `/assets` | Inventory from connections, owners, scope, CSV. | Import and export. | Write roles |
| `/tasks` | Work linked to a control, test, risk, vendor, or policy. | Create and complete. | Write roles create. Owners complete |
| `/access-reviews` | Campaigns, selected apps, decisions, remediation. | Draft, start, decide, close. | Write roles |
| `/auditor` | Engagement, date range, sample requests, findings, in-period evidence. | Set the range, request, accept, export. | Auditor, Admin, Control owner |
| `/trust/:slug` | Public frameworks, control counts, public documents, NDA request. | Request access. | Anyone with the link |
| `/trust-center` | Publish the page, edit documents, approve requests. | Save, approve, deny. | Write roles |
| `/settings` | Members, SSO status, auditor grants, automation rules. | Invite, deactivate, save a rule, run due reviews. | Admin |
| `/integrations` | Connect AWS, Google, Microsoft, GitHub, or load the example fixture. | Connect, collect. | Write roles |
| `/activity` | Sign-in and product actions, including auditor access. | Read. | Admin |

## Background checks

The person drawer records a provider, a link, an uploaded report, a completed date, and an exclusion with a reason. The control test "New hires have a background check" fails while an active person hired in the last 90 days has neither a clear result nor an exclusion with a reason. Saving a check refreshes that test. A new failure can fire the automation rule for a failed test.

## Computers

`/people/computers` lists machines. An agent check-in records encryption, screen lock, antivirus, password manager, and OS update, and marks the row Agent. Jamf, Intune, and Kandji are read when `JAMF_BASE_URL` and `JAMF_TOKEN`, `INTUNE_GRAPH_URL` and `INTUNE_TOKEN`, or `KANDJI_BASE_URL` and `KANDJI_TOKEN` are set. Without those settings sync imports nothing. Harborline keeps its example MDM rows and one agent check-in, HL-AGT-1001, whose screen lock is off. A screenshot upload is stored as evidence.

## Vulnerabilities

Tabs are by vulnerability, by asset, ignored, and SLA history. Each row has a CVE, severity, asset, source (AWS Inspector or GitHub Dependabot), first and last seen, an SLA due date from the workspace windows, and whether a fix is available. Ignore requires a reason and writes a history row. Changing the windows recomputes open due dates and records the change.

## Trust center

`/trust/harborline` is public. It shows frameworks, how many mapped controls are passing, public documents, and NDA documents without the file name. A visitor can request access. `/trust-center` is where an admin publishes the page and approves or denies requests.

## Changes

`/changes` is the production change log. Harborline is seeded with two GitHub pull requests and one Jira issue. CHG-02 is deployed without an approval, so the CM-11 test "production changes are reviewed" fails and names that change. Approving it, or linking a deploy on a change that already has an approval, refreshes the test.

Sync reads merged pull requests when a non-example GitHub App installation is connected. A merged request counts as the deploy, and an approving review by someone other than the author counts as the approval. Jira is read when `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN`, and `JIRA_PROJECT` are set. The feed asks for issues labeled `production-change`. Status Approved, Done, or Deployed counts as approval. Status Deployed also links the deploy. With those settings missing, sync adds nothing. The Harborline Jira row is seed data.

## Vulnerabilities, live ingest, and pentests

`/vulnerabilities` still lists findings by asset, by vulnerability, ignored, and SLA history. Sync asks AWS Inspector and GitHub Dependabot when the matching connection is connected and is not an example fixture. A successful list replaces the open findings from that source. Ignored rows stay. If the call fails, the current rows stay. Harborline's connections are examples, so sync imports nothing and the seeded Inspector and Dependabot rows remain.

A penetration-test upload takes `cve,severity,asset,title` and adds those rows with source Pentest. Harborline includes the 2026 application test and CVE-2024-5535.

## Continuity

`/continuity` lists the business impact analysis, continuity and disaster-recovery plans, and recovery exercises. Each row names a control. Harborline's region failover drill failed its four-hour RTO, so the BC-01 test "Recovery exercises are recorded" fails and names that exercise. A passing exercise does not clear the test while another exercise failed.

## System description

`/system-description` drafts a SOC 2 system description from the company, region, product summary, connected services, people, vendors, control counts, and framework names. A writer edits the text or drafts it again. The audit package writes that text to `system-description.txt`. Harborline opens on the generated draft.

## Questionnaires

`/questionnaires` uploads a CSV or XLSX whose first column, or a column headed `question`, is the question list. The knowledge base is the workspace policies, controls, and recent evidence titles. `IQuestionnaireAssistant` is the boundary. The implementation in this release is deterministic: it scores word overlap and cites the closest item. It does not call a model. That is why the tracker row is Faked-for-demo. A person edits the answer, accepts it, and exports the sheet as CSV. Harborline already has the Northgate questionnaire, with the change-management question still open.

## Automation

Settings holds rules. Triggers are test failed, control not ready, risk created, and review due. Actions are create a Jira issue, or a Slack, Teams, or email notification. `IIssueTracker` and `INotifier` are the boundaries. The implementations in this release return a fake id and store a delivery. They do not call Jira, Slack, Teams, or an email provider.

## Frameworks and custom controls

HIPAA and GDPR are JSON catalogs mapped onto the existing controls. Wording in the files is original and short. The controls page can create a control and map it to a requirement, or import a CSV with `code,title,description,frequency,ownerName,ownerEmail,framework,requirement,coverage`.

## Auditor package

The portal shows the engagement date range and the evidence collected inside it. Auditors do not see evidence outside that window on the evidence page, in the sample picker, or in the package files. The zip adds:

| File | Contents |
| --- | --- |
| `control-mapping.csv` | Control, requirement, framework, coverage |
| `connections.csv` | Provider, name, status, account |
| `vendors.csv` | Vendor review fields |
| `assets.csv` | Asset inventory |
| `infrastructure-access.csv` | Person accounts |
| `version-control.csv` | Repository snapshots |
| `manual-evidence.csv` | Uploads and other non-automated evidence in scope |
| `system-description.txt` | The saved SOC 2 system description, or a fresh draft when none is saved |

The manifest, integrity report, control matrix, and evidence files are unchanged in purpose.

## Parity

| Capability | Status |
| --- | --- |
| SOC 2 and ISO 27001 catalogs | Built |
| ISO 27017 and ISO 27018 catalogs | Built |
| HIPAA and GDPR catalogs mapped to controls | Built |
| Custom controls and CSV import | Built |
| Policies, acknowledgement, reminders | Built |
| Risk register, library, treatment export | Built |
| Vendors, questionnaire, residual risk | Built |
| People, directory sync, leaver access | Built |
| Background checks and the new-hire test | Built |
| Security training | Built |
| Asset inventory | Built |
| Computers from MDM data and manual screenshot | Built |
| Vulnerability list, ignore, SLA | Built |
| Access reviews | Built |
| Automated tests from AWS, Google, Microsoft, GitHub | Built |
| Evidence hash chain and verify | Built |
| Auditor portal, sampling, findings | Built |
| Audit date range and in-period evidence | Built |
| Audit package with the registers above | Built |
| Trust center, public page and NDA requests | Built |
| Automation rules with fake Jira, Slack, Teams, email | Built |
| WorkOS sign-in | Built behind `IIdentityProvider`. Local identity in dev. |
| Real Jira, Slack, Teams, and email delivery | Later |
| Device agent on the laptop | Later. MDM and manual upload cover the screen. |
| Azure and GCP live collectors | Later. The example client includes their fixtures. |
| SCIM deprovisioning | Later. WorkOS can own it. The app records the membership. |
| Assistant that calls a model | Later. The panel explains the command palette. |
| Manufacturing and IoT frameworks | Later |
| Production AWS deploy | Later. Terraform is a placeholder. |
