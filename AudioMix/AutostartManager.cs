using Microsoft.Win32;

namespace AudioMix;

/// <summary>
/// Trägt die Anwendung im Autostart des aktuellen Benutzers ein
/// (HKCU\...\Run – braucht keine Administratorrechte).
/// Beim Windows-Start wird sie mit "--background" gestartet, also ohne Fenster.
/// </summary>
internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioMix";

    /// <returns>true, wenn der Eintrag gesetzt bzw. entfernt werden konnte.</returns>
    public static bool TrySet(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null) return false;
                key?.SetValue(ValueName, $"\"{exePath}\" --background");
            }
            else
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}