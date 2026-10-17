using System.Reflection;

namespace ExamPC.Core.Security;

/// <summary>Memuat <c>hardening.js</c> yang ditanam sebagai embedded resource.</summary>
public static class HardeningScript
{
    private static string? _cached;

    public static string Source
    {
        get
        {
            if (_cached is not null) return _cached;

            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith("hardening.js", StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    "hardening.js tidak ikut terpaket. Periksa <EmbeddedResource> di ExamPC.Core.csproj.");

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return _cached = reader.ReadToEnd();
        }
    }
}
