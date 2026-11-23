using System.Text.Json;

namespace FinanceTracker.Data;

/// <summary>
/// Stores the desktop app's connection settings for the current Windows user.
/// The file contains a URL only; it never stores a password or financial data.
/// </summary>
public sealed class DesktopSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5080/";

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FinanceTracker",
        "settings.json");

    public static DesktopSettings Load()
    {
        if (!File.Exists(FilePath)) return new DesktopSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath), JsonOptions)
                ?? new DesktopSettings();
            if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            {
                return new DesktopSettings();
            }

            settings.ApiBaseUrl = address.ToString().EndsWith('/') ? address.ToString() : address + "/";
            return settings;
        }
        catch (JsonException)
        {
            // Keep a damaged settings file for inspection and use the documented local default.
            return new DesktopSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
