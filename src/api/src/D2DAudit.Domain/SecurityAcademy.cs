using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace D2DAudit.Domain;

public sealed class AcademyProgress
{
    public Guid Id { get; set; }

    public string ModuleKey { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public string PersonName { get; set; } = "";

    public int SectionsCompleted { get; set; }

    public int Attempts { get; set; }

    public int LastScore { get; set; }

    public int BestScore { get; set; }

    public DateTimeOffset? PassedAt { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public string CertificateNumber { get; set; } = "";

    public Guid? EvidenceId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AcademyAssignment
{
    public Guid Id { get; set; }

    public string ModuleKey { get; set; } = "";

    public string Audience { get; set; } = "";

    public string UpdatedByEmail { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record AcademySection(string Heading, string Body);

public sealed record AcademyQuestion(string Prompt, IReadOnlyList<string> Choices, int Answer, string Explanation);

public sealed record AcademyModuleContent(
    string Key,
    string Title,
    string Summary,
    int Minutes,
    IReadOnlyList<string> DefaultAudiences,
    string? RequiresFramework,
    IReadOnlyList<AcademySection> Sections,
    IReadOnlyList<AcademyQuestion> Questions);

public sealed record AcademyScore(int Correct, int Total, bool Passed, IReadOnlyList<int> Wrong);

/// <summary>Built-in security awareness academy. All lesson and quiz content is original to D2D Audit Services.</summary>
public static class SecurityAcademy
{
    public const int PassMark = 4;
    public const int RenewalMonths = 12;
    public const string Everyone = "Everyone";

    public static readonly IReadOnlyList<AcademyModuleContent> Modules =
    [
        new(
            "security-basics",
            "Security awareness basics",
            "How everyday habits protect customers, colleagues and the company.",
            12,
            [Everyone],
            null,
            [
                new("Why this matters", "Most security incidents start with an ordinary moment: a reused password, an unlocked laptop, a rushed click. Attackers target people because people have access.\n- You are part of the security program, not just a user of it.\n- Small habits, repeated every day, stop most real-world attacks."),
                new("Protect your devices", "Your laptop holds credentials, customer data and source code.\n- Keep full-disk encryption on and the screen locked when you step away (Windows+L or Ctrl+Cmd+Q).\n- Install updates within a few days of release.\n- Never disable endpoint protection or the firewall to make something work."),
                new("Handle information with care", "Classify before you share. Public, Internal, Confidential and Restricted data each have rules.\n- Share files through approved tools with named people, not public links.\n- Do not paste customer data into personal accounts, chat tools or unapproved AI services."),
                new("Report early", "Reporting quickly turns a potential breach into a contained event.\n- Report lost devices, suspicious messages and accidental disclosures to security straight away.\n- There is no penalty for reporting a mistake; there is a real cost to hiding one."),
            ],
            [
                new("You need to leave your desk for a five-minute coffee. What should you do with your laptop?", ["Leave it open, five minutes is short", "Lock the screen before you walk away", "Close the lid only if someone is nearby", "Ask a colleague to watch it"], 1, "Lock the screen every time you step away, however briefly."),
                new("A tool will not install unless you turn off endpoint protection. What is the right step?", ["Turn it off briefly and turn it back on", "Ask IT or security for an approved route", "Use a personal laptop instead", "Install it in a virtual machine without telling anyone"], 1, "Security controls should only be changed through an approved request."),
                new("Which is the best way to share a confidential document with a partner?", ["A public link from cloud storage", "An approved sharing tool restricted to named recipients", "Your personal email account", "A USB stick posted to them"], 1, "Restrict access to named people using approved tools."),
                new("You accidentally emailed a customer list to the wrong person. What first?", ["Wait to see if they reply", "Report it to security immediately", "Delete it from your Sent folder", "Ask the recipient not to tell anyone"], 1, "Early reporting lets the security team contain the disclosure."),
                new("How quickly should operating system security updates normally be installed?", ["Within a few days of release", "Once a year", "Only when the laptop feels slow", "Never, updates break things"], 0, "Prompt patching closes known vulnerabilities before they are exploited."),
            ]),
        new(
            "phishing-social-engineering",
            "Phishing and social engineering",
            "Spot and report manipulation by email, chat, phone and in person.",
            15,
            [Everyone],
            null,
            [
                new("How manipulation works", "Social engineering uses urgency, authority, curiosity or fear to make you act before you think.\n- 'The CEO needs gift cards in the next hour.'\n- 'Your mailbox will be deleted today unless you sign in.'\n- A pause of ten seconds defeats most of these tricks."),
                new("Signs of a phishing message", "Look at the whole message, not only the logo.\n- The sender domain is slightly wrong or the reply-to differs.\n- Links point somewhere unexpected when you hover over them.\n- Requests to bypass process: new bank details, urgent payments, password resets you did not start."),
                new("Beyond email", "Attackers also call, text, use chat tools and even visit offices.\n- Verify callers by calling back on a number you already know.\n- Never approve an MFA prompt you did not trigger; that is an attacker holding your password.\n- Challenge politely when someone without a badge follows you through a door."),
                new("Reporting", "Use the Report phishing button or forward the message to the security team.\n- Do not reply, click or forward the message to colleagues as a warning.\n- If you already clicked or typed a password, say so immediately so the account can be secured."),
            ],
            [
                new("You receive an MFA push you did not request. What do you do?", ["Approve it to make it stop", "Deny it and report it to security", "Ignore it", "Turn off MFA"], 1, "An unexpected prompt means someone may already have your password."),
                new("An email from 'the CFO' asks you to urgently change a supplier's bank details. Best response?", ["Change them, the CFO is senior", "Reply to the email to confirm", "Verify through a known phone number or approved process", "Forward it to the supplier"], 2, "Verify payment changes out of band through the established process."),
                new("Which is a strong sign that a link is malicious?", ["It is underlined", "Hovering shows a domain that does not match the sender", "It is in blue text", "It appears at the bottom of the message"], 1, "Mismatched domains are a classic phishing tell."),
                new("You clicked a link and typed your password before realising it was fake. What now?", ["Hope nothing happens", "Change your password later this week", "Report it immediately so the account can be secured", "Clear your browser history"], 2, "Speed matters; security can reset sessions and credentials."),
                new("Someone without a badge asks you to hold the office door. What is reasonable?", ["Let them in, it is polite", "Politely direct them to reception to sign in", "Ignore them and let the door close in their face", "Lend them your badge"], 1, "Tailgating is a physical social engineering technique."),
            ]),
        new(
            "passwords-mfa",
            "Passwords and multi-factor authentication",
            "Build strong, unique credentials and use MFA everywhere.",
            10,
            [Everyone],
            null,
            [
                new("Use a password manager", "A password manager remembers long, unique passwords so you do not have to.\n- Use the company-approved password manager for work accounts.\n- Let it generate passwords of at least 16 characters.\n- Protect the vault with a long passphrase you do not use anywhere else."),
                new("One account, one password", "Reused passwords turn one breach into many.\n- Never reuse a work password on a personal site.\n- Change a password straight away if a site you use reports a breach."),
                new("Multi-factor authentication", "MFA blocks most account takeover attempts even when a password leaks.\n- Prefer phishing-resistant factors such as passkeys or security keys.\n- Authenticator apps are the next best choice; SMS codes are the weakest option.\n- Never share codes; no legitimate team will ask for them."),
                new("Recovery and sharing", "Store recovery codes in your password manager, not in a document on your desktop.\n- Shared team credentials belong in a shared vault with access logging, never in chat."),
            ],
            [
                new("What is the main benefit of a password manager?", ["It makes passwords shorter", "It lets you use long, unique passwords for every account", "It removes the need for MFA", "It shares passwords with IT automatically"], 1, "Unique, generated passwords stop credential reuse attacks."),
                new("Which second factor is most resistant to phishing?", ["SMS code", "Security key or passkey", "Email link", "Security questions"], 1, "Security keys and passkeys are bound to the real website."),
                new("A caller from 'IT support' asks for your MFA code to fix your account. What do you do?", ["Read it out", "Refuse, end the call and report it", "Send it by chat instead", "Give them half the code"], 1, "No legitimate team asks for your codes."),
                new("Where should a team's shared service password be kept?", ["In a pinned chat message", "In a shared vault in the password manager", "On a sticky note under the keyboard", "In a spreadsheet on a shared drive"], 1, "Shared vaults control and log access."),
                new("A site you use announces a breach. What should you do?", ["Nothing, it was their fault", "Change that password and anywhere you reused it", "Delete your email account", "Wait for a second email"], 1, "Change exposed passwords immediately, and stop reusing them."),
            ]),
        new(
            "data-privacy",
            "Data handling and privacy",
            "Personal data, GDPR principles, HIPAA basics and your daily responsibilities.",
            15,
            [Everyone],
            null,
            [
                new("What counts as personal data", "Personal data is any information that identifies a person directly or indirectly.\n- Names, emails, IP addresses, device ids and support tickets all count.\n- Special categories, such as health data, need extra protection."),
                new("GDPR principles in practice", "Collect only what is needed, use it only for the stated purpose and keep it only as long as necessary.\n- Data minimisation: do not export whole tables when a few fields will do.\n- Purpose limitation: data collected for support is not automatically available for marketing.\n- Individuals can ask to see, correct or delete their data; route these requests to the privacy team within a day."),
                new("HIPAA basics", "Protected health information (PHI) is health data linked to an individual and handled for a covered entity.\n- Access PHI only when your job requires it (minimum necessary).\n- Never send PHI through unapproved channels or store it on personal devices.\n- Suspected PHI disclosures must be reported at once; breach notification deadlines are strict."),
                new("Everyday handling", "Label files with their classification and store them only in approved systems.\n- Use test data or anonymised extracts for development and demos.\n- Delete local copies when the task is complete."),
            ],
            [
                new("Which of these is personal data?", ["A customer's IP address", "The company's public address", "A product price list", "An anonymous aggregated count"], 0, "An IP address can identify an individual."),
                new("A data subject asks for a copy of their data. What should you do?", ["Ignore it", "Send them the full database", "Route it to the privacy team promptly", "Ask them to email again next month"], 2, "Subject requests have legal deadlines and a defined process."),
                new("What does data minimisation mean?", ["Compress files to save space", "Collect and use only the data needed for the purpose", "Delete all data every week", "Store data in fewer countries"], 1, "Only collect and process what is necessary."),
                new("Under HIPAA, when may you access a patient record?", ["Whenever you are curious", "Only when your job requires it", "When a friend asks", "Any time during working hours"], 1, "The minimum necessary standard limits access to job need."),
                new("You need realistic data for a product demo. What is the best choice?", ["Copy production customer data", "Use anonymised or synthetic data", "Screenshot a real customer account", "Ask a customer for their password"], 1, "Demos and development should use test or anonymised data."),
            ]),
        new(
            "secure-coding",
            "Secure coding and the OWASP Top 10",
            "Prevent the most common web application vulnerabilities in code you write and review.",
            20,
            ["Department:Engineering"],
            null,
            [
                new("Broken access control", "The most common serious flaw is letting a user act on data they do not own.\n- Check authorisation on every request on the server, not only in the UI.\n- Scope every query by tenant and owner; deny by default."),
                new("Injection and unsafe input", "Untrusted input must never be concatenated into queries, commands or templates.\n- Use parameterised queries and ORM bindings.\n- Encode output for its context (HTML, attribute, URL).\n- Validate input against allow-lists and size limits."),
                new("Cryptography and secrets", "Use well-reviewed libraries and platform defaults.\n- Never commit secrets; use a secret store and rotate on exposure.\n- Hash passwords with a slow, salted algorithm and store only hashes of API tokens.\n- Compare secrets with constant-time functions."),
                new("Dependencies, configuration and logging", "Vulnerable and outdated components and security misconfiguration are frequent root causes.\n- Keep dependencies patched and review new packages before adding them.\n- Ship secure defaults: no debug endpoints, strict CORS, least-privilege service accounts.\n- Log security events without logging secrets or personal data."),
                new("Server-side request forgery and design", "Validate outbound URLs against an allow-list and block internal address ranges.\n- Threat-model new features: who could abuse this, and what would they gain?"),
            ],
            [
                new("Where must authorisation checks be enforced?", ["Only in the front-end", "On the server for every request", "Only on the login page", "In the database backup job"], 1, "Client-side checks can always be bypassed."),
                new("What is the correct defence against SQL injection?", ["Escaping quotes by hand", "Parameterised queries", "Hiding error messages", "Using POST instead of GET"], 1, "Parameterised queries separate code from data."),
                new("You accidentally committed an API key. What should you do?", ["Delete it in the next commit", "Revoke and rotate the key, then remove it from history", "Make the repository private", "Nothing, nobody reads old commits"], 1, "Treat any committed secret as exposed and rotate it."),
                new("Which is the safest way to compare a presented token with a stored value?", ["String equality on the raw token", "Constant-time comparison of hashes", "Comparing the first eight characters", "Checking the length only"], 1, "Store hashes and compare in constant time to avoid timing leaks."),
                new("A feature fetches a URL supplied by the user. What risk must you mitigate?", ["Cross-site scripting only", "Server-side request forgery", "Clickjacking", "Password reuse"], 1, "Validate destinations to stop SSRF into internal networks."),
            ]),
        new(
            "ai-acceptable-use",
            "AI acceptable use",
            "Use generative AI tools productively without leaking data or shipping unreviewed output.",
            10,
            [Everyone],
            null,
            [
                new("Approved tools only", "Use AI tools that the company has approved and configured for business data.\n- Consumer AI services may retain prompts and use them for training.\n- If you are unsure whether a tool is approved, ask before you paste."),
                new("What you may share", "Never paste secrets, credentials, customer personal data or Restricted information into an AI tool unless it is explicitly approved for that classification.\n- Remove names and identifiers from examples.\n- Summarise rather than copy whole documents where possible."),
                new("You own the output", "AI output can be wrong, biased, insecure or infringing.\n- Review generated code with the same care as a colleague's pull request.\n- Check facts, sources and licences before publishing.\n- Disclose AI assistance where policy or customers require it."),
                new("Automated decisions", "Do not use AI to make decisions about people (hiring, performance, eligibility) without human review and approval from legal and privacy."),
            ],
            [
                new("Which is acceptable to paste into an unapproved public chatbot?", ["A customer support ticket with names", "Production database credentials", "A generic question with no company data", "A Restricted architecture document"], 2, "Only share information with no company or personal data in unapproved tools."),
                new("Who is responsible for AI-generated code that you merge?", ["The AI vendor", "Nobody", "You, as the author and reviewer", "The security team"], 2, "You own everything you commit."),
                new("An AI tool suggests a confident but unfamiliar statistic for a customer report. What should you do?", ["Publish it", "Verify it against a reliable source first", "Round the number", "Remove the citation"], 1, "Generative models can fabricate facts."),
                new("You are not sure if an AI tool is approved for Confidential data. What next?", ["Try it with a small sample", "Ask the security or IT team before using it", "Use it on a personal laptop", "Assume it is approved"], 1, "Check approval before sharing any business data."),
                new("Can AI alone decide which job applicants are rejected?", ["Yes, it is faster", "Only for junior roles", "No, decisions about people need human review and approval", "Yes, if the model is accurate"], 2, "Automated decisions about people need human oversight."),
            ]),
        new(
            "hipaa-privacy-security",
            "HIPAA privacy and security rule essentials",
            "PHI safeguards, minimum necessary access and breach reporting for teams handling health data.",
            15,
            [Everyone],
            "hipaa",
            [
                new("PHI and the HIPAA rules", "The Privacy Rule governs how protected health information is used and disclosed. The Security Rule requires administrative, physical and technical safeguards for electronic PHI.\n- As a business associate we must protect PHI exactly as our customers do."),
                new("Minimum necessary", "Use, disclose and request only the minimum PHI needed for the task.\n- Role-based access limits who can see PHI.\n- Do not browse records out of curiosity; access is logged and reviewed."),
                new("Safeguards in daily work", "Store ePHI only in approved, encrypted systems.\n- Never email PHI outside approved secure channels.\n- Lock screens, use privacy filters in public and shred printed PHI."),
                new("Breaches and reporting", "An impermissible use or disclosure of PHI is presumed to be a breach until a risk assessment shows otherwise.\n- Report suspected incidents to the privacy officer immediately.\n- Notification deadlines run from the date of discovery, so delays matter."),
            ],
            [
                new("What does the HIPAA Security Rule specifically protect?", ["All company data", "Electronic protected health information", "Marketing material", "Employee payroll"], 1, "The Security Rule covers ePHI."),
                new("What is the 'minimum necessary' standard?", ["Use the minimum password length", "Access only the PHI needed for the task", "Store PHI for the minimum time of one year", "Encrypt only some fields"], 1, "Limit PHI use and disclosure to what the task requires."),
                new("A colleague asks you to look up a celebrity's record out of interest. What do you do?", ["Look it up quickly", "Refuse; access without a job need is a violation", "Ask a manager to do it", "Look but do not tell anyone"], 1, "Curiosity is never a permitted purpose."),
                new("How should you send PHI to an approved partner?", ["Regular email", "An approved secure, encrypted channel", "A personal messaging app", "A public file-sharing link"], 1, "Only approved secure channels may carry PHI."),
                new("When should a suspected PHI disclosure be reported?", ["After the investigation is complete", "Immediately to the privacy officer", "At the next quarterly review", "Only if a patient complains"], 1, "Deadlines run from discovery, so report at once."),
            ]),
    ];

    public static AcademyModuleContent? Find(string key) =>
        Modules.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public static bool Available(AcademyModuleContent module, IEnumerable<string> frameworkKeys) =>
        module.RequiresFramework is null || frameworkKeys.Contains(module.RequiresFramework, StringComparer.OrdinalIgnoreCase);

    public static AcademyScore Score(AcademyModuleContent module, IReadOnlyList<int>? answers)
    {
        var given = answers ?? [];
        var wrong = new List<int>();
        for (var index = 0; index < module.Questions.Count; index++)
        {
            if (index >= given.Count || given[index] != module.Questions[index].Answer)
            {
                wrong.Add(index);
            }
        }

        var correct = module.Questions.Count - wrong.Count;
        return new AcademyScore(correct, module.Questions.Count, correct >= PassMark, wrong);
    }

    public static bool IsValidAudience(string audience)
    {
        var value = (audience ?? "").Trim();
        if (value == Everyone)
        {
            return true;
        }

        var sep = value.IndexOf(':', StringComparison.Ordinal);
        if (sep <= 0 || value.Length > 120)
        {
            return false;
        }

        var kind = value[..sep];
        return (kind is "Department" or "Role") && value[(sep + 1)..].Trim().Length > 0;
    }

    public static bool Matches(string audience, string department, string role)
    {
        var value = (audience ?? "").Trim();
        if (value == Everyone)
        {
            return true;
        }

        var sep = value.IndexOf(':', StringComparison.Ordinal);
        if (sep <= 0)
        {
            return false;
        }

        var target = value[(sep + 1)..].Trim();
        return value[..sep] switch
        {
            "Department" => string.Equals(target, (department ?? "").Trim(), StringComparison.OrdinalIgnoreCase),
            "Role" => string.Equals(target, (role ?? "").Trim(), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static bool IsAssigned(IEnumerable<string> audiences, string department, string role) =>
        audiences.Any(audience => Matches(audience, department, role));

    public static DateOnly ExpiresOn(DateOnly passedOn) => passedOn.AddMonths(RenewalMonths);

    public static bool IsCurrent(DateOnly? expiresOn, DateOnly today) => expiresOn is DateOnly expires && expires >= today;

    public static string Status(AcademyProgress? progress, DateOnly today)
    {
        if (progress is null || (progress.PassedAt is null && progress.SectionsCompleted == 0 && progress.Attempts == 0))
        {
            return "NotStarted";
        }

        if (progress.PassedAt is not null)
        {
            return IsCurrent(progress.ExpiresOn, today) ? "Passed" : "Expired";
        }

        return "InProgress";
    }

    public static bool AllCurrent(IEnumerable<string> requiredKeys, IEnumerable<AcademyProgress> progress, DateOnly today)
    {
        var current = progress.Where(item => IsCurrent(item.ExpiresOn, today)).Select(item => item.ModuleKey).ToHashSet(StringComparer.Ordinal);
        var required = requiredKeys.ToArray();
        return required.Length > 0 && required.All(current.Contains);
    }

    public static string CertificateNumber(Guid tenantId, string moduleKey, string email, DateOnly passedOn)
    {
        var raw = $"{tenantId:N}|{moduleKey}|{email.Trim().ToLowerInvariant()}|{passedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return "D2D-" + passedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + hash[..10];
    }

    public static IReadOnlyList<string> CertificateLines(string companyName, string personName, AcademyModuleContent module, AcademyProgress progress)
    {
        var passed = progress.PassedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        var expires = progress.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        return
        [
            "Certificate of completion",
            "",
            companyName + " security awareness academy",
            "",
            "This certifies that " + personName + " (" + progress.PersonEmail + ")",
            "completed the module: " + module.Title,
            "Quiz score: " + progress.BestScore.ToString(CultureInfo.InvariantCulture) + " of " + module.Questions.Count.ToString(CultureInfo.InvariantCulture) + " (pass mark " + PassMark.ToString(CultureInfo.InvariantCulture) + ")",
            "Completed on: " + passed,
            "Renewal due by: " + expires,
            "Certificate number: " + progress.CertificateNumber,
            "",
            "Issued by D2D Audit Services. Evidence is stored under control HR-06.",
        ];
    }
}
