// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;

namespace ONEXTweaks.WidgetBridge;

internal static class WidgetPackageIdentity
{
    internal static readonly string FamilyName = typeof(WidgetPackageIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "OnexWidgetPackageFamily").Value
        ?? throw new InvalidDataException("The Bridge build is missing its widget package family.");

    internal static string GameBarWidget => FamilyName + "_App_GamingWidget";
    internal static string LocalState => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", FamilyName, "LocalState");

    internal static bool MatchesApplication(string actual, string applicationId) =>
        string.Equals(actual, FamilyName + "!" + applicationId, StringComparison.Ordinal);
}
