using System.Text.Json;

namespace XenusDt1Decompiler
{
    // GUI settings remembered between runs, stored in %AppData%\xenus-dt1-decompiler\settings.json
    // (the exe folder may not be writable).
    internal sealed class AppSettings
    {
        public string? InputPath { get; set; }
        public string? OutputPath { get; set; }
        public string? Format { get; set; }
        public string? Palette { get; set; }

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "xenus-dt1-decompiler", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Corrupt or unreadable settings: start with defaults.
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not being able to remember settings is not worth an error dialog.
            }
        }
    }
}
