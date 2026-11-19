using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Questionnaires;

public sealed class QuestionnairePortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    IQuestionnaireAssistant assistant,
    TimeProvider time) : IQuestionnaires
{
    public async Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Questionnaires.AsNoTracking().Include(item => item.Answers).OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToArray();
    }

    public async Task<Result<QuestionnaireDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.Questionnaires.AsNoTracking().Include(item => item.Answers).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? Result<QuestionnaireDetail>.Fail("not_found") : Result<QuestionnaireDetail>.Ok(ToDetail(row));
    }

    public async Task<Result<QuestionnaireDetail>> UploadAsync(UploadQuestionnaireRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<QuestionnaireDetail>.Fail("forbidden");
        }

        var name = (request.Name ?? "").Trim();
        var customer = (request.CustomerName ?? "").Trim();
        var fileName = (request.FileName ?? "").Trim();
        if (name.Length is 0 or > 200 || customer.Length is 0 or > 200 || fileName.Length is 0 or > 200)
        {
            return Result<QuestionnaireDetail>.Fail("invalid_questionnaire");
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(request.ContentBase64 ?? "");
        }
        catch (FormatException)
        {
            return Result<QuestionnaireDetail>.Fail("invalid_questionnaire");
        }

        if (body.Length is 0 or > 750_000)
        {
            return Result<QuestionnaireDetail>.Fail("invalid_questionnaire");
        }

        var questions = QuestionnaireSuggest.Questions(fileName, body);
        if (questions.Count == 0)
        {
            return Result<QuestionnaireDetail>.Fail("invalid_questionnaire");
        }

        await using var db = Open();
        var knowledge = await KnowledgeAsync(db, cancellationToken);
        var now = time.GetUtcNow();
        var row = new SecurityQuestionnaire
        {
            Id = Guid.NewGuid(),
            Name = name,
            CustomerName = customer,
            Status = "InReview",
            CreatedAt = now,
        };
        var position = 1;
        foreach (var question in questions)
        {
            var suggested = assistant.Suggest(question, knowledge);
            var suggestion = Limit(suggested.Answer, 4000);
            row.Answers.Add(new QuestionnaireAnswer
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = row.Id,
                Position = position,
                Question = question,
                Suggestion = suggestion,
                Citation = Limit(suggested.Citation, 300),
                Answer = suggestion,
            });
            position++;
        }

        db.Questionnaires.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "questionnaire.uploaded", "ok", row.Name, null, cancellationToken);
        return Result<QuestionnaireDetail>.Ok(ToDetail(row));
    }

    public async Task<Result<QuestionnaireAnswerItem>> ReviewAsync(Guid id, Guid answerId, ReviewAnswerRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<QuestionnaireAnswerItem>.Fail("forbidden");
        }

        var answer = (request.Answer ?? "").Trim();
        if (answer.Length > 4000)
        {
            return Result<QuestionnaireAnswerItem>.Fail("invalid_questionnaire");
        }

        await using var db = Open();
        var row = await db.Questionnaires.Include(item => item.Answers).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        var item = row?.Answers.FirstOrDefault(entry => entry.Id == answerId);
        if (row is null || item is null)
        {
            return Result<QuestionnaireAnswerItem>.Fail("not_found");
        }

        if (request.Answer is not null)
        {
            item.Answer = answer;
        }

        if (request.Accepted is bool accepted)
        {
            item.Accepted = accepted;
            item.ReviewerName = accepted ? tenant.DisplayName : "";
        }

        row.Status = row.Answers.All(entry => entry.Accepted) ? "Accepted" : "InReview";
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "questionnaire.reviewed", "ok", item.Position.ToString(), null, cancellationToken);
        return Result<QuestionnaireAnswerItem>.Ok(ToAnswer(item));
    }

    public async Task<Result<FileExport>> ExportAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.Questionnaires.AsNoTracking().Include(item => item.Answers).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<FileExport>.Fail("not_found");
        }

        var csv = new StringBuilder();
        csv.AppendLine("position,question,suggestion,citation,answer,accepted");
        foreach (var item in row.Answers.OrderBy(entry => entry.Position))
        {
            csv.Append(item.Position.ToString()).Append(',')
                .Append(Cell(item.Question)).Append(',')
                .Append(Cell(item.Suggestion)).Append(',')
                .Append(Cell(item.Citation)).Append(',')
                .Append(Cell(item.Answer)).Append(',')
                .Append(item.Accepted ? "yes" : "no")
                .AppendLine();
        }

        var file = "questionnaire-" + row.Id.ToString("N") + ".csv";
        return Result<FileExport>.Ok(new FileExport(file, "text/csv", Encoding.UTF8.GetBytes(csv.ToString())));
    }

    internal static async Task<IReadOnlyList<(string Citation, string Text)>> KnowledgeAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var knowledge = new List<(string Citation, string Text)>();
        var policies = await db.Policies.AsNoTracking().Include(item => item.Versions).ToListAsync(cancellationToken);
        foreach (var policy in policies)
        {
            var version = policy.Versions.OrderByDescending(item => item.Number).FirstOrDefault();
            if (version is null || version.Body.Length == 0)
            {
                continue;
            }

            knowledge.Add(("Policy " + policy.Code + " " + policy.Title, Clip(version.Body)));
        }

        var controls = await db.Controls.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var control in controls)
        {
            knowledge.Add(("Control " + control.Code + " " + control.Title, Clip(control.Description)));
        }

        var evidence = await db.Evidence.AsNoTracking().OrderByDescending(item => item.CollectedAt).Take(40).ToListAsync(cancellationToken);
        foreach (var item in evidence)
        {
            knowledge.Add(("Evidence " + item.Title, Clip(item.Title + " " + item.Source)));
        }

        return knowledge;
    }

    private static string Limit(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static string Clip(string value)
    {
        var text = (value ?? "").Trim();
        return text.Length <= 400 ? text : text[..400];
    }

    private static string Cell(string value) => CsvSafety.EscapeCell(value);

    private static QuestionnaireSummary ToSummary(SecurityQuestionnaire row) => new(
        row.Id,
        row.Name,
        row.CustomerName,
        row.Status,
        row.CreatedAt,
        row.Answers.Count,
        row.Answers.Count(item => item.Accepted));

    private static QuestionnaireDetail ToDetail(SecurityQuestionnaire row) => new(
        row.Id,
        row.Name,
        row.CustomerName,
        row.Status,
        row.CreatedAt,
        row.Answers.OrderBy(item => item.Position).Select(ToAnswer).ToArray());

    private static QuestionnaireAnswerItem ToAnswer(QuestionnaireAnswer row) => new(
        row.Id,
        row.Position,
        row.Question,
        row.Suggestion,
        row.Citation,
        row.Answer,
        row.ReviewerName,
        row.Accepted);

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
