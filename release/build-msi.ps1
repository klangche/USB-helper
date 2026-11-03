# Build-Msi (#124, #147): builds installer/Package.wxs from the layout folder with WiX (pinned in
# .config/dotnet-tools.json) and its UI and Util extensions (pinned here, same version).
function Build-Msi([string] $version, [string] $productVersion, [string] $output) {
    dotnet tool restore | Out-Null
    dotnet wix extension add WixToolset.UI.wixext/6.0.2 WixToolset.Util.wixext/6.0.2
    if ($LASTEXITCODE) { throw "wix extension add failed." }
    # The license page shows LICENSE; WiX wants it as RTF.
    $text = (Get-Content LICENSE -Raw).Replace('\', '\\').Replace('{', '\{').Replace('}', '\}') -replace "`r?`n", "\par`r`n"
    Set-Content -NoNewline -Encoding ascii License.rtf "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18 $text}"
    # An absolute -bindpath: WiX resolves a relative one against the .wxs folder. -wx: a warning, such as no files
    # harvested, fails the build instead of making an empty .msi.
    dotnet wix build installer/Package.wxs -arch x64 -wx -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -bindpath "layout=$PWD\layout" -d Version=$version -d ProductVersion=$productVersion -d "LicenseRtf=$PWD\License.rtf" -o $output
    if ($LASTEXITCODE) { throw "wix build failed." }
}
