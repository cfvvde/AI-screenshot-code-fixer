using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
namespace SharpLensAI.Services;
// Offline rendering: no HTML, remote images, navigation or executable content.
public static class MarkdownRenderer
{
    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, PagePadding = new Thickness(8) };
        bool code = false; var buffer = new System.Text.StringBuilder();
        foreach (string line in markdown.Replace("\r", "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (code) { AddCode(); } code = !code; continue;
            }
            if (code) { buffer.AppendLine(line); continue; }
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            string text = line;
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success) { text = heading.Groups[2].Value; p.FontSize = 25 - heading.Groups[1].Length * 2; p.FontWeight = FontWeights.SemiBold; }
            if (text.StartsWith("- ") || text.StartsWith("* ")) text = "• " + text[2..];
            foreach (string part in Regex.Split(text, @"(\*\*.*?\*\*|`[^`]+`)"))
            {
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) p.Inlines.Add(new Bold(new Run(part[2..^2])));
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length >= 2) p.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas"), Background = Brushes.AliceBlue });
                else p.Inlines.Add(new Run(part));
            }
            doc.Blocks.Add(p);
        }
        if (buffer.Length > 0) AddCode();
        return doc;
        void AddCode() { doc.Blocks.Add(new Paragraph(new Run(buffer.ToString().TrimEnd('\r', '\n'))) { FontFamily = new FontFamily("Consolas"), FontSize = 13, Background = new SolidColorBrush(Color.FromRgb(237, 242, 249)), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 12) }); buffer.Clear(); }
    }
}
