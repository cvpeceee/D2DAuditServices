using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Questionnaires;

public sealed class DeterministicQuestionnaireAssistant : IQuestionnaireAssistant
{
    public (string Answer, string Citation) Suggest(string question, IReadOnlyList<(string Citation, string Text)> knowledge) =>
        QuestionnaireSuggest.Suggest(question, knowledge);
}
