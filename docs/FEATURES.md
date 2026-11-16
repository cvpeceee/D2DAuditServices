# Features

What is on each screen, who can use it, and how it compares with a Vanta, Drata, Sprinto, or Oneleet program. "Built" means the screen works against the API and the Pages demo with Harborline data. "Later" means the decision is recorded and the screen does not pretend the capability exists.

## Gap tracker

Source of truth for the Nov 21, 2026 Harborline demo and the remaining competitor gaps. Status is Done, In progress, Not started, or Faked-for-demo. A pull request link is filled when the item merges.

| # | Priority | Item | Status | PR |
| --- | --- | --- | --- | --- |
| 1 | P0 | Live Azure and GCP collectors (Entra ID users and MFA, storage public access, Key Vault or KMS, logging, network rules; GCP IAM, buckets, KMS, audit logs) with tests mapped to controls | Done | https://github.com/cvpeceee/D2DAuditServices/pull/12 |
| 2 | P0 | Framework readiness percent on the dashboard and framework pages, plus a one-page executive PDF | Not started | |
| 3 | P0 | Security incident register, employee report on `/me`, and the test "incidents resolved within SLA" | Not started | |
| 4 | P0 | Change management from GitHub pull requests and Jira, with the test "production changes are reviewed" | Not started | |
| 5 | P0 | Security questionnaire automation with a knowledge base, cited suggestions, review, and export | Not started | |
| 6 | P0 | SOC 2 system description drafted from tenant data, editable, and included in the audit package | Not started | |
| 7 | P0 | Business continuity: impact analysis, plans, and exercise log linked to controls | Not started | |
| 8 | P0 | Live AWS Inspector and GitHub Dependabot ingest when connected, plus pentest report tracking | Not started | |
| 9 | P1 | Device agent MVP and MDM import (Jamf, Intune, Kandji) | Not started | |
| 10 | P1 | Okta, HRIS, Jira, and Slack integrations, with real Slack and email delivery when configured | Not started | |
| 11 | P1 | PCI DSS 4.0, ISO 42001, NIST CSF 2.0, DORA, FedRAMP Moderate, HITRUST, CMMC, and custom framework upload | Not started | |
| 12 | P1 | Public REST API with API keys, OpenAPI, and webhooks | Not started | |
| 13 | P1 | Checkr background checks and KnowBe4 training | Not started | |
| 14 | P2 | Multi-entity workspaces inside one customer | Not started | |
| 15 | P2 | Trust Center badges, browser extension screenshots, evidence comments, frozen samples, common-control map | Not started | |
| 16 | P2 | AI assistant over product docs and tenant data | Not started | |
| 17 | P2 | Terraform for ECS Fargate, RDS, and S3 Object Lock, with an EU region option | Not started | |
| 18 | P3 | RoPA and DPIA, AI governance registry, phishing simulations, attack surface monitoring, auditor network | Not started | |

Harborline already shows fixture Azure, GCP, Inspector, and Dependabot data from M4 and M6. Those rows stay Not started until a connected workspace collects or ingests live data, or the screen is explicitly marked Faked-for-demo.

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
| `/vulnerabilities` | By asset, by vulnerability, ignored, SLA history. | Ignore with a reason, set SLA days. | Write roles |
| `/me` | The signed-in person's policy, training, MFA, device, and background cards. | Complete a card. Evidence is stored. | The signed-in person |
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

`/people/computers` lists machines. MDM rows come from the example device list. Manual rows are entered on the form. A screenshot upload is stored as evidence. A read-only device agent is still later. The screen does not invent agent telemetry beyond the seeded MDM rows and the file the user uploads.

## Vulnerabilities

Tabs are by vulnerability, by asset, ignored, and SLA history. Each row has a CVE, severity, asset, source (AWS Inspector or GitHub Dependabot), first and last seen, an SLA due date from the workspace windows, and whether a fix is available. Ignore requires a reason and writes a history row. Changing the windows recomputes open due dates and records the change.

## Trust center

`/trust/harborline` is public. It shows frameworks, how many mapped controls are passing, public documents, and NDA documents without the file name. A visitor can request access. `/trust-center` is where an admin publishes the page and approves or denies requests.

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
