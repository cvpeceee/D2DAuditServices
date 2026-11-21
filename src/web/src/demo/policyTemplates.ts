export const policyTemplates: {
  code: string;
  title: string;
  body: string;
  controlCodes: string[];
}[] = [
  {
    code: "POL-AUP",
    title: "Acceptable use policy",
    body: "Company devices, accounts, and networks are provided for company work.\nLight personal use is fine when it does not slow work, break the law, or put data at risk.\nDo not install software that has not been approved, and do not turn off security tools.\nReport a lost device or a suspicious message to the security owner straight away.",
    controlCodes: ["GV-01", "EP-08"],
  },
  {
    code: "POL-DCL",
    title: "Data classification policy",
    body: "Every data set has an owner who labels it Public, Internal, Confidential, or Restricted.\nThe label decides where the data may be stored and who may see it.\nRestricted data stays in approved production systems and is never copied to a laptop.\nWhen unsure, treat data as Confidential until the owner says otherwise.",
    controlCodes: ["PP-01", "CR-02"],
  },
  {
    code: "POL-RET",
    title: "Data retention policy",
    body: "Each record type has a written retention period and an owner.\nRecords are deleted or anonymised when the period ends, unless a legal hold applies.\nAudit logs are kept for at least one year.\nThe owner checks once a year that deletion jobs are still running.",
    controlCodes: ["LG-03", "PP-01"],
  },
  {
    code: "POL-ENC",
    title: "Encryption policy",
    body: "Production data is encrypted at rest with keys held in a managed key service.\nData moving between systems uses TLS 1.2 or later.\nKeys are rotated on a schedule and access to them is logged.\nHome-made encryption schemes are not used.",
    controlCodes: ["CR-02", "CR-05"],
  },
  {
    code: "POL-VEN",
    title: "Vendor management policy",
    body: "Before a vendor receives company data, the requester records what data is shared and why.\nVendors with access to Confidential data are reviewed before signing and once a year after.\nThe review looks at their security report, breach history, and contract terms.\nA vendor that is no longer used has its access removed and its data returned or deleted.",
    controlCodes: ["VM-05"],
  },
  {
    code: "POL-RSK",
    title: "Risk management policy",
    body: "The company keeps one risk register with an owner for every risk.\nRisks are scored on likelihood and impact and reviewed at least once a year.\nEach risk is treated, accepted with a reason, transferred, or avoided.\nNew systems and major changes are checked for new risks before launch.",
    controlCodes: ["RM-01"],
  },
  {
    code: "POL-SDL",
    title: "Secure development policy",
    body: "Code reaches production only through the main branch after a second person reviews it.\nAutomated checks for tests and known-vulnerable dependencies run on every change.\nSecrets are kept out of source code.\nDevelopment, test, and production environments stay separate.",
    controlCodes: ["CM-11", "CM-14"],
  },
  {
    code: "POL-VUL",
    title: "Vulnerability management policy",
    body: "Systems are scanned for vulnerabilities on a regular schedule.\nFindings are fixed within a set time by severity: critical first, then high, medium, and low.\nA finding that cannot be fixed in time gets a written exception with an end date.\nThe owner reports open findings to leadership every month.",
    controlCodes: ["VM-02"],
  },
  {
    code: "POL-LOG",
    title: "Logging and monitoring policy",
    body: "Production systems send security-relevant logs to a central place.\nLogs are protected from change and kept for at least one year.\nAlerts for suspicious activity go to a named team that reviews them every working day.\nThe review and any follow-up are recorded.",
    controlCodes: ["LG-03", "LG-08"],
  },
  {
    code: "POL-PHY",
    title: "Physical security policy",
    body: "Offices and server rooms are locked and entry is limited to people who need it.\nEntry to production spaces is logged and the log is kept.\nVisitors sign in and are accompanied.\nEquipment leaving the building is approved and tracked.",
    controlCodes: ["PH-02"],
  },
  {
    code: "POL-HRS",
    title: "Human resources security policy",
    body: "New hires are screened before they receive access, as local law allows.\nEveryone completes security awareness training when they join and once a year.\nWhen someone leaves, their access is removed within one business day.\nRole changes trigger a review of what access the person still needs.",
    controlCodes: ["HR-01", "HR-06", "HR-08"],
  },
  {
    code: "POL-AST",
    title: "Asset management policy",
    body: "Laptops, servers, and cloud accounts are recorded in an inventory with an owner.\nLaptops have disk encryption and a screen lock turned on.\nProduction systems follow a written baseline configuration.\nRetired assets are wiped before disposal and removed from the inventory.",
    controlCodes: ["EP-03", "CFG-01"],
  },
  {
    code: "POL-BKP",
    title: "Backup policy",
    body: "Production data is backed up on a schedule that matches how much loss the business can accept.\nBackups are encrypted and kept in a separate location from the source.\nA restore is tested at least once a quarter and the result is recorded.\nFailed backup jobs alert the owning team.",
    controlCodes: ["BC-01"],
  },
  {
    code: "POL-PWD",
    title: "Password and authentication policy",
    body: "Every workforce account uses a second factor.\nPasswords are long, unique per service, and stored in the company password manager.\nPasswords are never shared or sent in chat or email.\nA password is changed straight away if it may have been exposed.",
    controlCodes: ["AC-07"],
  },
  {
    code: "POL-RMT",
    title: "Remote work policy",
    body: "Remote work uses company-managed devices with encryption and a screen lock.\nPublic Wi-Fi is used only with the company VPN or other approved protection.\nScreens are kept out of view of others when handling Confidential data.\nPrinted Confidential material is shredded, not thrown away.",
    controlCodes: ["EP-03", "EP-08"],
  },
  {
    code: "POL-COC",
    title: "Code of conduct",
    body: "People treat colleagues, customers, and partners with respect.\nConflicts of interest are disclosed to a manager.\nCompany information is used only for company purposes.\nConcerns can be raised without fear of retaliation, and every report is looked into.",
    controlCodes: ["GV-02"],
  },
  {
    code: "POL-AIU",
    title: "AI acceptable use policy",
    body: "Only AI tools approved by the security owner may receive company data.\nConfidential and Restricted data is not pasted into public AI tools.\nA person reviews AI output before it is used in code, decisions, or customer messages.\nNew AI uses that affect customers are risk-assessed before launch.",
    controlCodes: ["GV-01"],
  },
];
