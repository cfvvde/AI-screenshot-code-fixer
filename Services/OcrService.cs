using System.IO;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
namespace SharpLensAI.Services;
public sealed class OcrResultModel
{
    public string Text { get; set; } = "";
    public string UsedLanguage { get; set; } = "";
    public string Warning { get; set; } = "";
}
public sealed class OcrService
{
    public async Task<OcrResultModel> RecognizeTextAsync(System.Drawing.Bitmap bitmap, string targetLang)
    {
        using var memory = new MemoryStream(); bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png); memory.Position = 0;
        using var stream = memory.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        double scale = Math.Min(1, OcrEngine.MaxImageDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, bitmap.Width * scale), ScaledHeight = (uint)Math.Max(1, bitmap.Height * scale) };
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var tags = targetLang switch { "ru" => new[] { "ru-RU" }, "en" => new[] { "en-US" }, _ => new[] { "ru-RU", "en-US" } };
        var candidates = new List<(string Tag, OcrResult Result)>();
        var missing = new List<string>();
        foreach (string tag in tags)
        {
            var engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
            if (engine == null) { missing.Add(tag); continue; }
            candidates.Add((tag, await engine.RecognizeAsync(softwareBitmap)));
        }
        if (candidates.Count == 0) throw new InvalidOperationException("Установите в параметрах Windows языковой компонент OCR: " + string.Join(", ", tags) + ".");
        IEnumerable<OcrLine> lines = candidates[0].Result.Lines;
        if (targetLang == "auto" && candidates.Count == 2)
        {
            var russian = candidates.First(c => c.Tag == "ru-RU").Result;
            var english = candidates.First(c => c.Tag == "en-US").Result;
            lines = russian.Lines.Count == 0 ? english.Lines : russian.Lines.Where(l => l.Words.Count > 0).Select(line =>
            {
                int letters = line.Text.Count(char.IsLetter);
                int cyrillic = line.Text.Count(ch => ch is >= '\u0400' and <= '\u04ff');
                if (letters > 0 && cyrillic > letters * 0.6) return line;
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double height = line.Words.Max(w => w.BoundingRect.Height);
                return english.Lines.Where(l => l.Words.Count > 0)
                    .OrderBy(l => Math.Abs(l.Words.Min(w => w.BoundingRect.Y) - top))
                    .FirstOrDefault(l => Math.Abs(l.Words.Min(w => w.BoundingRect.Y) - top) < height * 0.6) ?? line;
            }).ToArray();
        }
        return new OcrResultModel { Text = FormatLines(lines), UsedLanguage = string.Join(" + ", candidates.Select(c => c.Tag)), Warning = missing.Count == 0 ? "" : "Недоступен OCR: " + string.Join(", ", missing) + "." };
    }
    private static string FormatLines(IEnumerable<OcrLine> source)
    {
        var lines = source.Where(l => l.Words.Count > 0).ToArray();
        var words = lines.SelectMany(l => l.Words).ToArray();
        if (words.Length == 0) return "";
        double left = words.Min(w => w.BoundingRect.X);
        double unit = Math.Max(1, words.Select(w => w.BoundingRect.Width / Math.Max(1, w.Text.Length)).Order().ElementAt(words.Length / 2));
        var output = new StringBuilder(); double previousBottom = 0, previousHeight = 0;
        foreach (var line in lines)
        {
            if (line.Words.Count == 0) continue;
            double top = line.Words.Min(w => w.BoundingRect.Y), bottom = line.Words.Max(w => w.BoundingRect.Bottom);
            if (output.Length > 0 && top - previousBottom > previousHeight * 0.7) output.AppendLine();
            double cursor = left;
            foreach (var word in line.Words)
            {
                int spaces = Math.Clamp((int)Math.Round((word.BoundingRect.X - cursor) / unit), cursor == left ? 0 : 1, 100);
                output.Append(' ', spaces).Append(word.Text); cursor = word.BoundingRect.Right;
            }
            output.AppendLine(); previousBottom = bottom; previousHeight = bottom - top;
        }
        return output.ToString().TrimEnd();
    }
}
