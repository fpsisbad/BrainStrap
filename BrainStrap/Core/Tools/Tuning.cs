using Microsoft.Win32;

namespace BrainStrap.Core;

/// <summary>
/// Windows' own per-app settings for the Roblox player, written to the current user's registry exactly where
/// Windows Settings writes them. Nothing here changes Roblox itself.
///   • Graphics preference: Settings → System → Display → Graphics → "High performance" (the fast GPU on laptops)
///   • Fullscreen optimisations: the "Disable fullscreen optimisations" tick box on the exe's Compatibility tab
/// </summary>
internal static class Tuning
{
    const string GpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    const string GpuHigh = "GpuPreference=2;";
    const string NoFso = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public static void Apply(Settings s, string playerExe)
    {
        try
        {
            Set(GpuKey, playerExe, s.Tuning.HighPerformanceGpu ? GpuHigh : null, GpuHigh);
            Set(LayersKey, playerExe, s.Tuning.NoFullscreenOptimizations ? NoFso : null, NoFso);
        }
        catch (Exception ex) { Logger.Warn("tuning: " + ex.Message); }
    }

    /// <summary>Applies to every installed Roblox copy (used when a switch is flipped).</summary>
    public static void ApplyAll(Settings s)
    {
        foreach (var v in RobloxInstall.List()) Apply(s, v.PlayerExe);
    }

    /// <summary>Writes our value, or removes it again if it is still the one we wrote. Entries for Roblox copies that no longer exist are cleaned up.</summary>
    static void Set(string key, string exe, string? value, string ours)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        if (value != null) k.SetValue(exe, value, RegistryValueKind.String);
        else if (k.GetValue(exe) as string == ours) k.DeleteValue(exe, false);
        foreach (var name in k.GetValueNames())
            if (name.EndsWith(@"\RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase) && name.Contains(@"\Versions\version-") && !File.Exists(name) && k.GetValue(name) as string == ours)
                k.DeleteValue(name, false);
    }
}
