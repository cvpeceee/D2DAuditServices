using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Questionnaires;

internal static class QuestionnaireExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Questionnaires.Local.Any() || await db.Questionnaires.AnyAsync(cancellationToken))
        {
            return;
        }

        var knowledge = await QuestionnairePortal.KnowledgeAsync(db, cancellationToken);
        var questions = new[]
        {
            "Is production data encrypted at rest?",
            "Are production changes reviewed before they ship?",
            "How does an employee report a security incident?",
        };
        var row = new SecurityQuestionnaire
        {
            Id = Guid.NewGuid(),
            Name = "Harborline security questionnaire",
            CustomerName = "Northgate Assurance",
            Status = "InReview",
            CreatedAt = now.AddDays(-3),
        };
        var position = 1;
        foreach (var question in questions)
        {
            var suggested = QuestionnaireSuggest.Suggest(question, knowledge);
            var accepted = position != 2;
            var suggestion = suggested.Answer.Length <= 4000 ? suggested.Answer : suggested.Answer[..4000];
            var citation = suggested.Citation.Length <= 300 ? suggested.Citation : suggested.Citation[..300];
            row.Answers.Add(new QuestionnaireAnswer
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = row.Id,
                Position = position,
                Question = question,
                Suggestion = suggestion,
                Citation = citation,
                Answer = suggestion,
                ReviewerName = accepted ? "Chandra Vell" : "",
                Accepted = accepted,
            });
            position++;
        }

        db.Questionnaires.Add(row);
    }
}
