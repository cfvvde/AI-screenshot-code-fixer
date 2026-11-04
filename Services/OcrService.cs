using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SharpLensAI.Services
{
    public class OcrResultModel
    {
        public string Text { get; set; } = string.Empty;
        public string UsedLanguage { get; set; } = "Unknown";
    }

    public class OcrService
    {
        public async Task<OcrResultModel> RecognizeTextAsync(System.Drawing.Bitmap bitmap, string targetLang)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                throw new ArgumentException("Захваченное изображение имеет нулевой размер или отсутствует.");
            }

            // 1. Безопасное сохранение изображения в WinRT InMemoryRandomAccessStream
            using var stream = new InMemoryRandomAccessStream();
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                byte[] bytes = memoryStream.ToArray();
                using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            // 2. Инициализируем OCR-движок
            OcrEngine? engine = null;
            string usedLanguageTag = "Auto / System";

            if (targetLang.Equals("ru", StringComparison.OrdinalIgnoreCase))
            {
                var ruLang = new Language("ru-RU");
                if (OcrEngine.IsLanguageSupported(ruLang))
                {
                    engine = OcrEngine.TryCreateFromLanguage(ruLang);
                    usedLanguageTag = "ru-RU";
                }
            }
            else if (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                var enLang = new Language("en-US");
                if (OcrEngine.IsLanguageSupported(enLang))
                {
                    engine = OcrEngine.TryCreateFromLanguage(enLang);
                    usedLanguageTag = "en-US";
                }
            }

            // Если язык не выбран или не найден, используем системный
            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (engine == null)
            {
                throw new InvalidOperationException("Не удалось инициализировать OCR. Убедитесь, что в Windows установлен хотя бы один языковой пакет распознавания текста.");
            }

            // 3. Выполняем распознавание
            var ocrResult = await engine.RecognizeAsync(softwareBitmap);

            return new OcrResultModel
            {
                Text = ocrResult?.Text ?? string.Empty,
                UsedLanguage = usedLanguageTag
            };
        }
    }
}