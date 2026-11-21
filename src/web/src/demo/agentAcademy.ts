import { company, type DemoWorld } from "./catalog";
import { plainPdf } from "./pdf";

type Actor = { email: string; name: string; role: string };

export type DemoAgentCheckView = {
  key: string;
  title: string;
  controlCode: string;
  pass: boolean;
  detail: string;
  remediation: string[];
};

export type DemoAgentDevice = {
  id: string;
  serial: string;
  hostname: string;
  platform: "windows" | "macos" | "linux";
  operatingSystem: string;
  personEmail: string;
  personName: string;
  agentKind: "dotnet" | "powershell" | "bash";
  agentVersion: string;
  enrolledAt: string;
  lastSeenAt: string | null;
  revoked: boolean;
  secret: string;
  checks: DemoAgentCheckView[];
  history: {
    at: string;
    agentKind: string;
    agentVersion: string;
    passing: number;
    failing: number;
    checks: DemoAgentCheckView[];
  }[];
};

export type DemoEnrollmentToken = {
  id: string;
  raw: string;
  hint: string;
  personEmail: string;
  personName: string;
  createdByEmail: string;
  createdAt: string;
  expiresAt: string;
  usedAt: string | null;
  revokedAt: string | null;
};

type AcademyModuleDef = {
  key: string;
  title: string;
  summary: string;
  minutes: number;
  defaultAudiences: string[];
  requiresFramework: string | null;
  sections: { heading: string; body: string }[];
  questions: { prompt: string; choices: string[]; answer: number; explanation: string }[];
};

type DemoAcademyProgress = {
  moduleKey: string;
  personEmail: string;
  personName: string;
  sectionsCompleted: number;
  attempts: number;
  bestScore: number;
  passedAt: string | null;
  expiresOn: string | null;
  certificateNumber: string | null;
  evidenceId: string | null;
};

type AgentAcademyState = {
  tokens: DemoEnrollmentToken[];
  devices: DemoAgentDevice[];
  assignments: Record<string, string[]>;
  progress: DemoAcademyProgress[];
};

const TENANT_ID_HEX = "a360c8ce09884dfc9722bce45697b553";
const PASS_MARK = 4;
const RENEWAL_MONTHS = 12;
const STALE_AFTER_DAYS = 7;

const CHECK_CATALOG: { key: string; title: string; controlCode: string }[] = [
  { key: "diskEncryption", title: "Disk encryption", controlCode: "EP-03" },
  { key: "screenLock", title: "Screen lock within 15 minutes", controlCode: "EP-08" },
  { key: "antivirus", title: "Anti-malware running", controlCode: "MAL-01" },
  { key: "passwordManager", title: "Password manager installed", controlCode: "AC-07" },
  { key: "osUpdates", title: "Operating system updates", controlCode: "VM-02" },
  { key: "firewall", title: "Host firewall enabled", controlCode: "CFG-01" },
];

export const ACADEMY_MODULES: AcademyModuleDef[] = [
  {
    key: "security-basics",
    title: "Security awareness basics",
    summary: "How everyday habits protect customers, colleagues and the company.",
    minutes: 12,
    defaultAudiences: ["Everyone"],
    requiresFramework: null,
    sections: [
      {
        heading: "Why this matters",
        body: "Most security incidents start with an ordinary moment: a reused password, an unlocked laptop, a rushed click. Attackers target people because people have access.\n- You are part of the security program, not just a user of it.\n- Small habits, repeated every day, stop most real-world attacks.",
      },
      {
        heading: "Protect your devices",
        body: "Your laptop holds credentials, customer data and source code.\n- Keep full-disk encryption on and the screen locked when you step away (Windows+L or Ctrl+Cmd+Q).\n- Install updates within a few days of release.\n- Never disable endpoint protection or the firewall to make something work.",
      },
      {
        heading: "Handle information with care",
        body: "Classify before you share. Public, Internal, Confidential and Restricted data each have rules.\n- Share files through approved tools with named people, not public links.\n- Do not paste customer data into personal accounts, chat tools or unapproved AI services.",
      },
      {
        heading: "Report early",
        body: "Reporting quickly turns a potential breach into a contained event.\n- Report lost devices, suspicious messages and accidental disclosures to security straight away.\n- There is no penalty for reporting a mistake; there is a real cost to hiding one.",
      },
    ],
    questions: [
      {
        prompt:
          "You need to leave your desk for a five-minute coffee. What should you do with your laptop?",
        choices: [
          "Leave it open, five minutes is short",
          "Lock the screen before you walk away",
          "Close the lid only if someone is nearby",
          "Ask a colleague to watch it",
        ],
        answer: 1,
        explanation: "Lock the screen every time you step away, however briefly.",
      },
      {
        prompt:
          "A tool will not install unless you turn off endpoint protection. What is the right step?",
        choices: [
          "Turn it off briefly and turn it back on",
          "Ask IT or security for an approved route",
          "Use a personal laptop instead",
          "Install it in a virtual machine without telling anyone",
        ],
        answer: 1,
        explanation: "Security controls should only be changed through an approved request.",
      },
      {
        prompt: "Which is the best way to share a confidential document with a partner?",
        choices: [
          "A public link from cloud storage",
          "An approved sharing tool restricted to named recipients",
          "Your personal email account",
          "A USB stick posted to them",
        ],
        answer: 1,
        explanation: "Restrict access to named people using approved tools.",
      },
      {
        prompt: "You accidentally emailed a customer list to the wrong person. What first?",
        choices: [
          "Wait to see if they reply",
          "Report it to security immediately",
          "Delete it from your Sent folder",
          "Ask the recipient not to tell anyone",
        ],
        answer: 1,
        explanation: "Early reporting lets the security team contain the disclosure.",
      },
      {
        prompt: "How quickly should operating system security updates normally be installed?",
        choices: [
          "Within a few days of release",
          "Once a year",
          "Only when the laptop feels slow",
          "Never, updates break things",
        ],
        answer: 0,
        explanation: "Prompt patching closes known vulnerabilities before they are exploited.",
      },
    ],
  },
  {
    key: "phishing-social-engineering",
    title: "Phishing and social engineering",
    summary: "Spot and report manipulation by email, chat, phone and in person.",
    minutes: 15,
    defaultAudiences: ["Everyone"],
    requiresFramework: null,
    sections: [
      {
        heading: "How manipulation works",
        body: "Social engineering uses urgency, authority, curiosity or fear to make you act before you think.\n- 'The CEO needs gift cards in the next hour.'\n- 'Your mailbox will be deleted today unless you sign in.'\n- A pause of ten seconds defeats most of these tricks.",
      },
      {
        heading: "Signs of a phishing message",
        body: "Look at the whole message, not only the logo.\n- The sender domain is slightly wrong or the reply-to differs.\n- Links point somewhere unexpected when you hover over them.\n- Requests to bypass process: new bank details, urgent payments, password resets you did not start.",
      },
      {
        heading: "Beyond email",
        body: "Attackers also call, text, use chat tools and even visit offices.\n- Verify callers by calling back on a number you already know.\n- Never approve an MFA prompt you did not trigger; that is an attacker holding your password.\n- Challenge politely when someone without a badge follows you through a door.",
      },
      {
        heading: "Reporting",
        body: "Use the Report phishing button or forward the message to the security team.\n- Do not reply, click or forward the message to colleagues as a warning.\n- If you already clicked or typed a password, say so immediately so the account can be secured.",
      },
    ],
    questions: [
      {
        prompt: "You receive an MFA push you did not request. What do you do?",
        choices: [
          "Approve it to make it stop",
          "Deny it and report it to security",
          "Ignore it",
          "Turn off MFA",
        ],
        answer: 1,
        explanation: "An unexpected prompt means someone may already have your password.",
      },
      {
        prompt:
          "An email from 'the CFO' asks you to urgently change a supplier's bank details. Best response?",
        choices: [
          "Change them, the CFO is senior",
          "Reply to the email to confirm",
          "Verify through a known phone number or approved process",
          "Forward it to the supplier",
        ],
        answer: 2,
        explanation: "Verify payment changes out of band through the established process.",
      },
      {
        prompt: "Which is a strong sign that a link is malicious?",
        choices: [
          "It is underlined",
          "Hovering shows a domain that does not match the sender",
          "It is in blue text",
          "It appears at the bottom of the message",
        ],
        answer: 1,
        explanation: "Mismatched domains are a classic phishing tell.",
      },
      {
        prompt:
          "You clicked a link and typed your password before realising it was fake. What now?",
        choices: [
          "Hope nothing happens",
          "Change your password later this week",
          "Report it immediately so the account can be secured",
          "Clear your browser history",
        ],
        answer: 2,
        explanation: "Speed matters; security can reset sessions and credentials.",
      },
      {
        prompt: "Someone without a badge asks you to hold the office door. What is reasonable?",
        choices: [
          "Let them in, it is polite",
          "Politely direct them to reception to sign in",
          "Ignore them and let the door close in their face",
          "Lend them your badge",
        ],
        answer: 1,
        explanation: "Tailgating is a physical social engineering technique.",
      },
    ],
  },
  {
    key: "passwords-mfa",
    title: "Passwords and multi-factor authentication",
    summary: "Build strong, unique credentials and use MFA everywhere.",
    minutes: 10,
    defaultAudiences: ["Everyone"],
    requiresFramework: null,
    sections: [
      {
        heading: "Use a password manager",
        body: "A password manager remembers long, unique passwords so you do not have to.\n- Use the company-approved password manager for work accounts.\n- Let it generate passwords of at least 16 characters.\n- Protect the vault with a long passphrase you do not use anywhere else.",
      },
      {
        heading: "One account, one password",
        body: "Reused passwords turn one breach into many.\n- Never reuse a work password on a personal site.\n- Change a password straight away if a site you use reports a breach.",
      },
      {
        heading: "Multi-factor authentication",
        body: "MFA blocks most account takeover attempts even when a password leaks.\n- Prefer phishing-resistant factors such as passkeys or security keys.\n- Authenticator apps are the next best choice; SMS codes are the weakest option.\n- Never share codes; no legitimate team will ask for them.",
      },
      {
        heading: "Recovery and sharing",
        body: "Store recovery codes in your password manager, not in a document on your desktop.\n- Shared team credentials belong in a shared vault with access logging, never in chat.",
      },
    ],
    questions: [
      {
        prompt: "What is the main benefit of a password manager?",
        choices: [
          "It makes passwords shorter",
          "It lets you use long, unique passwords for every account",
          "It removes the need for MFA",
          "It shares passwords with IT automatically",
        ],
        answer: 1,
        explanation: "Unique, generated passwords stop credential reuse attacks.",
      },
      {
        prompt: "Which second factor is most resistant to phishing?",
        choices: ["SMS code", "Security key or passkey", "Email link", "Security questions"],
        answer: 1,
        explanation: "Security keys and passkeys are bound to the real website.",
      },
      {
        prompt:
          "A caller from 'IT support' asks for your MFA code to fix your account. What do you do?",
        choices: [
          "Read it out",
          "Refuse, end the call and report it",
          "Send it by chat instead",
          "Give them half the code",
        ],
        answer: 1,
        explanation: "No legitimate team asks for your codes.",
      },
      {
        prompt: "Where should a team's shared service password be kept?",
        choices: [
          "In a pinned chat message",
          "In a shared vault in the password manager",
          "On a sticky note under the keyboard",
          "In a spreadsheet on a shared drive",
        ],
        answer: 1,
        explanation: "Shared vaults control and log access.",
      },
      {
        prompt: "A site you use announces a breach. What should you do?",
        choices: [
          "Nothing, it was their fault",
          "Change that password and anywhere you reused it",
          "Delete your email account",
          "Wait for a second email",
        ],
        answer: 1,
        explanation: "Change exposed passwords immediately, and stop reusing them.",
      },
    ],
  },
  {
    key: "data-privacy",
    title: "Data handling and privacy",
    summary: "Personal data, GDPR principles, HIPAA basics and your daily responsibilities.",
    minutes: 15,
    defaultAudiences: ["Everyone"],
    requiresFramework: null,
    sections: [
      {
        heading: "What counts as personal data",
        body: "Personal data is any information that identifies a person directly or indirectly.\n- Names, emails, IP addresses, device ids and support tickets all count.\n- Special categories, such as health data, need extra protection.",
      },
      {
        heading: "GDPR principles in practice",
        body: "Collect only what is needed, use it only for the stated purpose and keep it only as long as necessary.\n- Data minimisation: do not export whole tables when a few fields will do.\n- Purpose limitation: data collected for support is not automatically available for marketing.\n- Individuals can ask to see, correct or delete their data; route these requests to the privacy team within a day.",
      },
      {
        heading: "HIPAA basics",
        body: "Protected health information (PHI) is health data linked to an individual and handled for a covered entity.\n- Access PHI only when your job requires it (minimum necessary).\n- Never send PHI through unapproved channels or store it on personal devices.\n- Suspected PHI disclosures must be reported at once; breach notification deadlines are strict.",
      },
      {
        heading: "Everyday handling",
        body: "Label files with their classification and store them only in approved systems.\n- Use test data or anonymised extracts for development and demos.\n- Delete local copies when the task is complete.",
      },
    ],
    questions: [
      {
        prompt: "Which of these is personal data?",
        choices: [
          "A customer's IP address",
          "The company's public address",
          "A product price list",
          "An anonymous aggregated count",
        ],
        answer: 0,
        explanation: "An IP address can identify an individual.",
      },
      {
        prompt: "A data subject asks for a copy of their data. What should you do?",
        choices: [
          "Ignore it",
          "Send them the full database",
          "Route it to the privacy team promptly",
          "Ask them to email again next month",
        ],
        answer: 2,
        explanation: "Subject requests have legal deadlines and a defined process.",
      },
      {
        prompt: "What does data minimisation mean?",
        choices: [
          "Compress files to save space",
          "Collect and use only the data needed for the purpose",
          "Delete all data every week",
          "Store data in fewer countries",
        ],
        answer: 1,
        explanation: "Only collect and process what is necessary.",
      },
      {
        prompt: "Under HIPAA, when may you access a patient record?",
        choices: [
          "Whenever you are curious",
          "Only when your job requires it",
          "When a friend asks",
          "Any time during working hours",
        ],
        answer: 1,
        explanation: "The minimum necessary standard limits access to job need.",
      },
      {
        prompt: "You need realistic data for a product demo. What is the best choice?",
        choices: [
          "Copy production customer data",
          "Use anonymised or synthetic data",
          "Screenshot a real customer account",
          "Ask a customer for their password",
        ],
        answer: 1,
        explanation: "Demos and development should use test or anonymised data.",
      },
    ],
  },
  {
    key: "secure-coding",
    title: "Secure coding and the OWASP Top 10",
    summary:
      "Prevent the most common web application vulnerabilities in code you write and review.",
    minutes: 20,
    defaultAudiences: ["Department:Engineering"],
    requiresFramework: null,
    sections: [
      {
        heading: "Broken access control",
        body: "The most common serious flaw is letting a user act on data they do not own.\n- Check authorisation on every request on the server, not only in the UI.\n- Scope every query by tenant and owner; deny by default.",
      },
      {
        heading: "Injection and unsafe input",
        body: "Untrusted input must never be concatenated into queries, commands or templates.\n- Use parameterised queries and ORM bindings.\n- Encode output for its context (HTML, attribute, URL).\n- Validate input against allow-lists and size limits.",
      },
      {
        heading: "Cryptography and secrets",
        body: "Use well-reviewed libraries and platform defaults.\n- Never commit secrets; use a secret store and rotate on exposure.\n- Hash passwords with a slow, salted algorithm and store only hashes of API tokens.\n- Compare secrets with constant-time functions.",
      },
      {
        heading: "Dependencies, configuration and logging",
        body: "Vulnerable and outdated components and security misconfiguration are frequent root causes.\n- Keep dependencies patched and review new packages before adding them.\n- Ship secure defaults: no debug endpoints, strict CORS, least-privilege service accounts.\n- Log security events without logging secrets or personal data.",
      },
      {
        heading: "Server-side request forgery and design",
        body: "Validate outbound URLs against an allow-list and block internal address ranges.\n- Threat-model new features: who could abuse this, and what would they gain?",
      },
    ],
    questions: [
      {
        prompt: "Where must authorisation checks be enforced?",
        choices: [
          "Only in the front-end",
          "On the server for every request",
          "Only on the login page",
          "In the database backup job",
        ],
        answer: 1,
        explanation: "Client-side checks can always be bypassed.",
      },
      {
        prompt: "What is the correct defence against SQL injection?",
        choices: [
          "Escaping quotes by hand",
          "Parameterised queries",
          "Hiding error messages",
          "Using POST instead of GET",
        ],
        answer: 1,
        explanation: "Parameterised queries separate code from data.",
      },
      {
        prompt: "You accidentally committed an API key. What should you do?",
        choices: [
          "Delete it in the next commit",
          "Revoke and rotate the key, then remove it from history",
          "Make the repository private",
          "Nothing, nobody reads old commits",
        ],
        answer: 1,
        explanation: "Treat any committed secret as exposed and rotate it.",
      },
      {
        prompt: "Which is the safest way to compare a presented token with a stored value?",
        choices: [
          "String equality on the raw token",
          "Constant-time comparison of hashes",
          "Comparing the first eight characters",
          "Checking the length only",
        ],
        answer: 1,
        explanation: "Store hashes and compare in constant time to avoid timing leaks.",
      },
      {
        prompt: "A feature fetches a URL supplied by the user. What risk must you mitigate?",
        choices: [
          "Cross-site scripting only",
          "Server-side request forgery",
          "Clickjacking",
          "Password reuse",
        ],
        answer: 1,
        explanation: "Validate destinations to stop SSRF into internal networks.",
      },
    ],
  },
  {
    key: "ai-acceptable-use",
    title: "AI acceptable use",
    summary:
      "Use generative AI tools productively without leaking data or shipping unreviewed output.",
    minutes: 10,
    defaultAudiences: ["Everyone"],
    requiresFramework: null,
    sections: [
      {
        heading: "Approved tools only",
        body: "Use AI tools that the company has approved and configured for business data.\n- Consumer AI services may retain prompts and use them for training.\n- If you are unsure whether a tool is approved, ask before you paste.",
      },
      {
        heading: "What you may share",
        body: "Never paste secrets, credentials, customer personal data or Restricted information into an AI tool unless it is explicitly approved for that classification.\n- Remove names and identifiers from examples.\n- Summarise rather than copy whole documents where possible.",
      },
      {
        heading: "You own the output",
        body: "AI output can be wrong, biased, insecure or infringing.\n- Review generated code with the same care as a colleague's pull request.\n- Check facts, sources and licences before publishing.\n- Disclose AI assistance where policy or customers require it.",
      },
      {
        heading: "Automated decisions",
        body: "Do not use AI to make decisions about people (hiring, performance, eligibility) without human review and approval from legal and privacy.",
      },
    ],
    questions: [
      {
        prompt: "Which is acceptable to paste into an unapproved public chatbot?",
        choices: [
          "A customer support ticket with names",
          "Production database credentials",
          "A generic question with no company data",
          "A Restricted architecture document",
        ],
        answer: 2,
        explanation: "Only share information with no company or personal data in unapproved tools.",
      },
      {
        prompt: "Who is responsible for AI-generated code that you merge?",
        choices: [
          "The AI vendor",
          "Nobody",
          "You, as the author and reviewer",
          "The security team",
        ],
        answer: 2,
        explanation: "You own everything you commit.",
      },
      {
        prompt:
          "An AI tool suggests a confident but unfamiliar statistic for a customer report. What should you do?",
        choices: [
          "Publish it",
          "Verify it against a reliable source first",
          "Round the number",
          "Remove the citation",
        ],
        answer: 1,
        explanation: "Generative models can fabricate facts.",
      },
      {
        prompt: "You are not sure if an AI tool is approved for Confidential data. What next?",
        choices: [
          "Try it with a small sample",
          "Ask the security or IT team before using it",
          "Use it on a personal laptop",
          "Assume it is approved",
        ],
        answer: 1,
        explanation: "Check approval before sharing any business data.",
      },
      {
        prompt: "Can AI alone decide which job applicants are rejected?",
        choices: [
          "Yes, it is faster",
          "Only for junior roles",
          "No, decisions about people need human review and approval",
          "Yes, if the model is accurate",
        ],
        answer: 2,
        explanation: "Automated decisions about people need human oversight.",
      },
    ],
  },
  {
    key: "hipaa-privacy-security",
    title: "HIPAA privacy and security rule essentials",
    summary:
      "PHI safeguards, minimum necessary access and breach reporting for teams handling health data.",
    minutes: 15,
    defaultAudiences: ["Everyone"],
    requiresFramework: "hipaa",
    sections: [
      {
        heading: "PHI and the HIPAA rules",
        body: "The Privacy Rule governs how protected health information is used and disclosed. The Security Rule requires administrative, physical and technical safeguards for electronic PHI.\n- As a business associate we must protect PHI exactly as our customers do.",
      },
      {
        heading: "Minimum necessary",
        body: "Use, disclose and request only the minimum PHI needed for the task.\n- Role-based access limits who can see PHI.\n- Do not browse records out of curiosity; access is logged and reviewed.",
      },
      {
        heading: "Safeguards in daily work",
        body: "Store ePHI only in approved, encrypted systems.\n- Never email PHI outside approved secure channels.\n- Lock screens, use privacy filters in public and shred printed PHI.",
      },
      {
        heading: "Breaches and reporting",
        body: "An impermissible use or disclosure of PHI is presumed to be a breach until a risk assessment shows otherwise.\n- Report suspected incidents to the privacy officer immediately.\n- Notification deadlines run from the date of discovery, so delays matter.",
      },
    ],
    questions: [
      {
        prompt: "What does the HIPAA Security Rule specifically protect?",
        choices: [
          "All company data",
          "Electronic protected health information",
          "Marketing material",
          "Employee payroll",
        ],
        answer: 1,
        explanation: "The Security Rule covers ePHI.",
      },
      {
        prompt: "What is the 'minimum necessary' standard?",
        choices: [
          "Use the minimum password length",
          "Access only the PHI needed for the task",
          "Store PHI for the minimum time of one year",
          "Encrypt only some fields",
        ],
        answer: 1,
        explanation: "Limit PHI use and disclosure to what the task requires.",
      },
      {
        prompt:
          "A colleague asks you to look up a celebrity's record out of interest. What do you do?",
        choices: [
          "Look it up quickly",
          "Refuse; access without a job need is a violation",
          "Ask a manager to do it",
          "Look but do not tell anyone",
        ],
        answer: 1,
        explanation: "Curiosity is never a permitted purpose.",
      },
      {
        prompt: "How should you send PHI to an approved partner?",
        choices: [
          "Regular email",
          "An approved secure, encrypted channel",
          "A personal messaging app",
          "A public file-sharing link",
        ],
        answer: 1,
        explanation: "Only approved secure channels may carry PHI.",
      },
      {
        prompt: "When should a suspected PHI disclosure be reported?",
        choices: [
          "After the investigation is complete",
          "Immediately to the privacy officer",
          "At the next quarterly review",
          "Only if a patient complains",
        ],
        answer: 1,
        explanation: "Deadlines run from discovery, so report at once.",
      },
    ],
  },
];

const stateByWorld = new WeakMap<DemoWorld, AgentAcademyState>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function fail(status: number, code: string) {
  return json({ code }, status);
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function todayStr() {
  return new Date().toISOString().slice(0, 10);
}

function addMonths(dateStr: string, months: number) {
  const date = new Date(`${dateStr}T00:00:00Z`);
  date.setUTCMonth(date.getUTCMonth() + months);
  return date.toISOString().slice(0, 10);
}

function randomHex(length: number) {
  const bytes = new Uint8Array(Math.ceil(length / 2));
  crypto.getRandomValues(bytes);
  return [...bytes]
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("")
    .slice(0, length);
}

async function sha256(text: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function remediationFor(key: string, platform: string): string[] {
  const os = platform.toLowerCase();
  if (key === "diskEncryption" && os === "windows") {
    return [
      "Open Settings > Privacy & security > Device encryption (or BitLocker Drive Encryption).",
      "Turn on BitLocker for the OS drive and save the recovery key to your company account.",
    ];
  }
  if (key === "diskEncryption" && os === "macos") {
    return [
      "Open System Settings > Privacy & Security > FileVault and click Turn On.",
      "Store the recovery key with your company.",
    ];
  }
  if (key === "diskEncryption") {
    return [
      "Confirm LUKS full-disk encryption with 'lsblk -f' (crypto_LUKS) or re-image with the IT encrypted image.",
    ];
  }
  if (key === "screenLock" && os === "windows") {
    return [
      "Open Settings > Accounts > Sign-in options and require sign-in when PC wakes up.",
      "Set screen saver timeout to 15 minutes or less and enable 'On resume, display logon screen'.",
    ];
  }
  if (key === "screenLock" && os === "macos") {
    return [
      "Open System Settings > Lock Screen and set Start Screen Saver when inactive to 15 minutes or less.",
      "Set 'Require password after screen saver begins' to Immediately.",
    ];
  }
  if (key === "screenLock") {
    return [
      "Open Settings > Privacy > Screen Lock and set Blank Screen Delay to 15 minutes or less.",
    ];
  }
  if (key === "antivirus" && os === "windows") {
    return ["Open Windows Security > Virus & threat protection and enable Real-time protection."];
  }
  if (key === "antivirus" && os === "macos") {
    return ["Enable Automatic Security Responses (XProtect) in System Settings > Software Update."];
  }
  if (key === "antivirus") {
    return ["Install and enable ClamAV or the company endpoint protection daemon."];
  }
  if (key === "passwordManager") {
    return [
      "Install the company-approved password manager (1Password, Bitwarden, LastPass, or KeePassXC).",
    ];
  }
  if (key === "osUpdates" && os === "windows") {
    return ["Open Settings > Windows Update, install pending security updates, and restart."];
  }
  if (key === "osUpdates" && os === "macos") {
    return ["Open System Settings > General > Software Update and install pending updates."];
  }
  if (key === "osUpdates") {
    return ["Run 'sudo apt update && sudo apt upgrade' and enable unattended security upgrades."];
  }
  if (key === "firewall" && os === "windows") {
    return ["Turn on Microsoft Defender Firewall for Domain, Private, and Public profiles."];
  }
  if (key === "firewall" && os === "macos") {
    return ["Open System Settings > Network > Firewall and turn it on."];
  }
  return [
    "Enable the host firewall ('sudo ufw enable' or 'sudo systemctl enable --now firewalld').",
  ];
}

function buildChecks(
  platform: string,
  overrides: Partial<Record<string, { pass: boolean; detail: string }>> = {},
): DemoAgentCheckView[] {
  const defaults: Record<string, string> = {
    diskEncryption:
      platform === "windows"
        ? "BitLocker On (AES-256)"
        : platform === "macos"
          ? "FileVault On"
          : "LUKS2 active on root",
    screenLock: "Idle lock set to 10 minutes",
    antivirus:
      platform === "windows"
        ? "Microsoft Defender active"
        : platform === "macos"
          ? "XProtect current"
          : "ClamAV daemon running",
    passwordManager: "1Password installed",
    osUpdates: "Security patches current",
    firewall: "Host firewall enabled",
  };
  return CHECK_CATALOG.map((info) => {
    const custom = overrides[info.key];
    const pass = custom ? custom.pass : true;
    const detail = custom ? custom.detail : (defaults[info.key] ?? "Passing");
    return {
      key: info.key,
      title: info.title,
      controlCode: info.controlCode,
      pass,
      detail,
      remediation: pass ? [] : remediationFor(info.key, platform),
    };
  });
}

function isStale(lastSeenAt: string | null) {
  if (!lastSeenAt) return true;
  return Date.now() - new Date(lastSeenAt).getTime() > STALE_AFTER_DAYS * 86_400_000;
}

function tokenState(token: DemoEnrollmentToken) {
  if (token.revokedAt) return "Revoked";
  if (token.usedAt) return "Used";
  return new Date(token.expiresAt).getTime() <= Date.now() ? "Expired" : "Active";
}

function getState(data: DemoWorld): AgentAcademyState {
  const existing = stateByWorld.get(data);
  if (existing) return existing;

  const now = Date.now();
  const iso = (hoursAgo: number) => new Date(now - hoursAgo * 3_600_000).toISOString();
  const domain = company.domain;

  const chandraChecks = buildChecks("macos");
  const devChecks = buildChecks("macos", {
    screenLock: { pass: false, detail: "Screen lock timeout is 30 minutes (max 15m)" },
  });
  const lenaChecks = buildChecks("windows", {
    diskEncryption: { pass: false, detail: "BitLocker protection suspended on C:" },
    osUpdates: { pass: false, detail: "2 critical Windows security updates pending" },
  });
  const staleLinuxChecks = buildChecks("linux", {
    firewall: { pass: false, detail: "ufw inactive" },
  });

  const devices: DemoAgentDevice[] = [
    {
      id: "4e9b64c1-2f10-4b88-901a-100000000001",
      serial: "HL-AGT-1002",
      hostname: "chandra-mbp-pro",
      platform: "macos",
      operatingSystem: "macOS 15.1",
      personEmail: `chandra.vell@${domain}`,
      personName: "Chandra Vell",
      agentKind: "dotnet",
      agentVersion: "1.0.0",
      enrolledAt: iso(240),
      lastSeenAt: iso(1),
      revoked: false,
      secret: "demo-secret-1",
      checks: chandraChecks,
      history: [
        {
          at: iso(1),
          agentKind: "dotnet",
          agentVersion: "1.0.0",
          passing: 6,
          failing: 0,
          checks: chandraChecks,
        },
        {
          at: iso(7),
          agentKind: "dotnet",
          agentVersion: "1.0.0",
          passing: 6,
          failing: 0,
          checks: chandraChecks,
        },
      ],
    },
    {
      id: "4e9b64c1-2f10-4b88-901a-100000000002",
      serial: "HL-AGT-1001",
      hostname: "dev-macbook",
      platform: "macos",
      operatingSystem: "macOS 15.1",
      personEmail: `dev.patel@${domain}`,
      personName: "Dev Patel",
      agentKind: "bash",
      agentVersion: "1.0.0",
      enrolledAt: iso(180),
      lastSeenAt: iso(2),
      revoked: false,
      secret: "demo-secret-2",
      checks: devChecks,
      history: [
        {
          at: iso(2),
          agentKind: "bash",
          agentVersion: "1.0.0",
          passing: 5,
          failing: 1,
          checks: devChecks,
        },
        {
          at: iso(8),
          agentKind: "bash",
          agentVersion: "1.0.0",
          passing: 5,
          failing: 1,
          checks: devChecks,
        },
      ],
    },
    {
      id: "4e9b64c1-2f10-4b88-901a-100000000003",
      serial: "HL-AGT-1003",
      hostname: "lena-thinkpad-w11",
      platform: "windows",
      operatingSystem: "Windows 11 24H2",
      personEmail: `lena.cho@${domain}`,
      personName: "Lena Cho",
      agentKind: "powershell",
      agentVersion: "1.0.0",
      enrolledAt: iso(96),
      lastSeenAt: iso(4),
      revoked: false,
      secret: "demo-secret-3",
      checks: lenaChecks,
      history: [
        {
          at: iso(4),
          agentKind: "powershell",
          agentVersion: "1.0.0",
          passing: 4,
          failing: 2,
          checks: lenaChecks,
        },
      ],
    },
    {
      id: "4e9b64c1-2f10-4b88-901a-100000000004",
      serial: "HL-AGT-1004",
      hostname: "build-ws-ubuntu",
      platform: "linux",
      operatingSystem: "Ubuntu 24.04 LTS",
      personEmail: `dev.patel@${domain}`,
      personName: "Dev Patel",
      agentKind: "bash",
      agentVersion: "1.0.0",
      enrolledAt: iso(480),
      lastSeenAt: iso(220),
      revoked: false,
      secret: "demo-secret-4",
      checks: staleLinuxChecks,
      history: [
        {
          at: iso(220),
          agentKind: "bash",
          agentVersion: "1.0.0",
          passing: 5,
          failing: 1,
          checks: staleLinuxChecks,
        },
      ],
    },
  ];

  const tokens: DemoEnrollmentToken[] = [
    {
      id: "5d9b64c1-2f10-4b88-901a-200000000001",
      raw: `d2de_${TENANT_ID_HEX}_a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2`,
      hint: "…f6a1b2",
      personEmail: `chandra.vell@${domain}`,
      personName: "Chandra Vell",
      createdByEmail: `chandra.vell@${domain}`,
      createdAt: iso(241),
      expiresAt: iso(169),
      usedAt: iso(240),
      revokedAt: null,
    },
    {
      id: "5d9b64c1-2f10-4b88-901a-200000000002",
      raw: `d2de_${TENANT_ID_HEX}_9988776655443322110099887766554433221100`,
      hint: "…221100",
      personEmail: `dev.patel@${domain}`,
      personName: "Dev Patel",
      createdByEmail: `chandra.vell@${domain}`,
      createdAt: iso(3),
      expiresAt: new Date(now + 69 * 3_600_000).toISOString(),
      usedAt: null,
      revokedAt: null,
    },
  ];

  const passedDate = todayStr();
  const expiresDate = addMonths(passedDate, RENEWAL_MONTHS);

  const progress: DemoAcademyProgress[] = [
    {
      moduleKey: "security-basics",
      personEmail: `chandra.vell@${domain}`,
      personName: "Chandra Vell",
      sectionsCompleted: 4,
      attempts: 1,
      bestScore: 5,
      passedAt: iso(48),
      expiresOn: expiresDate,
      certificateNumber: `D2D-${passedDate.replaceAll("-", "")}-A1B2C3D4E5`,
      evidenceId: null,
    },
    {
      moduleKey: "phishing-social-engineering",
      personEmail: `chandra.vell@${domain}`,
      personName: "Chandra Vell",
      sectionsCompleted: 2,
      attempts: 0,
      bestScore: 0,
      passedAt: null,
      expiresOn: null,
      certificateNumber: null,
      evidenceId: null,
    },
    {
      moduleKey: "security-basics",
      personEmail: `dev.patel@${domain}`,
      personName: "Dev Patel",
      sectionsCompleted: 4,
      attempts: 1,
      bestScore: 4,
      passedAt: iso(72),
      expiresOn: expiresDate,
      certificateNumber: `D2D-${passedDate.replaceAll("-", "")}-9F8E7D6C5B`,
      evidenceId: null,
    },
  ];

  const state: AgentAcademyState = {
    tokens,
    devices,
    assignments: {},
    progress,
  };
  stateByWorld.set(data, state);
  return state;
}

function toDeviceDto(device: DemoAgentDevice) {
  return {
    id: device.id,
    serial: device.serial,
    hostname: device.hostname,
    platform: device.platform,
    operatingSystem: device.operatingSystem,
    personEmail: device.personEmail,
    personName: device.personName,
    agentKind: device.agentKind,
    agentVersion: device.agentVersion,
    enrolledAt: device.enrolledAt,
    lastSeenAt: device.lastSeenAt,
    stale: !device.revoked && isStale(device.lastSeenAt),
    revoked: device.revoked,
    passing: device.checks.filter((c) => c.pass).length,
    failing: device.checks.filter((c) => !c.pass).length,
    checks: device.checks,
  };
}

function toTokenDto(token: DemoEnrollmentToken) {
  return {
    id: token.id,
    hint: token.hint,
    personEmail: token.personEmail,
    personName: token.personName,
    createdByEmail: token.createdByEmail,
    createdAt: token.createdAt,
    expiresAt: token.expiresAt,
    state: tokenState(token),
  };
}

function isValidAudience(audience: string) {
  const value = audience.trim();
  if (value === "Everyone") return true;
  const sep = value.indexOf(":");
  if (sep <= 0 || value.length > 120) return false;
  const kind = value.slice(0, sep);
  return (kind === "Department" || kind === "Role") && value.slice(sep + 1).trim().length > 0;
}

function matchesAudience(audience: string, department: string, role: string) {
  const value = audience.trim();
  if (value === "Everyone") return true;
  const sep = value.indexOf(":");
  if (sep <= 0) return false;
  const kind = value.slice(0, sep);
  const target = value
    .slice(sep + 1)
    .trim()
    .toLowerCase();
  if (kind === "Department") return department.trim().toLowerCase() === target;
  if (kind === "Role") return role.trim().toLowerCase() === target;
  return false;
}

function moduleAudiences(module: AcademyModuleDef, state: AgentAcademyState) {
  return state.assignments[module.key] ?? module.defaultAudiences;
}

function isAvailable(module: AcademyModuleDef, data: DemoWorld) {
  if (!module.requiresFramework) return true;
  return data.frameworks.some(
    (fw) => fw.key.toLowerCase() === module.requiresFramework!.toLowerCase(),
  );
}

function isCurrent(expiresOn: string | null) {
  return Boolean(expiresOn && expiresOn >= todayStr());
}

function progressStatus(row: DemoAcademyProgress | undefined) {
  if (!row || (!row.passedAt && row.sectionsCompleted === 0 && row.attempts === 0)) {
    return "NotStarted";
  }
  if (row.passedAt) {
    return isCurrent(row.expiresOn) ? "Passed" : "Expired";
  }
  return "InProgress";
}

function toModuleItem(
  module: AcademyModuleDef,
  state: AgentAcademyState,
  data: DemoWorld,
  actor: Actor,
) {
  const person = data.people.find((p) => p.email.toLowerCase() === actor.email.toLowerCase());
  const audiences = moduleAudiences(module, state);
  const required = audiences.some((aud) =>
    matchesAudience(aud, person?.department ?? "", person?.role ?? actor.role),
  );
  const row = state.progress.find(
    (p) => p.moduleKey === module.key && p.personEmail.toLowerCase() === actor.email.toLowerCase(),
  );
  return {
    key: module.key,
    title: module.title,
    summary: module.summary,
    minutes: module.minutes,
    required,
    frameworkSpecific: module.requiresFramework !== null,
    audiences,
    sectionsTotal: module.sections.length,
    sectionsCompleted: row?.sectionsCompleted ?? 0,
    status: progressStatus(row),
    bestScore: row?.bestScore ?? 0,
    questionCount: module.questions.length,
    passedAt: row?.passedAt ?? null,
    expiresOn: row?.expiresOn ?? null,
    certificateNumber: row?.passedAt ? row.certificateNumber : null,
  };
}

export async function handleAnonymousAgent(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
) {
  if (path === "/api/v1/agent/enroll" && method === "POST") {
    const state = getState(data);
    const rawToken = String(body.token ?? "").trim();
    const token = state.tokens.find((t) => t.raw === rawToken);
    if (!token) return fail(401, "unauthorized");
    if (tokenState(token) !== "Active") return fail(401, "expired_signature");
    const serial = String(body.serial ?? "").trim();
    const os = String(body.operatingSystem ?? "").trim();
    const platform = String(body.platform ?? "")
      .trim()
      .toLowerCase();
    const agentKind = String(body.agentKind ?? "dotnet")
      .trim()
      .toLowerCase();
    if (
      !serial ||
      !os ||
      !["windows", "macos", "linux"].includes(platform) ||
      !["dotnet", "powershell", "bash"].includes(agentKind)
    ) {
      return fail(400, "invalid_device");
    }
    const now = new Date().toISOString();
    for (const prev of state.devices) {
      if (prev.serial === serial && !prev.revoked) prev.revoked = true;
    }
    const id = crypto.randomUUID();
    const secret = randomHex(64);
    const checks = buildChecks(platform);
    const device: DemoAgentDevice = {
      id,
      serial,
      hostname: String(body.hostname ?? serial).trim(),
      platform: platform as DemoAgentDevice["platform"],
      operatingSystem: os,
      personEmail: token.personEmail,
      personName: token.personName,
      agentKind: agentKind as DemoAgentDevice["agentKind"],
      agentVersion: String(body.agentVersion ?? "1.0.0").trim(),
      enrolledAt: now,
      lastSeenAt: now,
      revoked: false,
      secret,
      checks,
      history: [
        {
          at: now,
          agentKind,
          agentVersion: String(body.agentVersion ?? "1.0.0").trim(),
          passing: checks.length,
          failing: 0,
          checks,
        },
      ],
    };
    state.devices.unshift(device);
    token.usedAt = now;
    return json({
      deviceId: `d2dd_${TENANT_ID_HEX}_${id.replaceAll("-", "")}`,
      secret,
      checkInPath: "/api/v1/agent/check-in",
      intervalHours: 6,
      personEmail: device.personEmail,
    });
  }
  return null;
}

export async function handleAgentAcademy(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
  onRecord: (action: string, detail: string) => void,
) {
  const state = getState(data);

  // ---- Device Agent endpoints ----
  if (path === "/api/v1/agent/devices" && method === "GET") {
    if (actor.role === "Auditor") return json([]);
    const mine = query.get("mine") === "true" || !canWrite(actor);
    const rows = state.devices
      .filter((d) => !mine || d.personEmail.toLowerCase() === actor.email.toLowerCase())
      .map(toDeviceDto);
    return json(rows);
  }

  if (path.startsWith("/api/v1/agent/devices/") && path.endsWith("/history") && method === "GET") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    const id = path.slice("/api/v1/agent/devices/".length, -"/history".length);
    const device = state.devices.find((d) => d.id === id);
    if (!device) return fail(404, "not_found");
    if (!canWrite(actor) && device.personEmail.toLowerCase() !== actor.email.toLowerCase()) {
      return fail(403, "forbidden");
    }
    return json(device.history);
  }

  if (path.startsWith("/api/v1/agent/devices/") && path.endsWith("/revoke") && method === "POST") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    const id = path.slice("/api/v1/agent/devices/".length, -"/revoke".length);
    const device = state.devices.find((d) => d.id === id);
    if (!device) return fail(404, "not_found");
    device.revoked = true;
    onRecord("agent.device.revoked", device.serial);
    return json(toDeviceDto(device));
  }

  if (path === "/api/v1/agent/enrollment-tokens" && method === "GET") {
    if (actor.role === "Auditor") return json([]);
    const rows = state.tokens
      .filter((t) => canWrite(actor) || t.personEmail.toLowerCase() === actor.email.toLowerCase())
      .map(toTokenDto);
    return json(rows);
  }

  if (path === "/api/v1/agent/enrollment-tokens" && method === "POST") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    const email = String(body.personEmail ?? "").trim() || actor.email;
    const self = email.toLowerCase() === actor.email.toLowerCase();
    if (!self && !canWrite(actor)) return fail(403, "forbidden");
    if (!email.includes("@")) return fail(400, "invalid_person");
    const person = data.people.find((p) => p.email.toLowerCase() === email.toLowerCase());
    if (!person && !self) return fail(400, "invalid_person");
    const raw = `d2de_${TENANT_ID_HEX}_${randomHex(40)}`;
    const now = new Date();
    const expires = new Date(now.getTime() + 72 * 3_600_000);
    const created: DemoEnrollmentToken = {
      id: crypto.randomUUID(),
      raw,
      hint: `…${raw.slice(-6)}`,
      personEmail: person?.email ?? email,
      personName: person?.name ?? actor.name,
      createdByEmail: actor.email,
      createdAt: now.toISOString(),
      expiresAt: expires.toISOString(),
      usedAt: null,
      revokedAt: null,
    };
    state.tokens.unshift(created);
    onRecord("agent.token.created", created.personEmail);
    return json({
      id: created.id,
      token: created.raw,
      personEmail: created.personEmail,
      personName: created.personName,
      expiresAt: created.expiresAt,
    });
  }

  if (
    path.startsWith("/api/v1/agent/enrollment-tokens/") &&
    path.endsWith("/revoke") &&
    method === "POST"
  ) {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    const id = path.slice("/api/v1/agent/enrollment-tokens/".length, -"/revoke".length);
    const token = state.tokens.find((t) => t.id === id);
    if (!token) return fail(404, "not_found");
    if (!canWrite(actor) && token.personEmail.toLowerCase() !== actor.email.toLowerCase()) {
      return fail(403, "forbidden");
    }
    token.revokedAt ??= new Date().toISOString();
    onRecord("agent.token.revoked", token.personEmail);
    return json(toTokenDto(token));
  }

  // ---- Security Awareness Academy endpoints ----
  if (path === "/api/v1/academy" && method === "GET") {
    const available = ACADEMY_MODULES.filter((m) => isAvailable(m, data));
    const items = available.map((m) => toModuleItem(m, state, data, actor));
    const required = items.filter((i) => i.required);
    return json({
      modules: items,
      required: required.length,
      current: required.filter((i) => i.status === "Passed").length,
      passMark: PASS_MARK,
      renewalMonths: RENEWAL_MONTHS,
    });
  }

  if (path === "/api/v1/academy/assignments" && method === "GET") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    return json(assignmentItems(state, data));
  }

  if (path.startsWith("/api/v1/academy/assignments/") && method === "PUT") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    const key = decodeURIComponent(path.slice("/api/v1/academy/assignments/".length));
    const module = ACADEMY_MODULES.find((m) => m.key === key);
    if (!module) return fail(404, "not_found");
    const rawList = Array.isArray(body.audiences)
      ? body.audiences.map((item) => String(item ?? "").trim()).filter(Boolean)
      : [];
    const deduped = [...new Set(rawList)];
    if (deduped.length > 20 || deduped.some((a) => !isValidAudience(a))) {
      return fail(400, "invalid_audience");
    }
    state.assignments[key] = deduped;
    onRecord("academy.assigned", `${key} ${deduped.join(", ")}`);
    const updated = assignmentItems(state, data).find((a) => a.moduleKey === key);
    return json(updated);
  }

  if (path.startsWith("/api/v1/academy/") && path.endsWith("/progress") && method === "POST") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    const key = decodeURIComponent(path.slice("/api/v1/academy/".length, -"/progress".length));
    const module = ACADEMY_MODULES.find((m) => m.key === key && isAvailable(m, data));
    if (!module) return fail(404, "not_found");
    const count = Number(body.sectionsCompleted);
    if (!Number.isInteger(count) || count < 0 || count > module.sections.length) {
      return fail(400, "invalid_progress");
    }
    const row = getOrCreateProgress(state, module.key, actor);
    row.sectionsCompleted = Math.max(row.sectionsCompleted, count);
    return json(toModuleItem(module, state, data, actor));
  }

  if (path.startsWith("/api/v1/academy/") && path.endsWith("/quiz") && method === "POST") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    const key = decodeURIComponent(path.slice("/api/v1/academy/".length, -"/quiz".length));
    const module = ACADEMY_MODULES.find((m) => m.key === key && isAvailable(m, data));
    if (!module) return fail(404, "not_found");
    const answers = Array.isArray(body.answers) ? body.answers.map((v) => Number(v)) : null;
    if (!answers || answers.length !== module.questions.length) {
      return fail(400, "invalid_answers");
    }
    const wrong: number[] = [];
    module.questions.forEach((q, idx) => {
      if (answers[idx] !== q.answer) wrong.push(idx);
    });
    const correct = module.questions.length - wrong.length;
    const passed = correct >= PASS_MARK;
    const explanations = wrong.map((idx) => module.questions[idx].explanation);
    const row = getOrCreateProgress(state, module.key, actor);
    row.attempts += 1;

    if (!passed) {
      onRecord("academy.quiz", `${module.key} failed (${correct}/${module.questions.length})`);
      return json({
        correct,
        total: module.questions.length,
        passMark: PASS_MARK,
        passed: false,
        wrong,
        explanations,
        expiresOn: null,
        certificateNumber: null,
        evidenceId: null,
      });
    }

    const renewing = !isCurrent(row.expiresOn);
    row.bestScore = renewing ? correct : Math.max(row.bestScore, correct);
    row.sectionsCompleted = module.sections.length;
    if (renewing) {
      const today = todayStr();
      const digest = await sha256(
        `${TENANT_ID_HEX}|${module.key}|${actor.email.toLowerCase()}|${today}`,
      );
      row.passedAt = new Date().toISOString();
      row.expiresOn = addMonths(today, RENEWAL_MONTHS);
      row.certificateNumber = `D2D-${today.replaceAll("-", "")}-${digest.slice(0, 10).toUpperCase()}`;

      const certLines = certificateLines(module, row);
      const certText = certLines.join("\n");
      const hash = await sha256(certText);
      const evidenceId = crypto.randomUUID();
      const lockedUntil = new Date();
      lockedUntil.setUTCFullYear(lockedUntil.getUTCFullYear() + 3);
      data.evidence.unshift({
        id: evidenceId,
        title: `${module.title} ${actor.name}`,
        controlCode: "HR-06",
        source: "Security academy",
        collectedAt: row.passedAt,
        integrity: "verified",
        sha256: hash,
        lockedUntil: lockedUntil.toISOString(),
        body: certText,
      });
      row.evidenceId = evidenceId;
    }

    onRecord("academy.passed", module.key);
    return json({
      correct,
      total: module.questions.length,
      passMark: PASS_MARK,
      passed: true,
      wrong,
      explanations,
      expiresOn: row.expiresOn,
      certificateNumber: row.certificateNumber,
      evidenceId: row.evidenceId,
    });
  }

  if (path.startsWith("/api/v1/academy/") && path.endsWith("/certificate") && method === "GET") {
    const key = decodeURIComponent(path.slice("/api/v1/academy/".length, -"/certificate".length));
    const module = ACADEMY_MODULES.find((m) => m.key === key);
    if (!module) return fail(404, "not_found");
    const row = state.progress.find(
      (p) => p.moduleKey === key && p.personEmail.toLowerCase() === actor.email.toLowerCase(),
    );
    if (!row?.passedAt) return fail(400, "not_completed");
    const bytes = plainPdf(certificateLines(module, row));
    const buf = new ArrayBuffer(bytes.byteLength);
    new Uint8Array(buf).set(bytes);
    return new Response(buf, {
      status: 200,
      headers: {
        "content-type": "application/pdf",
        "content-disposition": `attachment; filename="certificate-${module.key}.pdf"`,
      },
    });
  }

  if (path.startsWith("/api/v1/academy/") && method === "GET") {
    const key = decodeURIComponent(path.slice("/api/v1/academy/".length));
    const module = ACADEMY_MODULES.find((m) => m.key === key && isAvailable(m, data));
    if (!module) return fail(404, "not_found");
    return json({
      module: toModuleItem(module, state, data, actor),
      sections: module.sections,
      questions: module.questions.map(({ prompt, choices }) => ({ prompt, choices })),
    });
  }

  return null;
}

function getOrCreateProgress(state: AgentAcademyState, moduleKey: string, actor: Actor) {
  let row = state.progress.find(
    (p) => p.moduleKey === moduleKey && p.personEmail.toLowerCase() === actor.email.toLowerCase(),
  );
  if (!row) {
    row = {
      moduleKey,
      personEmail: actor.email,
      personName: actor.name,
      sectionsCompleted: 0,
      attempts: 0,
      bestScore: 0,
      passedAt: null,
      expiresOn: null,
      certificateNumber: null,
      evidenceId: null,
    };
    state.progress.push(row);
  }
  return row;
}

function assignmentItems(state: AgentAcademyState, data: DemoWorld) {
  const activePeople = data.people.filter((p) => p.state === "Active");
  return ACADEMY_MODULES.map((module) => {
    const audiences = moduleAudiences(module, state);
    const available = isAvailable(module, data);
    const assignedPeople = available
      ? activePeople.filter((person) =>
          audiences.some((aud) => matchesAudience(aud, person.department, person.role)),
        )
      : [];
    const current = assignedPeople.filter((person) =>
      state.progress.some(
        (row) =>
          row.moduleKey === module.key &&
          row.personEmail.toLowerCase() === person.email.toLowerCase() &&
          isCurrent(row.expiresOn),
      ),
    ).length;
    return {
      moduleKey: module.key,
      title: module.title,
      available,
      audiences,
      assigned: assignedPeople.length,
      current,
    };
  });
}

function certificateLines(module: AcademyModuleDef, row: DemoAcademyProgress) {
  const passed = row.passedAt ? row.passedAt.slice(0, 10) : "";
  return [
    "Certificate of completion",
    "",
    `${company.name} security awareness academy`,
    "",
    `This certifies that ${row.personName} (${row.personEmail})`,
    `completed the module: ${module.title}`,
    `Quiz score: ${row.bestScore} of ${module.questions.length} (pass mark ${PASS_MARK})`,
    `Completed on: ${passed}`,
    `Renewal due by: ${row.expiresOn ?? ""}`,
    `Certificate number: ${row.certificateNumber ?? ""}`,
    "",
    "Issued by D2D Audit Services. Evidence is stored under control HR-06.",
  ];
}
