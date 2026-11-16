using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class QuestionnaireSuggestTests
{
    [Fact]
    public void A_question_cites_the_closest_control()
    {
        var knowledge = new (string Citation, string Text)[]
        {
            ("Control CR-02 Encryption at rest", "Production data is encrypted at rest."),
            ("Control IR-02 Incident response", "An employee reports a security incident the same day."),
            ("Policy POL-CHG Change management", "A production change is reviewed before it ships."),
        };
        var encrypted = QuestionnaireSuggest.Suggest("Is production data encrypted at rest?", knowledge);
        var incident = QuestionnaireSuggest.Suggest("How does an employee report a security incident?", knowledge);
        var unknown = QuestionnaireSuggest.Suggest("What color is the lobby couch?", knowledge);
        var csv = QuestionnaireSuggest.Questions("questions.csv", "question,answer\nIs MFA required?\n"u8.ToArray());

        Assert.Contains("CR-02", encrypted.Citation, StringComparison.Ordinal);
        Assert.StartsWith("From Control CR-02", encrypted.Answer, StringComparison.Ordinal);
        Assert.Contains("IR-02", incident.Citation, StringComparison.Ordinal);
        Assert.Equal(QuestionnaireSuggest.EmptyAnswer, unknown.Answer);
        Assert.Equal("", unknown.Citation);
        Assert.Equal(["Is MFA required?"], csv);
    }
}
