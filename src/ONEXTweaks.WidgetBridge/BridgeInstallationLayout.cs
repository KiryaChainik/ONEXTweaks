// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal static class BridgeInstallationLayout
{
    internal const string BridgeFileName = "ONEXTweaks.WidgetBridge.exe";
    internal static string InstalledRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ONEXTweaks");
    internal static string InstalledImage => Path.Combine(InstalledRoot, "WidgetBridge", BridgeFileName);
    internal static string InstalledLauncher => Path.Combine(InstalledRoot, "CompanionLauncher.exe");
    internal static string DevelopmentLauncher => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONEXTweaks", "CompanionLauncher.exe");
    internal static string DevelopmentProject => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONEXTweaks", "dev", "project");
    internal static string DevelopmentImage => Path.Combine(DevelopmentProject, "src", "ONEXTweaks.WidgetBridge",
        "bin", "Release", "net10.0-windows", BridgeFileName);
    // Retain the existing data root and lease across the change of executable location.
    internal static string Reports => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONEXTweaks", "dev", "Reports");

    internal static bool IsInstalled => SameImage(Environment.ProcessPath, InstalledImage);
    internal static bool IsCaptureImage(string image) =>
        SameImage(image, InstalledImage) || SameImage(image, DevelopmentImage);

    internal static bool SameImage(string? image, string expected) =>
        image is not null && string.Equals(image, expected, StringComparison.OrdinalIgnoreCase);

    internal static bool IsRecordedLeaseImage(string image, string installedNtImage, string developmentNtRoot) =>
        image.Length > 0 && !image.Contains('\0') &&
        (SameImage(image, installedNtImage) ||
         (image.StartsWith(developmentNtRoot, StringComparison.OrdinalIgnoreCase) &&
          image.EndsWith(@"\" + BridgeFileName, StringComparison.OrdinalIgnoreCase)));
}
