"""Generate framework and example-control content. Wording in summaries is ours."""

from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent / "frameworks"


def leaf(code: str, title: str, summary: str) -> dict:
    return {"code": code, "title": title, "summary": summary, "children": []}


def node(code: str, title: str, summary: str, children: list) -> dict:
    return {"code": code, "title": title, "summary": summary, "children": children}


ISO = {
    "A.5.1": ("Policies for information security", "Write the security rules down, have management approve them, and tell people where they live."),
    "A.5.2": ("Information security roles and responsibilities", "Name who protects information, who they report to, and what they are accountable for."),
    "A.5.3": ("Segregation of duties", "Split sensitive work so one person cannot both request and approve it."),
    "A.5.4": ("Management responsibilities", "Managers make sure their teams follow the security rules."),
    "A.5.5": ("Contact with authorities", "Know which regulators and law-enforcement contacts you will call, and when."),
    "A.5.6": ("Contact with special interest groups", "Keep a link to security communities that share warnings relevant to you."),
    "A.5.7": ("Threat intelligence", "Collect useful threat information and use it to adjust your defenses."),
    "A.5.8": ("Information security in project management", "Put security checkpoints into projects, not only into day-to-day operations."),
    "A.5.9": ("Inventory of information and other associated assets", "Keep a list of the information and systems you rely on, and who owns each one."),
    "A.5.10": ("Acceptable use of information and other associated assets", "Tell people what they may and may not do with company systems and data."),
    "A.5.11": ("Return of assets", "Collect equipment and access when someone stops needing them."),
    "A.5.12": ("Classification of information", "Label information by how sensitive it is so people handle it consistently."),
    "A.5.13": ("Labelling of information", "Mark sensitive information in a way people and systems can recognize."),
    "A.5.14": ("Information transfer", "Protect information when it moves between people, systems, or organizations."),
    "A.5.15": ("Access control", "Give people access only to what their work requires."),
    "A.5.16": ("Identity management", "Give each person a unique identity and keep the lifecycle of that identity under control."),
    "A.5.17": ("Authentication information", "Protect passwords, keys, and other secrets people use to prove who they are."),
    "A.5.18": ("Access rights", "Review and adjust access when jobs change, and remove it when it is no longer needed."),
    "A.5.19": ("Information security in supplier relationships", "Expect suppliers who touch your systems or data to meet your security bar."),
    "A.5.20": ("Addressing information security within supplier agreements", "Put the security obligations in the contract, not only in a conversation."),
    "A.5.21": ("Managing information security in the ICT supply chain", "Understand security risk in the chain of technology suppliers behind a service."),
    "A.5.22": ("Monitoring, review and change management of supplier services", "Recheck supplier services when they change, and on a regular schedule."),
    "A.5.23": ("Information security for use of cloud services", "Decide how cloud services are chosen, configured, and left."),
    "A.5.24": ("Information security incident management planning and preparation", "Decide in advance who responds to a security incident and how."),
    "A.5.25": ("Assessment and decision on information security events", "Triage security events so real incidents get a response."),
    "A.5.26": ("Response to information security incidents", "Contain and fix incidents using the plan you prepared."),
    "A.5.27": ("Learning from information security incidents", "Record what an incident taught you and change the system accordingly."),
    "A.5.28": ("Collection of evidence", "When you need evidence, collect it in a way that preserves it."),
    "A.5.29": ("Information security during disruption", "Keep security in place when the business is operating in a degraded mode."),
    "A.5.30": ("ICT readiness for business continuity", "Make sure technology can be recovered in the time the business can tolerate."),
    "A.5.31": ("Legal, statutory, regulatory and contractual requirements", "Know the legal and contractual security duties that apply and track them."),
    "A.5.32": ("Intellectual property rights", "Use software and content within the rights you actually have."),
    "A.5.33": ("Protection of records", "Keep required records readable and protected for as long as you must."),
    "A.5.34": ("Privacy and protection of PII", "Handle personal information in line with the privacy commitments you make."),
    "A.5.35": ("Independent review of information security", "Have someone independent check the security program on a schedule."),
    "A.5.36": ("Compliance with policies, rules and standards for information security", "Check that teams actually follow the security rules you published."),
    "A.5.37": ("Documented operating procedures", "Write down how to operate systems so the work does not live in one person's head."),
    "A.6.1": ("Screening", "Screen people before you trust them with access, at a level that matches the role."),
    "A.6.2": ("Terms and conditions of employment", "Put security responsibilities into the employment terms people agree to."),
    "A.6.3": ("Information security awareness, education and training", "Train people for the security risks in their actual job."),
    "A.6.4": ("Disciplinary process", "Have a fair process for when someone breaks the security rules."),
    "A.6.5": ("Responsibilities after termination or change of employment", "When someone leaves or changes role, end their access and remind them of ongoing duties."),
    "A.6.6": ("Confidentiality or non-disclosure agreements", "Use confidentiality terms with people who will see sensitive information."),
    "A.6.7": ("Remote working", "Set expectations for working outside the office, including how devices are protected."),
    "A.6.8": ("Information security event reporting", "Make it easy and expected for people to report a security concern."),
    "A.7.1": ("Physical security perimeters", "Define the physical boundary around spaces that need protection."),
    "A.7.2": ("Physical entry", "Control and record who enters secure areas."),
    "A.7.3": ("Securing offices, rooms and facilities", "Lock rooms that hold sensitive work, and don't leave them open by habit."),
    "A.7.4": ("Physical security monitoring", "Watch physical spaces for unauthorized access."),
    "A.7.5": ("Protecting against physical and environmental threats", "Plan for fire, water, heat, and similar threats to facilities and equipment."),
    "A.7.6": ("Working in secure areas", "Set extra rules for people working inside a secure area."),
    "A.7.7": ("Clear desk and clear screen", "Lock screens and put sensitive papers away when you step away."),
    "A.7.8": ("Equipment siting and protection", "Place equipment so it is harder to steal, view, or damage."),
    "A.7.9": ("Security of assets off-premises", "Protect equipment and data that leave the building."),
    "A.7.10": ("Storage media", "Control storage media through its life, including when it is reused or sent out."),
    "A.7.11": ("Supporting utilities", "Protect power and other utilities the systems depend on."),
    "A.7.12": ("Cabling security", "Protect network and power cables from tampering and accidental damage."),
    "A.7.13": ("Equipment maintenance", "Maintain equipment so it stays reliable and so maintenance staff don't get open-ended access."),
    "A.7.14": ("Secure disposal or re-use of equipment", "Wipe or destroy storage before equipment is reused or thrown away."),
    "A.8.1": ("User endpoint devices", "Protect laptops and other endpoints that people use for work."),
    "A.8.2": ("Privileged access rights", "Limit administrator access and keep it separate from everyday accounts."),
    "A.8.3": ("Information access restriction", "Restrict access to information and system functions to the people who need them."),
    "A.8.4": ("Access to source code", "Limit who can read and change source code."),
    "A.8.5": ("Secure authentication", "Use authentication that resists guessing, replay, and stolen-password reuse."),
    "A.8.6": ("Capacity management", "Watch capacity so systems keep up with demand."),
    "A.8.7": ("Protection against malware", "Protect systems from malware and know what to do when it is found."),
    "A.8.8": ("Management of technical vulnerabilities", "Find technical vulnerabilities and fix them on a timeline that matches the risk."),
    "A.8.9": ("Configuration management", "Define secure configurations and notice when they drift."),
    "A.8.10": ("Information deletion", "Delete information when there is no reason left to keep it."),
    "A.8.11": ("Data masking", "Hide personal or sensitive data when the real values are not required."),
    "A.8.12": ("Data leakage prevention", "Put measures in place so sensitive data is harder to send to the wrong place."),
    "A.8.13": ("Information backup", "Back up information and prove you can restore it."),
    "A.8.14": ("Redundancy of information processing facilities", "Have enough redundancy for the availability you commit to."),
    "A.8.15": ("Logging", "Log security-relevant activity and keep those logs usable."),
    "A.8.16": ("Monitoring activities", "Watch systems for unusual activity and follow up."),
    "A.8.17": ("Clock synchronization", "Keep system clocks aligned so logs and evidence share one timeline."),
    "A.8.18": ("Use of privileged utility programs", "Restrict tools that can bypass normal security controls."),
    "A.8.19": ("Installation of software on operational systems", "Control what software can be installed on production systems."),
    "A.8.20": ("Networks security", "Protect the network, not only the individual servers."),
    "A.8.21": ("Security of network services", "Know the security features you require from network services."),
    "A.8.22": ("Segregation of networks", "Separate networks when a flat network would spread a failure or an attack."),
    "A.8.23": ("Web filtering", "Reduce exposure to malicious or inappropriate sites from work devices."),
    "A.8.24": ("Use of cryptography", "Use cryptography where it protects confidentiality or integrity, and manage the keys."),
    "A.8.25": ("Secure development life cycle", "Build security into how software is designed, built, and released."),
    "A.8.26": ("Application security requirements", "State the security requirements for an application before it is built."),
    "A.8.27": ("Secure system architecture and engineering principles", "Follow engineering principles that keep systems understandable and defensible."),
    "A.8.28": ("Secure coding", "Write code in a way that avoids the common classes of implementation flaws."),
    "A.8.29": ("Security testing in development and acceptance", "Test security before a change is accepted into production."),
    "A.8.30": ("Outsourced development", "Keep security oversight when someone else writes the code."),
    "A.8.31": ("Separation of development, test and production environments", "Keep production separate from the places where people experiment."),
    "A.8.32": ("Change management", "Approve and record changes to production systems."),
    "A.8.33": ("Test information", "Protect real personal or customer data if it is ever used in a test."),
    "A.8.34": ("Protection of information systems during audit testing", "Plan audit tests so they don't take production down or expose data."),
}

SOC = {
    "CC1.1": ("Integrity and ethical values", "Leadership sets a standard for honest behavior and people can see it in decisions."),
    "CC1.2": ("Board oversight", "The board, or an equivalent, oversees how management handles security risk."),
    "CC1.3": ("Organizational structure", "Reporting lines and responsibilities for security are defined."),
    "CC1.4": ("Commitment to competence", "People who run security-relevant work have the skills the job needs."),
    "CC1.5": ("Accountability", "People are held to the security responsibilities assigned to them."),
    "CC2.1": ("Quality information", "The organization uses information that is good enough to run its controls."),
    "CC2.2": ("Internal communication", "Security responsibilities and issues are communicated inside the organization."),
    "CC2.3": ("External communication", "Customers and other outsiders are told what they need to know about security."),
    "CC3.1": ("Specifies objectives", "Security objectives are clear enough that risks can be judged against them."),
    "CC3.2": ("Identifies and analyzes risk", "The organization identifies security risks and decides how significant they are."),
    "CC3.3": ("Considers fraud risk", "The risk assessment includes fraud and misuse, not only external attack."),
    "CC3.4": ("Identifies significant change", "Changes that would affect security are identified and assessed."),
    "CC4.1": ("Ongoing and separate evaluations", "Controls are evaluated as they run, and by separate reviews."),
    "CC4.2": ("Communicates deficiencies", "Control gaps are reported to the people who can fix them."),
    "CC5.1": ("Selects control activities", "Control activities are chosen to address the risks that were identified."),
    "CC5.2": ("Technology general controls", "Technology controls support the security objectives of the service."),
    "CC5.3": ("Policies and procedures", "Policies say what must happen, and procedures say how it happens."),
    "CC6.1": ("Logical access security", "Logical access to systems and data is restricted to authorized users."),
    "CC6.2": ("Registers and authorizes users", "New access is registered and authorized before it is used."),
    "CC6.3": ("Removes user access", "Access is removed when someone leaves or no longer needs it."),
    "CC6.4": ("Restricts physical access", "Physical access to systems is limited to authorized people."),
    "CC6.5": ("Discontinues access as assets change", "Logical and physical access ends when assets are retired or moved."),
    "CC6.6": ("Protects against external threats", "Boundaries are protected against threats from outside the system."),
    "CC6.7": ("Protects data in transmission", "Data is protected while it moves across networks."),
    "CC6.8": ("Prevents malicious software", "Controls reduce the chance that malicious software is introduced."),
    "CC7.1": ("Detects vulnerabilities and changes", "Configuration changes and vulnerabilities are detected."),
    "CC7.2": ("Monitors for anomalies", "System activity is monitored for behavior that is out of the ordinary."),
    "CC7.3": ("Evaluates security events", "Security events are evaluated so the response matches the event."),
    "CC7.4": ("Responds to incidents", "Identified security incidents get a defined response."),
    "CC7.5": ("Recovers from incidents", "The organization can recover systems after an incident."),
    "CC8.1": ("Manages change", "Changes to infrastructure, software, and procedures are authorized and tested."),
    "CC9.1": ("Mitigates business disruption", "Risks that would disrupt the business are identified and mitigated."),
    "CC9.2": ("Manages vendor risk", "Risks from vendors and partners are assessed and addressed."),
    "A1.1": ("Maintains capacity", "Capacity is managed so commitments to customers can be met."),
    "A1.2": ("Protects environmental and recovery needs", "Facilities, backups, and recovery support the availability commitment."),
    "A1.3": ("Tests recovery", "Recovery procedures are tested."),
}

SOC_GROUPS = [
    ("CC1", "Control environment", "How the organization sets the tone and structure for its controls.", ["CC1.1", "CC1.2", "CC1.3", "CC1.4", "CC1.5"]),
    ("CC2", "Communication and information", "How security information moves to the people who need it.", ["CC2.1", "CC2.2", "CC2.3"]),
    ("CC3", "Risk assessment", "How the organization finds and judges security risk.", ["CC3.1", "CC3.2", "CC3.3", "CC3.4"]),
    ("CC4", "Monitoring activities", "How the organization checks that controls keep working.", ["CC4.1", "CC4.2"]),
    ("CC5", "Control activities", "The policies and activities that carry out the security response.", ["CC5.1", "CC5.2", "CC5.3"]),
    ("CC6", "Logical and physical access", "How access to systems, data, and facilities is limited.", ["CC6.1", "CC6.2", "CC6.3", "CC6.4", "CC6.5", "CC6.6", "CC6.7", "CC6.8"]),
    ("CC7", "System operations", "How the organization detects and responds to operational security events.", ["CC7.1", "CC7.2", "CC7.3", "CC7.4", "CC7.5"]),
    ("CC8", "Change management", "How changes are introduced without losing control.", ["CC8.1"]),
    ("CC9", "Risk mitigation", "How residual business and vendor risk is handled.", ["CC9.1", "CC9.2"]),
]


def children_for(table: dict, codes: list[str]) -> list:
    return [leaf(code, *table[code]) for code in codes]


def iso_group(prefix: str, title: str, summary: str, count: int) -> dict:
    codes = [f"{prefix}.{i}" for i in range(1, count + 1)]
    return node(prefix, title, summary, children_for(ISO, codes))


def control(code, title, description, status, frequency, owner_name, owner_email, mappings, tests):
    return {
        "code": code,
        "title": title,
        "description": description,
        "status": status,
        "frequency": frequency,
        "ownerName": owner_name,
        "ownerEmail": owner_email,
        "mappings": [{"framework": f, "requirement": r, "coverage": c} for f, r, c in mappings],
        "tests": [{"name": n, "kind": k, "outcome": o, "detail": d} for n, k, o, d in tests],
    }


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    soc = {
        "key": "soc2",
        "name": "SOC 2",
        "version": "2017",
        "description": "Trust Services Criteria for Security and Availability. Titles are short public identifiers. Descriptions are our own wording, not the criteria text.",
        "requirements": [
            node(
                "CC",
                "Security",
                "Common criteria that apply to the security of the system.",
                [node(code, title, summary, children_for(SOC, leaves)) for code, title, summary, leaves in SOC_GROUPS],
            ),
            node(
                "A",
                "Availability",
                "Additional criteria for the availability commitment.",
                [
                    node(
                        "A1",
                        "Availability",
                        "Capacity, environmental protection, backup, and recovery testing.",
                        children_for(SOC, ["A1.1", "A1.2", "A1.3"]),
                    )
                ],
            ),
        ],
    }
    iso = {
        "key": "iso27001",
        "name": "ISO 27001",
        "version": "2022",
        "description": "Annex A controls from ISO/IEC 27001:2022. Titles identify the control. Descriptions are our own short guidance, not the standard's text. Clauses 4 to 10 are not part of this seed.",
        "requirements": [
            iso_group("A.5", "Organizational controls", "Controls that sit with the organization rather than a single system.", 37),
            iso_group("A.6", "People controls", "Controls that depend on how people are hired, trained, and offboarded.", 8),
            iso_group("A.7", "Physical controls", "Controls for facilities, equipment, and the spaces people work in.", 14),
            iso_group("A.8", "Technological controls", "Controls implemented in systems, software, and networks.", 34),
        ],
    }
    priya = ("Priya N.", "priya@acme.example")
    marcus = ("Marcus L.", "marcus@acme.example")
    chandra = ("Chandra V.", "chandra@acme.example")
    dev = ("Dev T.", "dev@acme.example")
    people = ("People Ops", "people@acme.example")
    itops = ("IT Ops", "itops@acme.example")
    lena = ("Lena K.", "lena@acme.example")
    ex = "Example data in the Acme Robotics workspace."
    controls = [
        control("GV-01", "Information security policy is approved and published", "Management approves a written security policy and employees can find it.", "Passing", "Annual", *chandra, [("iso27001", "A.5.1", "full"), ("soc2", "CC5.3", "partial")], [("Policy approval record", "document", "pass", ex)]),
        control("GV-02", "Security roles are named and staffed", "Security responsibilities have a named owner.", "Passing", "Annual", *priya, [("iso27001", "A.5.2", "full"), ("soc2", "CC1.3", "partial")], [("Role catalog review", "document", "pass", ex)]),
        control("GV-03", "Segregation of duties for privileged changes", "The person who requests a privileged change is not the only person who approves it.", "Passing", "Continuous", *marcus, [("iso27001", "A.5.3", "full"), ("soc2", "CC5.1", "partial")], [("Privileged change sample", "manualSampling", "pass", ex)]),
        control("GV-04", "Independent review of the security program", "Someone outside the team that runs the controls reviews the program.", "Manual", "Annual", *chandra, [("iso27001", "A.5.35", "full"), ("soc2", "CC4.1", "partial")], [("Annual independent review", "manualSampling", "notRun", "Example data: this year's review has not been sampled.")]),
        control("RM-01", "Risk assessment covers objectives and change", "Security objectives, risks, and significant changes are assessed on a schedule.", "Passing", "Annual", *priya, [("soc2", "CC3.1", "partial"), ("soc2", "CC3.2", "full"), ("soc2", "CC3.4", "partial"), ("iso27001", "A.5.7", "partial")], [("Annual risk assessment", "document", "pass", ex)]),
        control("HR-01", "New hires are screened before access", "People are screened before they receive production access.", "Passing", "Continuous", *people, [("iso27001", "A.6.1", "full"), ("soc2", "CC1.4", "partial")], [("Screening completion check", "attestation", "pass", ex)]),
        control("HR-06", "Security awareness training is completed", "People finish security training for their role.", "Passing", "Annual", *people, [("iso27001", "A.6.3", "full"), ("soc2", "CC1.4", "partial")], [("Training completion", "document", "pass", ex)]),
        control("HR-08", "Leaver access is removed within one business day", "Access ends quickly after someone leaves.", "DueSoon", "Continuous", *priya, [("iso27001", "A.6.5", "full"), ("iso27001", "A.5.11", "partial"), ("soc2", "CC6.3", "full")], [("Leaver sample", "manualSampling", "needsEvidence", "Example data: one leaver sample is still open.")]),
        control("AC-04", "Quarterly user access reviews", "Access is reviewed every quarter and adjusted.", "DueSoon", "Quarterly", *priya, [("iso27001", "A.5.18", "full"), ("soc2", "CC6.2", "full"), ("soc2", "CC6.3", "partial")], [("Quarterly access review", "manualSampling", "needsEvidence", "Example data: the current quarter is not signed yet.")]),
        control("AC-07", "MFA required for all workforce accounts", "Workforce accounts use a second factor.", "Failing", "Continuous", *priya, [("iso27001", "A.5.17", "full"), ("iso27001", "A.8.5", "partial"), ("soc2", "CC6.1", "partial")], [("Identity provider second-factor policy", "automated", "pass", ex), ("Accounts missing a second factor", "automated", "fail", "Example data: 3 of 41 accounts are missing a second factor.")]),
        control("AC-12", "Privileged access is time-limited", "Administrator access expires instead of staying open.", "NeedsEvidence", "Continuous", *priya, [("iso27001", "A.8.2", "full"), ("soc2", "CC6.1", "partial")], [("Privileged access expiry", "attestation", "needsEvidence", "Example data: no current attestation is on file.")]),
        control("AC-15", "Access changes when a role changes", "Moving to a new role replaces access instead of only adding to it.", "Passing", "Continuous", *priya, [("iso27001", "A.5.18", "partial"), ("soc2", "CC6.2", "partial"), ("soc2", "CC6.3", "partial")], [("Role-change sample", "manualSampling", "pass", ex)]),
        control("CR-02", "Encryption at rest for production data", "Production data stores are encrypted at rest.", "Passing", "Continuous", *marcus, [("iso27001", "A.8.24", "partial"), ("soc2", "CC6.1", "partial")], [("Storage encryption configuration", "automated", "pass", ex)]),
        control("CR-05", "Data in transit uses TLS", "Production traffic uses current TLS.", "Passing", "Continuous", *marcus, [("iso27001", "A.5.14", "partial"), ("iso27001", "A.8.24", "partial"), ("soc2", "CC6.7", "full")], [("TLS configuration check", "automated", "pass", ex)]),
        control("CM-11", "Code changes are peer-reviewed before merge", "Production code changes are reviewed by someone other than the author.", "Passing", "Continuous", *dev, [("iso27001", "A.8.4", "partial"), ("iso27001", "A.8.32", "partial"), ("soc2", "CC8.1", "partial")], [("Peer review on protected branches", "automated", "pass", ex)]),
        control("CM-14", "Development, test, and production are separated", "Production is not the place where unreleased changes are tried out.", "Passing", "Continuous", *dev, [("iso27001", "A.8.31", "full"), ("soc2", "CC8.1", "partial")], [("Environment separation", "document", "pass", ex)]),
        control("EP-03", "Laptop disk encryption is enabled", "Company laptops encrypt their disks.", "Passing", "Continuous", *itops, [("iso27001", "A.8.1", "partial"), ("iso27001", "A.7.9", "partial"), ("soc2", "CC6.1", "partial")], [("Disk encryption report", "automated", "pass", ex)]),
        control("EP-08", "Endpoint screen lock is enforced", "Screens lock after a short idle period.", "Passing", "Continuous", *itops, [("iso27001", "A.7.7", "full"), ("iso27001", "A.8.1", "partial"), ("soc2", "CC6.1", "partial")], [("Screen lock policy", "automated", "pass", ex)]),
        control("VM-05", "Vendor security reviews happen annually", "Vendors with access to systems or data are reviewed each year.", "Manual", "Annual", *lena, [("iso27001", "A.5.19", "full"), ("iso27001", "A.5.20", "partial"), ("iso27001", "A.5.22", "partial"), ("soc2", "CC9.2", "full")], [("Vendor review sample", "manualSampling", "notRun", "Example data: the annual sample has not started.")]),
        control("BC-01", "Backups are tested by restore quarterly", "Backups are proven by restoring them, not only by watching a success light.", "Passing", "Quarterly", *marcus, [("iso27001", "A.8.13", "full"), ("iso27001", "A.5.30", "partial"), ("soc2", "A1.2", "full"), ("soc2", "A1.3", "partial")], [("Quarterly restore sample", "manualSampling", "pass", ex)]),
        control("BC-04", "Capacity is reviewed for production systems", "Someone looks at capacity before it becomes an outage.", "Passing", "Quarterly", *marcus, [("iso27001", "A.8.6", "full"), ("soc2", "A1.1", "full")], [("Capacity review", "automated", "pass", ex)]),
        control("IR-02", "Incident response plan is tested yearly", "The incident plan is exercised, not only written.", "NeedsEvidence", "Annual", *chandra, [("iso27001", "A.5.24", "full"), ("iso27001", "A.5.26", "partial"), ("iso27001", "A.5.27", "partial"), ("soc2", "CC7.4", "full"), ("soc2", "CC7.5", "partial")], [("Annual incident exercise", "attestation", "needsEvidence", "Example data: this year's exercise record is missing.")]),
        control("LG-03", "Audit logs are retained at least one year", "Security logs stay available for at least a year.", "Passing", "Continuous", *marcus, [("iso27001", "A.8.15", "full"), ("iso27001", "A.5.33", "partial"), ("soc2", "CC7.2", "partial")], [("Log retention configuration", "automated", "pass", ex)]),
        control("LG-08", "Security alerts are reviewed", "Alerts are looked at before they go stale.", "Failing", "Continuous", *marcus, [("iso27001", "A.8.16", "full"), ("soc2", "CC7.2", "partial"), ("soc2", "CC7.3", "partial")], [("Alert review queue", "automated", "fail", "Example data: the alert queue is older than the sample target.")]),
        control("VM-02", "Vulnerabilities are remediated on a schedule", "Vulnerabilities are fixed within the window the risk calls for.", "Failing", "Continuous", *marcus, [("iso27001", "A.8.8", "full"), ("soc2", "CC7.1", "partial")], [("Vulnerability aging", "automated", "fail", "Example data: one critical finding is past its due date.")]),
        control("PP-01", "Privacy notice is kept current", "The public privacy notice matches how personal information is actually handled.", "Passing", "Annual", *lena, [("iso27001", "A.5.34", "full")], [("Privacy notice review", "document", "pass", ex)]),
        control("PH-02", "Physical entry to production spaces is logged", "Entry to rooms that hold production systems is recorded.", "Passing", "Continuous", *itops, [("iso27001", "A.7.2", "full"), ("soc2", "CC6.4", "full")], [("Entry log sample", "document", "pass", ex)]),
        control("CFG-01", "Production baselines are defined", "Production systems have a baseline configuration to compare against.", "Passing", "Continuous", *dev, [("iso27001", "A.8.9", "full"), ("soc2", "CC7.1", "partial")], [("Baseline drift check", "automated", "pass", ex)]),
        control("MAL-01", "Anti-malware is active on endpoints", "Managed endpoints run anti-malware and report it.", "Passing", "Continuous", *itops, [("iso27001", "A.8.7", "full"), ("soc2", "CC6.8", "full")], [("Anti-malware status", "automated", "pass", ex)]),
    ]
    (OUT / "soc2.json").write_text(json.dumps(soc, indent=2) + "\n", encoding="utf-8")
    (OUT / "iso27001.json").write_text(json.dumps(iso, indent=2) + "\n", encoding="utf-8")
    (OUT / "example-controls.json").write_text(json.dumps({"controls": controls}, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
