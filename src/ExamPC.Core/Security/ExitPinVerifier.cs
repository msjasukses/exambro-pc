using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExamPC.Core.Security;

public enum ExitPinOutcome { Valid, Invalid, Unavailable }

public sealed record ExitPinResult(ExitPinOutcome Outcome, string Reason = "")
{
    public static readonly ExitPinResult Valid = new(ExitPinOutcome.Valid);
    public static readonly ExitPinResult Invalid = new(ExitPinOutcome.Invalid);
    public static ExitPinResult Unavailable(string reason) => new(ExitPinOutcome.Unavailable, reason);
}

/// <summary>
/// Verifikasi PIN keluar ke server CBT memakai API key — protokol yang sama persis
/// dengan ExitPinVerifier.kt milik exbro, sehingga HP dan PC memakai satu PIN.
///
/// Protokol (pasangannya: exbro/server/exbropinapi/verify-pin.php):
///   Request  POST {"nonce": hex32, "device": id, "pin_hmac": HMAC(key, nonce|pin)}
///   Response 200  {"result": "valid"|"invalid", "sig": HMAC(key, nonce|result)}
///
/// PIN tidak pernah dikirim apa adanya, dan jawaban server ditandatangani memakai
/// nonce baru di setiap percobaan, sehingga "valid" tidak bisa dipalsukan atau
/// diputar ulang walau koneksinya HTTP biasa.
///
/// Hanya jawaban bertanda tangan sah yang final. Selain itu (server mati, timeout,
/// belum dikonfigurasi, dibatasi, tanda tangan salah) hasilnya Unavailable dan
/// pemanggil memakai PIN cadangan dari build.
/// </summary>
public static class ExitPinVerifier
{
    private const string ResourceName = "ExamPC.Core.exampc-api.properties";

    /// <summary>Sama dengan PIN cadangan bawaan exbro bila fallback.pin tidak diisi.</summary>
    private const string DefaultFallbackPin = "112233";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private static readonly Regex DeviceIdPattern = new("^[A-Za-z0-9-]{8,64}$");
    private static readonly Regex Ipv4Pattern = new(@"^\d{1,3}(\.\d{1,3}){3}$");

    private sealed record Settings(string ApiUrl, string ApiKey, string FallbackPin);

    private static readonly Lazy<Settings> Current = new(LoadSettings);

    public static bool IsConfigured =>
        Current.Value.ApiKey.Length > 0 && Current.Value.ApiUrl.Length > 0;

    /// <summary>api.url boleh URL absolut, atau path yang ditempelkan ke origin link ujian.</summary>
    public static string? ResolveEndpoint(string examUrl)
    {
        var configured = Current.Value.ApiUrl;
        if (configured.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            configured.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return configured;

        if (!Uri.TryCreate(examUrl, UriKind.Absolute, out var exam)) return null;
        return $"{exam.Scheme}://{exam.Authority}/{configured.TrimStart('/')}";
    }

    /// <summary>PIN cadangan untuk semua perangkat, dipakai hanya bila server PIN tidak terjangkau.</summary>
    public static bool MatchesFallbackPin(string pin) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(pin),
            Encoding.UTF8.GetBytes(Current.Value.FallbackPin));

    public static async Task<ExitPinResult> VerifyAsync(
        string examUrl, string pin, string deviceId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return ExitPinResult.Unavailable("api_not_configured");

        var endpoint = ResolveEndpoint(examUrl);
        if (endpoint is null || !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            return ExitPinResult.Unavailable("bad_endpoint");

        try
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["nonce"] = nonce,
                ["device"] = deviceId,
                ["pin_hmac"] = HmacHex($"{nonce}|{pin}"),
            });

            using var handler = new HttpClientHandler();

            // Server CBT lokal umumnya memakai sertifikat terbitan sendiri. Validasi TLS
            // dilonggarkan HANYA untuk host lokal, dan itu aman karena keaslian jawaban
            // dijamin tanda tangan HMAC, bukan oleh sertifikat.
            if (endpointUri.Scheme == Uri.UriSchemeHttps && IsPrivateHost(endpointUri.Host))
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri) { Content = content };
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                return ExitPinResult.Unavailable($"http_{(int)response.StatusCode}");

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(text);

            var result = ReadString(json.RootElement, "result");
            var sig = ReadString(json.RootElement, "sig");

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sig),
                    Encoding.UTF8.GetBytes(HmacHex($"{nonce}|{result}"))))
                return ExitPinResult.Unavailable("bad_signature");

            return result switch
            {
                "valid" => ExitPinResult.Valid,
                "invalid" => ExitPinResult.Invalid,
                _ => ExitPinResult.Unavailable("bad_result"),
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ExitPinResult.Unavailable(ex.GetType().Name);
        }
    }

    /// <summary>
    /// ID acak per instalasi (bukan ID perangkat keras). Dipakai server untuk membatasi
    /// percobaan PIN salah per PC, supaya satu siswa tidak mengunci PC lain di lab yang
    /// keluar lewat IP publik yang sama.
    /// </summary>
    public static string GetInstallId(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "install-id");

        try
        {
            var existing = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            if (DeviceIdPattern.IsMatch(existing)) return existing;
        }
        catch (Exception) { /* dibuat ulang di bawah */ }

        var id = Guid.NewGuid().ToString();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, id);
        }
        catch (Exception)
        {
            // Tidak bisa disimpan: ID tetap sah untuk sesi ini, hanya berganti tiap aplikasi dibuka.
        }

        return id;
    }

    /// <summary>Aturan yang sama dengan UrlWhitelist.isPrivateHost milik exbro.</summary>
    public static bool IsPrivateHost(string host)
    {
        var h = host.ToLowerInvariant();
        if (h.Length == 0) return false;
        if (h == "localhost" || h.EndsWith(".local", StringComparison.Ordinal) ||
            h.EndsWith(".lan", StringComparison.Ordinal)) return true;
        if (!Ipv4Pattern.IsMatch(h)) return false;

        var parts = h.Split('.').Select(int.Parse).ToArray();
        if (parts.Any(p => p > 255)) return false;

        return parts[0] switch
        {
            10 or 127 => true,
            172 => parts[1] is >= 16 and <= 31,
            192 => parts[1] == 168,
            169 => parts[1] == 254,
            _ => true, // IPv4 langsung tanpa nama domain: perlakukan sebagai server lokal
        };
    }

    private static string HmacHex(string message) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Current.Value.ApiKey),
            Encoding.UTF8.GetBytes(message))).ToLowerInvariant();

    private static string ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>Membaca exampc-api.properties yang ditanam saat build (format sama dengan exbro).</summary>
    private static Settings LoadSettings()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } raw)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or '!') continue;

                var split = line.IndexOf('=');
                if (split <= 0) continue;

                values[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        }

        var fallback = values.GetValueOrDefault("fallback.pin", "");
        if (!Regex.IsMatch(fallback, @"^\d{4,8}$")) fallback = DefaultFallbackPin;

        return new Settings(
            values.GetValueOrDefault("api.url", ""),
            values.GetValueOrDefault("api.key", ""),
            fallback);
    }
}
