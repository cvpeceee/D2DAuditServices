using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Questionnaires;

public sealed class GroundedQuestionnaireAssistant(IAssistantProvider provider) : IQuestionnaireAssistant
{
    public (string Answer, string Citation) Suggest(string question, IReadOnlyList<(string Citation, string Text)> knowledge) =>
        QuestionnaireSuggest.Suggest(question, knowledge);

    public async Task<(string Answer, string Citation)> SuggestAsync(
        string question,
        IReadOnlyList<(string Citation, string Text)> knowledge,
        string workspaceLabel,
        CancellationToken cancellationToken)
    {
        var anchor = QuestionnaireSuggest.Suggest(question, knowledge);
        if (knowledge.Count == 0)
        {
            return anchor;
        }

        var facts = knowledge
            .Take(40)
            .Select((item, idx) => ToFact(item.Citation, item.Text, idx))
            .ToArray();

        var scope = string.IsNullOrWhiteSpace(workspaceLabel) ? "workspace knowledge base" : workspaceLabel;
        var reply = await provider.AnswerAsync(
            $"Answer this customer security questionnaire question concisely in 1-3 sentences, grounded strictly in the provided workspace policies, controls, and evidence, and preserve the citation: {question}",
            facts,
            scope,
            cancellationToken);

        if (!string.Equals(reply.Mode, "llm", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(reply.Answer))
        {
            return anchor;
        }

        var citation = ResolveCitation(anchor.Citation, reply.Citations);
        var trimmed = reply.Answer.Trim();
        var answer = !string.IsNullOrWhiteSpace(citation) && !trimmed.Contains(citation, StringComparison.OrdinalIgnoreCase)
            ? $"{trimmed} [{citation}]"
            : trimmed;
        return (answer, citation);
    }

    private static string ResolveCitation(string anchorCitation, IReadOnlyList<AssistantCitation> citations)
    {
        if (!string.IsNullOrWhiteSpace(anchorCitation))
        {
            return anchorCitation;
        }

        var first = citations.FirstOrDefault();
        return first is null ? "" : $"{first.Kind} {first.Code} {first.Title}".Trim();
    }

    private static AssistantFact ToFact(string citation, string text, int index)
    {
        var kind = "Evidence";
        if (citation.StartsWith("Policy ", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Policy";
        }
        else if (citation.StartsWith("Control ", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Control";
        }

        var parts = citation.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var code = parts.Length >= 2 ? parts[1] : $"KB-{index + 1}";
        var title = parts.Length >= 3 ? parts[2] : citation;
        var href = kind switch
        {
            "Policy" => "/policies",
            "Control" => $"/controls/{Uri.EscapeDataString(code)}",
            _ => "/evidence",
        };
        return new AssistantFact(kind, code, title, text, href);
    }
}
