namespace ExamPC.Core.Security;

/// <summary>Tingkat keparahan temuan pemindaian pra-ujian.</summary>
public enum Severity
{
    /// <summary>Sekadar informasi, ujian boleh lanjut.</summary>
    Info,

    /// <summary>Patut diwaspadai pengawas, ujian tetap boleh lanjut.</summary>
    Warn,

    /// <summary>Ujian tidak boleh dimulai sampai temuan ini dibereskan.</summary>
    Block,
}

/// <param name="Code">Kode stabil untuk pencatatan, mis. <c>REMOTE_DESKTOP</c>.</param>
/// <param name="Title">Judul singkat berbahasa Indonesia untuk pengawas.</param>
/// <param name="Detail">Penjelasan konkret, mis. nama proses yang ditemukan.</param>
/// <param name="Remedy">Langkah yang harus dilakukan agar temuan hilang.</param>
public sealed record SecurityFinding(
    string Code,
    Severity Severity,
    string Title,
    string Detail,
    string Remedy)
{
    public override string ToString() => $"[{Severity}] {Code}: {Title} — {Detail}";
}
