namespace ExamPC.Core.Security;

/// <summary>
/// Daftar proses yang tidak boleh hidup selama ujian, dikelompokkan agar pesan
/// ke pengawas menyebut alasannya, bukan sekadar nama file.
/// </summary>
public static class ProcessBlacklist
{
    public sealed record Entry(string ProcessName, string Category, string DisplayName);

    private static readonly Entry[] Entries =
    {
        // --- Remote desktop: paling berbahaya, orang lain bisa mengerjakan soal ---
        new("anydesk",        "Remote desktop", "AnyDesk"),
        new("teamviewer",     "Remote desktop", "TeamViewer"),
        new("tv_w32",         "Remote desktop", "TeamViewer (helper)"),
        new("tv_x64",         "Remote desktop", "TeamViewer (helper)"),
        new("rustdesk",       "Remote desktop", "RustDesk"),
        new("remoting_host",  "Remote desktop", "Chrome Remote Desktop"),
        new("winvnc",         "Remote desktop", "VNC Server"),
        new("tvnserver",      "Remote desktop", "TightVNC"),
        new("vncserver",      "Remote desktop", "VNC Server"),
        new("ammyy",          "Remote desktop", "Ammyy Admin"),
        new("aa_v3",          "Remote desktop", "Ammyy Admin"),
        new("supremo",        "Remote desktop", "Supremo"),
        new("dwservice",      "Remote desktop", "DWService"),
        new("parsecd",        "Remote desktop", "Parsec"),
        new("mstsc",          "Remote desktop", "Remote Desktop Connection"),

        // --- Perekam & pembagi layar ---
        new("obs64",          "Perekam layar",  "OBS Studio"),
        new("obs32",          "Perekam layar",  "OBS Studio"),
        new("obs",            "Perekam layar",  "OBS Studio"),
        new("bdcam",          "Perekam layar",  "Bandicam"),
        new("bandicam",       "Perekam layar",  "Bandicam"),
        new("camtasia",       "Perekam layar",  "Camtasia"),
        new("snagit32",       "Perekam layar",  "Snagit"),
        new("sharex",         "Perekam layar",  "ShareX"),
        new("greenshot",      "Perekam layar",  "Greenshot"),
        new("lightshot",      "Perekam layar",  "Lightshot"),
        new("flashback",      "Perekam layar",  "FlashBack Recorder"),
        new("screenrec",      "Perekam layar",  "ScreenRec"),

        // --- Konferensi: kamera + share screen ke luar ruangan ---
        new("zoom",           "Konferensi",     "Zoom"),
        new("teams",          "Konferensi",     "Microsoft Teams"),
        new("ms-teams",       "Konferensi",     "Microsoft Teams"),
        new("skype",          "Konferensi",     "Skype"),
        new("webexmta",       "Konferensi",     "Cisco Webex"),
        new("googlemeet",     "Konferensi",     "Google Meet"),

        // --- Perpesanan: jalur paling gampang bertukar jawaban ---
        new("discord",        "Perpesanan",     "Discord"),
        new("telegram",       "Perpesanan",     "Telegram"),
        new("whatsapp",       "Perpesanan",     "WhatsApp"),
        new("slack",          "Perpesanan",     "Slack"),
        new("line",           "Perpesanan",     "LINE"),

        // --- Mesin virtual: ujian bisa dijalankan di jendela lain ---
        new("vmware",         "Mesin virtual",  "VMware"),
        new("vmware-vmx",     "Mesin virtual",  "VMware"),
        new("virtualbox",     "Mesin virtual",  "VirtualBox"),
        new("virtualboxvm",   "Mesin virtual",  "VirtualBox"),
        new("vmwaretray",     "Mesin virtual",  "VMware Tray"),

        // --- Otomasi & makro: bisa menekan tombol menembus proteksi kita ---
        new("autohotkey",     "Otomasi",        "AutoHotkey"),
        new("autoit3",        "Otomasi",        "AutoIt"),
    };

    /// <summary>Cocokkan daftar proses berjalan dengan blacklist.</summary>
    public static IReadOnlyList<Entry> Match(IEnumerable<string> runningProcesses)
    {
        var running = new HashSet<string>(
            runningProcesses.Select(p => p.Trim().ToLowerInvariant()),
            StringComparer.Ordinal);

        return Entries.Where(e => running.Contains(e.ProcessName)).ToList();
    }
}
