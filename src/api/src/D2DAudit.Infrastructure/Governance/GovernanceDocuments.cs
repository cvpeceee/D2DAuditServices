using System.Globalization;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Infrastructure.Audits;

namespace D2DAudit.Infrastructure.Governance;

internal static class GovernanceDocuments
{
    public static FileExport Risks(string format, IReadOnlyList<RiskListItem> rows)
    {
        var lines = new List<string> { "ISO 27001 risk assessment and treatment plan", "" };
        lines.AddRange(rows.Select(row =>
            $"{row.Code}  {row.Likelihood}x{row.Impact}={row.Score} {row.Band}  residual {row.ResidualLikelihood}x{row.ResidualImpact}={row.ResidualScore}  {row.Treatment}  review {row.ReviewOn:yyyy-MM-dd}  {row.Title}"));
        return format == "pdf"
            ? new FileExport("iso27001-risk-treatment-plan.pdf", "application/pdf", PlainPdf.Document(lines))
            : new FileExport("iso27001-risk-treatment-plan.csv", "text/csv", Encoding.UTF8.GetBytes(RiskCsv(rows)));
    }

    public static FileExport Soa(string format, IReadOnlyList<SoaRow> rows)
    {
        var lines = new List<string> { "ISO 27001 statement of applicability", "" };
        lines.AddRange(rows.Select(row =>
            $"{row.Code}  {(row.Applicable ? "applicable" : "not applicable")}  {row.Implementation}  {row.Title}"));
        return format == "pdf"
            ? new FileExport("iso27001-statement-of-applicability.pdf", "application/pdf", PlainPdf.Document(lines))
            : new FileExport("iso27001-statement-of-applicability.csv", "text/csv", Encoding.UTF8.GetBytes(SoaCsv(rows)));
    }

    public static string ReviewCsv(IReadOnlyList<AccessItemDto> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("person,email,provider,account,decision");
        foreach (var item in items)
        {
            builder.Append(Cell(item.PersonName)).Append(',')
                .Append(Cell(item.PersonEmail)).Append(',')
                .Append(Cell(item.Provider)).Append(',')
                .Append(Cell(item.AccountName)).Append(',')
                .Append(Cell(item.Decision))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string RiskCsv(IReadOnlyList<RiskListItem> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("code,title,statement,likelihood,impact,score,band,owner,treatment,treatmentPlan,controls,reviewOn,residualLikelihood,residualImpact,residualScore");
        foreach (var row in rows)
        {
            builder.Append(Cell(row.Code)).Append(',')
                .Append(Cell(row.Title)).Append(',')
                .Append(Cell(row.Statement)).Append(',')
                .Append(row.Likelihood.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Impact.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Score.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Cell(row.Band)).Append(',')
                .Append(Cell(row.OwnerName)).Append(',')
                .Append(Cell(row.Treatment)).Append(',')
                .Append(Cell(row.TreatmentPlan)).Append(',')
                .Append(Cell(string.Join(' ', row.ControlCodes))).Append(',')
                .Append(row.ReviewOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ResidualLikelihood.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ResidualImpact.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ResidualScore.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string SoaCsv(IReadOnlyList<SoaRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("code,title,applicable,justification,implementation");
        foreach (var row in rows)
        {
            builder.Append(Cell(row.Code)).Append(',')
                .Append(Cell(row.Title)).Append(',')
                .Append(row.Applicable ? "yes" : "no").Append(',')
                .Append(Cell(row.Justification)).Append(',')
                .Append(Cell(row.Implementation))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Cell(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
