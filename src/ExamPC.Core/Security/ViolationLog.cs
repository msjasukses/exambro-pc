using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPC.Core.Security;

public sealed record Violation(
    DateTimeOffset At,
    string Code,
    string Detail,
    string Machine,
    string Room);

/// <summary>
/// Catatan pelanggaran selama sesi. Selalu ditulis ke berkas lokal lebih dulu —
/// jaringan lab sering putus, dan bukti tidak boleh ikut hilang. Pengiriman ke
/// webhook bersifat usaha-terbaik dan tidak pernah menghentikan ujian.
/// </summary>
public sealed class ViolationLog : IDisposable
{
    private readonly string _path;
    private readonly string _webhookUrl;
    private readonly string _room;
    private readonly List<Violation> _all = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public ViolationLog(string logDirectory, string webhookUrl, string room)
    {
        Directory.CreateDirectory(logDirectory);
        _path = Path.Combine(logDirectory, $"pelanggaran-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        _webhookUrl = webhookUrl;
        _room = room;
    }

    public IReadOnlyList<Violation> All => _all;
    public int Count => _all.Count;

    public async Task RecordAsync(string code, string detail)
    {
        var v = new Violation(DateTimeOffset.Now, code, detail, Environment.MachineName, _room);
        _all.Add(v);

        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(v) + Environment.NewLine);
        }
        catch (IOException)
        {
            // Disk penuh atau berkas terkunci: catatan di memori tetap utuh dan
            // masih ditampilkan ke pengawas saat sesi ditutup.
        }
        finally
        {
            _writeLock.Release();
        }

        if (!string.IsNullOrWhiteSpace(_webhookUrl))
            _ = PostAsync(v);
    }

    private async Task PostAsync(Violation v)
    {
        try
        {
            await _http.PostAsJsonAsync(_webhookUrl, v);
        }
        catch (Exception)
        {
            // Server ujian tidak terjangkau — berkas lokal tetap jadi sumber kebenaran.
        }
    }

    public string LogPath => _path;

    public void Dispose()
    {
        _http.Dispose();
        _writeLock.Dispose();
    }
}
