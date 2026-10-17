using System.Text.Json;

namespace ExamPC.Core.Config;

/// <summary>
/// Baca/tulis konfigurasi ujian ke folder data pengguna. Berkasnya memang bisa
/// dibaca siswa, tapi tidak berguna: PIN hanya tersimpan sebagai hash PBKDF2.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "ExamPC");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(DataDirectory, "exam-config.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static ExamConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new ExamConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ExamConfig>(json) ?? new ExamConfig();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Konfigurasi rusak jangan sampai membuat aplikasi gagal dibuka;
            // pengawas cukup mengisi ulang layar penyiapan.
            return new ExamConfig();
        }
    }

    public static void Save(ExamConfig config)
        => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
}
