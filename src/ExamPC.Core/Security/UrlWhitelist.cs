namespace ExamPC.Core.Security;

/// <summary>
/// Penentu boleh-tidaknya sebuah URL dimuat. Dipakai di dua tempat:
/// navigasi utama (klik tautan) dan intersepsi setiap resource request.
/// </summary>
public sealed class UrlWhitelist
{
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _suffixes = new(); // dari pola "*.contoh.sch.id"

    /// <param name="examUrl">URL ujian; host-nya otomatis diizinkan.</param>
    /// <param name="extraHosts">Pola host tambahan dari pengawas.</param>
    public UrlWhitelist(string examUrl, IEnumerable<string>? extraHosts = null)
    {
        if (Uri.TryCreate(examUrl, UriKind.Absolute, out var examUri))
            Allow(examUri.Host);

        foreach (var raw in extraHosts ?? Enumerable.Empty<string>())
            Allow(raw);
    }

    private void Allow(string pattern)
    {
        var host = pattern.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return;

        // Terima juga bila pengawas menempelkan URL utuh, bukan sekadar host.
        if (host.Contains("://") && Uri.TryCreate(host, UriKind.Absolute, out var asUri))
            host = asUri.Host;

        if (host.StartsWith("*."))
        {
            var suffix = host[1..]; // "*.a.com" -> ".a.com"
            _suffixes.Add(suffix);
            _exact.Add(suffix[1..]); // domain apex ikut diizinkan
        }
        else
        {
            _exact.Add(host);
        }
    }

    /// <summary>
    /// Skema selain http/https selalu ditolak — <c>file:</c> membuka disk lokal,
    /// dan skema kustom bisa melompat ke aplikasi lain di luar kendali kita.
    /// </summary>
    public bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme is "data" or "blob") return true; // di-generate halaman itu sendiri
        if (uri.Scheme is not ("http" or "https")) return false;

        return IsHostAllowed(uri.Host);
    }

    public bool IsHostAllowed(string host)
    {
        host = host.ToLowerInvariant();
        if (_exact.Contains(host)) return true;

        foreach (var suffix in _suffixes)
            if (host.EndsWith(suffix, StringComparison.Ordinal))
                return true;

        return false;
    }

    public IReadOnlyCollection<string> AllowedHosts => _exact;
}
