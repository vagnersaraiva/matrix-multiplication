using System.Globalization;
using System.Text;

namespace MatrixMultiply.Core.Orchestration;

public static class TimingReport
{
    public static string Format(RunReport report)
    {
        var text = new StringBuilder();
        text.Append("Init Message: ").AppendLine(report.InitMessage);
        text.AppendLine();
        text.AppendLine("Get A & B took");
        text.AppendLine(report.FetchMilliseconds.ToString(CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("Multiply took");
        text.AppendLine(report.MultiplyMilliseconds.ToString(CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("Validate Result");
        text.AppendLine(report.ValidateResult);
        text.AppendLine();
        text.AppendLine("Validate took");
        text.AppendLine(report.ValidateMilliseconds.ToString(CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("Total took");
        text.AppendLine(report.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
