using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class AgentAcademyTests
{
    [Fact]
    public void Enrollment_token_round_trips_tenant_id_and_hashes_constantly()
    {
        var tenantId = Guid.NewGuid();
        var token = DeviceAgentTokens.CreateEnrollmentToken(tenantId);

        Assert.StartsWith(DeviceAgentTokens.EnrollmentPrefix, token, StringComparison.Ordinal);
        Assert.True(DeviceAgentTokens.TryParseEnrollmentToken(token, out var parsed));
        Assert.Equal(tenantId, parsed);
        Assert.StartsWith("…", DeviceAgentTokens.Hint(token), StringComparison.Ordinal);

        var hash = DeviceAgentTokens.Hash(token);
        Assert.Equal(64, hash.Length);
        Assert.True(DeviceAgentTokens.HashEquals(hash, DeviceAgentTokens.Hash("  " + token + "  ")));
        Assert.False(DeviceAgentTokens.HashEquals(hash, DeviceAgentTokens.Hash(token + "x")));
        Assert.False(DeviceAgentTokens.TryParseEnrollmentToken("d2de_not_a_guid", out _));
        Assert.False(DeviceAgentTokens.TryParseEnrollmentToken("wrong_prefix", out _));
    }

    [Fact]
    public void Device_id_round_trips_tenant_and_credential_ids()
    {
        var tenantId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var deviceId = DeviceAgentTokens.DeviceId(tenantId, credentialId);

        Assert.True(DeviceAgentTokens.TryParseDeviceId(deviceId, out var parsedTenant, out var parsedCred));
        Assert.Equal(tenantId, parsedTenant);
        Assert.Equal(credentialId, parsedCred);
        Assert.False(DeviceAgentTokens.TryParseDeviceId("d2dd_short", out _, out _));
    }

    [Fact]
    public void Enrollment_token_state_and_usability_follow_lifecycle()
    {
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var active = new DeviceEnrollmentToken { ExpiresAt = now.AddHours(12) };
        Assert.True(DeviceAgentTokens.IsUsable(active, now));
        Assert.Equal("Active", DeviceAgentTokens.State(active, now));

        var expired = new DeviceEnrollmentToken { ExpiresAt = now.AddMinutes(-1) };
        Assert.False(DeviceAgentTokens.IsUsable(expired, now));
        Assert.Equal("Expired", DeviceAgentTokens.State(expired, now));

        var used = new DeviceEnrollmentToken { ExpiresAt = now.AddHours(12), UsedAt = now.AddMinutes(-5) };
        Assert.False(DeviceAgentTokens.IsUsable(used, now));
        Assert.Equal("Used", DeviceAgentTokens.State(used, now));

        var revoked = new DeviceEnrollmentToken { ExpiresAt = now.AddHours(12), RevokedAt = now.AddMinutes(-2) };
        Assert.False(DeviceAgentTokens.IsUsable(revoked, now));
        Assert.Equal("Revoked", DeviceAgentTokens.State(revoked, now));
    }

    [Fact]
    public void Device_signature_verifies_hmac_window_and_anti_replay()
    {
        var secret = DeviceAgentTokens.CreateSecret();
        var key = DeviceSignature.KeyFromSecret(secret);
        var body = """{"schemaVersion":1,"serial":"SN-1"}""";
        var now = 1_795_262_400L;

        var signature = DeviceSignature.Sign(key, now, body);
        Assert.StartsWith(DeviceSignature.Scheme, signature, StringComparison.Ordinal);

        Assert.Equal(SignatureCheck.Valid, DeviceSignature.Verify(key, now.ToString(), body, signature, now, now - 10));
        Assert.Equal(SignatureCheck.Replayed, DeviceSignature.Verify(key, now.ToString(), body, signature, now, now));
        Assert.Equal(SignatureCheck.Invalid, DeviceSignature.Verify(key, now.ToString(), body + " ", signature, now, 0));
        Assert.Equal(SignatureCheck.Missing, DeviceSignature.Verify(key, null, body, signature, now, 0));

        var staleTs = now - DeviceSignature.WindowSeconds - 5;
        var staleSig = DeviceSignature.Sign(key, staleTs, body);
        Assert.Equal(SignatureCheck.Expired, DeviceSignature.Verify(key, staleTs.ToString(), body, staleSig, now, 0));
    }

    [Fact]
    public void Device_posture_parses_validates_applies_and_computes_outcomes()
    {
        var valid = JsonSerializer.Serialize(new AgentPayload(
            1,
            "1.0.0",
            "dotnet",
            "  HL-LAP-99  ",
            " dev-mbp ",
            " macOS 15.1 ",
            "macos",
            DateTimeOffset.UtcNow,
            [
                new PostureCheck("diskEncryption", true, "FileVault On"),
                new PostureCheck("screenLock", false, "Timeout 30m"),
                new PostureCheck("antivirus", true, "XProtect active"),
                new PostureCheck("passwordManager", true, "1Password"),
                new PostureCheck("osUpdates", true, "Up to date"),
                new PostureCheck("firewall", true, "Application Firewall enabled"),
            ]));

        var (payload, error) = DevicePosture.Parse(valid);
        Assert.Null(error);
        Assert.NotNull(payload);
        Assert.Equal("HL-LAP-99", payload.Serial);
        Assert.Equal("dev-mbp", payload.Hostname);

        var computer = new ComputerRecord();
        DevicePosture.Apply(computer, payload.Checks);
        Assert.True(computer.Encrypted);
        Assert.False(computer.ScreenLock);
        Assert.True(computer.Antivirus);
        Assert.True(computer.PasswordManager);
        Assert.True(computer.AutoUpdate);

        var (screenOutcome, screenDetail) = DevicePosture.CheckOutcome("screenLock", [(payload.Serial, payload.Checks)]);
        Assert.Equal(TestOutcome.Fail, screenOutcome);
        Assert.Contains("HL-LAP-99", screenDetail, StringComparison.Ordinal);

        var (encOutcome, _) = DevicePosture.CheckOutcome("diskEncryption", [(payload.Serial, payload.Checks)]);
        Assert.Equal(TestOutcome.Pass, encOutcome);

        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TestOutcome.NotRun, DevicePosture.StaleOutcome([], now).Outcome);
        Assert.Equal(TestOutcome.Pass, DevicePosture.StaleOutcome([("SN-1", now.AddDays(-3))], now).Outcome);
        Assert.Equal(TestOutcome.Fail, DevicePosture.StaleOutcome([("SN-1", now.AddDays(-9))], now).Outcome);

        foreach (var check in DevicePosture.Checks)
        {
            Assert.NotEmpty(DevicePosture.Remediation(check.Key, "windows"));
            Assert.NotEmpty(DevicePosture.Remediation(check.Key, "macos"));
            Assert.NotEmpty(DevicePosture.Remediation(check.Key, "linux"));
        }
    }

    [Fact]
    public void Device_posture_rejects_bad_schema_duplicate_checks_and_unknown_keys()
    {
        var badSchema = JsonSerializer.Serialize(new AgentPayload(2, "1.0", "dotnet", "SN-1", "h", "Win 11", "windows", null, [new PostureCheck("diskEncryption", true, "ok")]));
        Assert.Equal("unsupported_schema", DevicePosture.Parse(badSchema).Error);

        var badKind = JsonSerializer.Serialize(new AgentPayload(1, "1.0", "ruby", "SN-1", "h", "Win 11", "windows", null, [new PostureCheck("diskEncryption", true, "ok")]));
        Assert.Equal("invalid_payload", DevicePosture.Parse(badKind).Error);

        var dupChecks = JsonSerializer.Serialize(new AgentPayload(1, "1.0", "powershell", "SN-1", "h", "Win 11", "windows", null, [
            new PostureCheck("diskEncryption", true, "ok"),
            new PostureCheck("diskEncryption", true, "dup"),
        ]));
        Assert.Equal("invalid_checks", DevicePosture.Parse(dupChecks).Error);

        var unknownCheck = JsonSerializer.Serialize(new AgentPayload(1, "1.0", "bash", "SN-1", "h", "Ubuntu 24.04", "linux", null, [new PostureCheck("unknownKey", true, "x")]));
        Assert.Equal("invalid_checks", DevicePosture.Parse(unknownCheck).Error);

        Assert.Equal(AgentAuthFailure.Replayed, AgentCall<string>.Denied(AgentAuthFailure.Replayed).Auth);
        Assert.Equal("bad", AgentCall<string>.Fail("bad").Error);
    }

    [Fact]
    public void Security_academy_catalog_scoring_audiences_and_certificates_work()
    {
        Assert.Equal(7, SecurityAcademy.Modules.Count);
        Assert.All(SecurityAcademy.Modules, module =>
        {
            Assert.True(module.Sections.Count >= 4);
            Assert.Equal(5, module.Questions.Count);
        });

        var hipaa = SecurityAcademy.Find("hipaa-privacy-security");
        Assert.NotNull(hipaa);
        Assert.False(SecurityAcademy.Available(hipaa, ["soc2", "iso27001"]));
        Assert.True(SecurityAcademy.Available(hipaa, ["soc2", "HIPAA"]));

        var basics = SecurityAcademy.Find("security-basics")!;
        var perfect = basics.Questions.Select(q => q.Answer).ToArray();
        var passResult = SecurityAcademy.Score(basics, perfect);
        Assert.True(passResult.Passed);
        Assert.Equal(5, passResult.Correct);
        Assert.Empty(passResult.Wrong);

        var fourOfFive = perfect.ToArray();
        fourOfFive[0] = (fourOfFive[0] + 1) % 4;
        var boundary = SecurityAcademy.Score(basics, fourOfFive);
        Assert.True(boundary.Passed);
        Assert.Equal(4, boundary.Correct);
        Assert.Equal([0], boundary.Wrong);

        var failAnswers = perfect.Select(a => (a + 1) % 4).ToArray();
        var failResult = SecurityAcademy.Score(basics, failAnswers);
        Assert.False(failResult.Passed);
        Assert.Equal(0, failResult.Correct);

        Assert.True(SecurityAcademy.IsValidAudience("Everyone"));
        Assert.True(SecurityAcademy.IsValidAudience("Department:Engineering"));
        Assert.True(SecurityAcademy.IsValidAudience("Role:Admin"));
        Assert.False(SecurityAcademy.IsValidAudience("Team:Blue"));
        Assert.False(SecurityAcademy.IsValidAudience("Department:"));

        Assert.True(SecurityAcademy.IsAssigned(["Department:Engineering"], "engineering", "Engineer"));
        Assert.False(SecurityAcademy.IsAssigned(["Department:Engineering"], "Finance", "Analyst"));

        var today = new DateOnly(2026, 11, 21);
        var expires = SecurityAcademy.ExpiresOn(today);
        Assert.Equal(new DateOnly(2027, 11, 21), expires);
        Assert.True(SecurityAcademy.IsCurrent(expires, today));
        Assert.False(SecurityAcademy.IsCurrent(today.AddDays(-1), today));

        Assert.Equal("NotStarted", SecurityAcademy.Status(null, today));
        Assert.Equal("InProgress", SecurityAcademy.Status(new AcademyProgress { SectionsCompleted = 2 }, today));
        Assert.Equal("Passed", SecurityAcademy.Status(new AcademyProgress { PassedAt = DateTimeOffset.UtcNow, ExpiresOn = expires }, today));
        Assert.Equal("Expired", SecurityAcademy.Status(new AcademyProgress { PassedAt = DateTimeOffset.UtcNow.AddYears(-2), ExpiresOn = today.AddDays(-1) }, today));

        var tenantId = Guid.NewGuid();
        var cert1 = SecurityAcademy.CertificateNumber(tenantId, basics.Key, "Chandra.Vell@harborline.example", today);
        var cert2 = SecurityAcademy.CertificateNumber(tenantId, basics.Key, "chandra.vell@harborline.example", today);
        Assert.Equal(cert1, cert2);
        Assert.StartsWith("D2D-20261121-", cert1, StringComparison.Ordinal);

        var progress = new AcademyProgress
        {
            ModuleKey = basics.Key,
            PersonEmail = "chandra.vell@harborline.example",
            BestScore = 5,
            PassedAt = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
            ExpiresOn = expires,
            CertificateNumber = cert1,
        };
        Assert.True(SecurityAcademy.AllCurrent([basics.Key], [progress], today));
        var lines = SecurityAcademy.CertificateLines("Harborline Data Systems", "Chandra Vell", basics, progress);
        Assert.Contains(lines, line => line.Contains(cert1, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("HR-06", StringComparison.Ordinal));
    }
}
