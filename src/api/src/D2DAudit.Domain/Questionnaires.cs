using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace D2DAudit.Domain;

public sealed class SecurityQuestionnaire
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string CustomerName { get; set; } = "";

    public string Status { get; set; } = "InReview";

    public DateTimeOffset CreatedAt { get; set; }

    public List<QuestionnaireAnswer> Answers { get; set; } = [];
}

public sealed class QuestionnaireAnswer
{
    public Guid Id { get; set; }

    public Guid QuestionnaireId { get; set; }

    public SecurityQuestionnaire? Questionnaire { get; set; }

    public int Position { get; set; }

    public string Question { get; set; } = "";

    public string Suggestion { get; set; } = "";

    public string Citation { get; set; } = "";

    public string Answer { get; set; } = "";

    public string ReviewerName { get; set; } = "";

    public bool Accepted { get; set; }
}

public static class QuestionnaireSuggest
{
    public const string EmptyAnswer = "No matching policy, control, or evidence was found.";

    public static (string Answer, string Citation) Suggest(string question, IReadOnlyList<(string Citation, string Text)> knowledge)
    {
        var words = Words(question);
        var bestScore = 0;
        var bestCitation = "";
        var bestText = "";
        foreach (var item in knowledge)
        {
            var score = Score(words, Words(item.Citation + " " + item.Text));
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestCitation = item.Citation;
            bestText = item.Text;
        }

        if (bestScore == 0 || bestCitation.Length == 0)
        {
            return (EmptyAnswer, "");
        }

        var excerpt = bestText.Trim();
        if (excerpt.Length > 240)
        {
            excerpt = excerpt[..240];
        }

        return ("From " + bestCitation + ": " + excerpt, bestCitation);
    }

    public static IReadOnlyList<string> Questions(string fileName, byte[] body)
    {
        var spreadsheet = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        var zip = body.Length >= 2 && body[0] == (byte)'P' && body[1] == (byte)'K';
        if (spreadsheet || zip)
        {
            return FromXlsx(body);
        }

        return FromCsv(Encoding.UTF8.GetString(body));
    }

    private static int Score(HashSet<string> left, HashSet<string> right) => left.Count(right.Contains);

    private static HashSet<string> Words(string value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            Keep(set, current);
        }

        Keep(set, current);
        return set;
    }

    private static void Keep(HashSet<string> set, StringBuilder current)
    {
        if (current.Length > 3)
        {
            set.Add(current.ToString());
        }

        current.Clear();
    }

    private static IReadOnlyList<string> FromCsv(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            rows.Add(SplitCsv(line));
        }

        return Column(rows);
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var index = 0;
        while (index < line.Length)
        {
            var character = line[index];
            index++;
            if (character == '"')
            {
                var escaped = quoted && index < line.Length && line[index] == '"';
                if (escaped)
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (character == ',' && !quoted)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static IReadOnlyList<string> FromXlsx(byte[] body)
    {
        using var zip = new ZipArchive(new MemoryStream(body), ZipArchiveMode.Read);
        var shared = SharedStrings(zip);
        var sheet = zip.Entries.FirstOrDefault(entry =>
            entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            return [];
        }

        using var stream = sheet.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var column = Column(cell.Attribute("r")?.Value);
                while (cells.Count < column)
                {
                    cells.Add("");
                }

                cells.Add(CellText(cell, ns, shared));
            }

            rows.Add(cells);
        }

        return Column(rows);
    }

    private static List<string> SharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si").Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToList();
    }

    private static string CellText(XElement cell, XNamespace ns, List<string> shared)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value)).Trim();
        }

        var value = cell.Element(ns + "v")?.Value ?? "";
        if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < shared.Count)
        {
            return shared[index].Trim();
        }

        return value.Trim();
    }

    private static int Column(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return 0;
        }

        var column = 0;
        foreach (var character in reference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = (column * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return Math.Max(0, column - 1);
    }

    private static IReadOnlyList<string> Column(List<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0].ToList();
        var column = header.FindIndex(cell => cell.Equals("question", StringComparison.OrdinalIgnoreCase));
        var start = 0;
        if (column >= 0)
        {
            start = 1;
        }
        else
        {
            column = 0;
        }

        var questions = new List<string>();
        for (var index = start; index < rows.Count && questions.Count < 200; index++)
        {
            if (column >= rows[index].Count)
            {
                continue;
            }

            var question = rows[index][column].Trim();
            if (question.Length is > 0 and <= 1000)
            {
                questions.Add(question);
            }
        }

        return questions;
    }
}
