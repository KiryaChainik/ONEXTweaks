function Get-ONEXTweaksGameBarWidgetHost([string]$UserSid) {
    $scope = @{}
    if ($UserSid) { $scope.User = $UserSid }
    foreach ($package in (Get-AppxPackage -Name 'Microsoft.XboxGamingOverlay' -PackageTypeFilter Main @scope)) {
        if ($package.Status -ne 'Ok') { continue }
        try {
            $manifest = Get-AppxPackageManifest -Package $package.PackageFullName @scope -ErrorAction Stop
        } catch { continue }
        if (!$manifest) { continue }
        $widgetHost = $manifest.SelectSingleNode("//*[local-name()='AppExtensionHost']/*[local-name()='Name' and text()='microsoft.gameBarUIExtension']")
        $activation = $manifest.SelectSingleNode("//*[local-name()='Protocol' and @Name='ms-gamebar']")
        if ($widgetHost -and $activation) { $package }
    }
}
