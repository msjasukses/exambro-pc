using ExamPC.Core.Security;
using ExamPC.Platform.Linux;
using ExamPC.Platform.MacOS;
using ExamPC.Platform.Windows;

namespace ExamPC.Platform;

public static class PlatformSecurityFactory
{
    public static IPlatformSecurity Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsSecurity();
        if (OperatingSystem.IsMacOS()) return new MacOsSecurity();
        if (OperatingSystem.IsLinux()) return new LinuxSecurity();

        throw new PlatformNotSupportedException(
            $"ExamPC belum mendukung {Environment.OSVersion.Platform}.");
    }
}
