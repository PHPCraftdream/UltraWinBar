<#
.SYNOPSIS
  Fails when a package in the restored dependency graph has a license outside policy.json.

.DESCRIPTION
  Reads every project.assets.json under -Root (run `dotnet restore` first), takes each package once, and
  reads its license from the package's own .nuspec: an SPDX expression, or a license file whose SHA-256 is
  pinned in policy.json. Anything else (no license metadata, an unpinned file, an expression with a license
  outside allowedLicenses) fails. With -SelfTest it checks its own logic and exits.
#>
[CmdletBinding()]
param(
    [string]$Root = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))),
    [string]$PolicyPath = (Join-Path $PSScriptRoot 'policy.json'),
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# SPDX precedence: WITH, then AND, then OR. A malformed expression is not allowed.
class SpdxParser {
    [string[]]$Tokens
    [int]$Pos
    [System.Collections.Generic.HashSet[string]]$Allowed
    [System.Collections.Generic.HashSet[string]]$Exceptions

    SpdxParser([string]$expression, [string[]]$allowed, [string[]]$exceptions) {
        $this.Tokens = @([regex]::Matches($expression, '\(|\)|[^\s()]+') | ForEach-Object { $_.Value })
        $this.Allowed = [System.Collections.Generic.HashSet[string]]::new([string[]]@($allowed), [StringComparer]::OrdinalIgnoreCase)
        $this.Exceptions = [System.Collections.Generic.HashSet[string]]::new([string[]]@($exceptions), [StringComparer]::OrdinalIgnoreCase)
    }

    # '' past the last token.
    [string] Peek() { if ($this.Pos -lt $this.Tokens.Count) { return $this.Tokens[$this.Pos] } else { return '' } }

    [bool] Parse() {
        if ($this.Tokens.Count -eq 0) { return $false }
        $result = $this.ParseOr()
        if ($this.Pos -ne $this.Tokens.Count) { throw "unexpected '$($this.Peek())'" }
        return $result
    }

    [bool] ParseOr() {
        $result = $this.ParseAnd()
        while ($this.Peek() -ieq 'OR') { $this.Pos++; $right = $this.ParseAnd(); $result = $result -or $right }
        return $result
    }

    [bool] ParseAnd() {
        $result = $this.ParseTerm()
        while ($this.Peek() -ieq 'AND') { $this.Pos++; $right = $this.ParseTerm(); $result = $result -and $right }
        return $result
    }

    [bool] ParseTerm() {
        $token = $this.Peek()
        if ($token -eq '' -or $token -eq ')' -or $token -ieq 'AND' -or $token -ieq 'OR' -or $token -ieq 'WITH') { throw "unexpected '$token'" }
        $this.Pos++
        if ($token -eq '(') {
            $inner = $this.ParseOr()
            if ($this.Peek() -ne ')') { throw "missing ')'" }
            $this.Pos++
            return $inner
        }
        $isAllowed = $this.Allowed.Contains($token)
        if ($this.Peek() -ieq 'WITH') {
            $this.Pos++
            $exception = $this.Peek()
            if ($exception -eq '' -or $exception -eq '(' -or $exception -eq ')') { throw "missing exception after WITH" }
            $this.Pos++
            return $isAllowed -and $this.Exceptions.Contains($exception)
        }
        return $isAllowed
    }
}

function Test-LicenseExpression {
    param([string]$Expression, [string[]]$Allowed, [string[]]$AllowedExceptions = @())
    try { return [SpdxParser]::new($Expression, $Allowed, $AllowedExceptions).Parse() } catch { return $false }
}

function Read-Nuspec([string]$Path) {
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try { $document = New-Object System.Xml.XmlDocument; $document.Load($reader); return $document } finally { $reader.Dispose() }
}

# Expression is the license in SPDX form, or $null with Detail saying why it could not be read.
function Get-PackageLicense {
    param([string]$Id, [string]$PackageDir, [object[]]$Overrides = @())
    $none = { param($detail) [pscustomobject]@{ Expression = $null; Source = 'none'; Detail = $detail } }
    $nuspec = Get-ChildItem -LiteralPath $PackageDir -Filter '*.nuspec' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspec) { return & $none 'no .nuspec in the package folder' }
    $node = (Read-Nuspec $nuspec.FullName).SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    if (-not $node) { return & $none 'no license element (licenseUrl only)' }
    $type = $node.GetAttribute('type')
    $text = $node.InnerText.Trim()
    if ($type -eq 'expression') { return [pscustomobject]@{ Expression = $text; Source = 'expression'; Detail = '' } }
    if ($type -ne 'file') { return & $none "unknown license element type '$type'" }

    $root = [IO.Path]::GetFullPath($PackageDir).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $file = [IO.Path]::GetFullPath((Join-Path $PackageDir $text))
    if (-not $file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
        return & $none "license file '$text' is missing from the package"
    }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $pinned = @($Overrides | Where-Object { $_.id -ieq $Id -and $_.licenseFileSha256 -ieq $hash }) | Select-Object -First 1
    if ($pinned) { return [pscustomobject]@{ Expression = $pinned.license; Source = 'file, pinned'; Detail = '' } }
    return & $none "license file '$text' (sha256 $hash) is not pinned in policy.json"
}

# Each package once, with the folder holding its files.
function Get-RestoredPackages([string]$Root) {
    $found = @{}
    $assetFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -Filter project.assets.json -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](\.git|node_modules|worktrees)[\\/]' })
    foreach ($assetFile in $assetFiles) {
        $assets = Get-Content -LiteralPath $assetFile.FullName -Raw | ConvertFrom-Json
        $folders = @($assets.packageFolders.PSObject.Properties.Name)
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -ne 'package' -or $found.ContainsKey($library.Name)) { continue }
            $dir = $folders | ForEach-Object { Join-Path $_ $library.Value.path } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            $id, $version = $library.Name -split '/', 2
            $found[$library.Name] = [pscustomobject]@{ Id = $id; Version = $version; Dir = $dir }
        }
    }
    [pscustomobject]@{ AssetFiles = $assetFiles.Count; Packages = @($found.Values | Sort-Object Id, Version) }
}

function Invoke-LicenseCheck {
    param([string]$Root, [string]$PolicyPath)
    $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
    $restored = Get-RestoredPackages $Root
    if ($restored.AssetFiles -eq 0 -or $restored.Packages.Count -eq 0) {
        throw "No restored packages under '$Root'; run dotnet restore first. An empty graph must not pass."
    }
    foreach ($package in $restored.Packages) {
        if (-not $package.Dir) {
            $license = [pscustomobject]@{ Expression = $null; Source = 'none'; Detail = 'package folder not found in the NuGet cache' }
        } else {
            $license = Get-PackageLicense -Id $package.Id -PackageDir $package.Dir -Overrides @($policy.licenseFileOverrides)
        }
        $allowed = $license.Expression -and (Test-LicenseExpression $license.Expression @($policy.allowedLicenses) @($policy.allowedExceptions))
        $shown = if ($license.Expression) { $license.Expression } else { '(unknown)' }
        [pscustomobject]@{
            Package = "$($package.Id) $($package.Version)"; License = $shown; Source = $license.Source
            Allowed = [bool]$allowed; Detail = $license.Detail
        }
    }
}

function Invoke-SelfTest {
    $script:tests = 0
    function Expect($actual, $expected, $what) {
        $script:tests++
        if ($actual -ne $expected) { throw "self-test failed: $what (expected '$expected', got '$actual')" }
    }

    $allowed = 'MIT', 'BSD-3-Clause', 'Apache-2.0'
    $expressions = @(
        ('MIT', $true), ('mit', $true), ('GPL-3.0-only', $false), ('LGPL-2.1-or-later', $false), ('Unknown-1.0', $false),
        ('MIT OR GPL-3.0-only', $true), ('GPL-3.0-only OR MIT', $true), ('GPL-2.0-only OR GPL-3.0-only', $false),
        ('MIT AND BSD-3-Clause', $true), ('MIT AND GPL-2.0-only', $false),
        ('GPL-2.0-only AND MIT OR Apache-2.0', $true), ('GPL-2.0-only AND (MIT OR Apache-2.0)', $false),
        ('(MIT OR GPL-2.0-only) AND Apache-2.0', $true), ('((MIT))', $true),
        ('MIT WITH Classpath-exception-2.0', $false), ('', $false), ('MIT OR', $false), ('(MIT', $false), ('MIT)', $false),
        ('MIT BSD-3-Clause', $false), ('AND', $false), ('MIT WITH', $false)
    )
    foreach ($case in $expressions) {
        Expect (Test-LicenseExpression $case[0] $allowed) $case[1] "expression '$($case[0])'"
    }
    Expect (Test-LicenseExpression 'MIT WITH Classpath-exception-2.0' $allowed @('Classpath-exception-2.0')) $true 'a listed exception'
    Expect (Test-LicenseExpression 'GPL-2.0-only WITH Classpath-exception-2.0' $allowed @('Classpath-exception-2.0')) $false 'an exception does not allow its base license'

    $scratch = Join-Path ([IO.Path]::GetTempPath()) ("license-check-" + [guid]::NewGuid().ToString('N'))
    try {
        $nuget = Join-Path $scratch 'packages'
        function New-FakePackage($id, $version, $licenseXml, $files = @{}) {
            $dir = Join-Path $nuget "$($id.ToLower())/$version"
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            $xml = "<?xml version=`"1.0`"?><package xmlns=`"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd`"><metadata><id>$id</id><version>$version</version>$licenseXml</metadata></package>"
            Set-Content -LiteralPath (Join-Path $dir "$($id.ToLower()).nuspec") -Value $xml -Encoding UTF8
            foreach ($name in $files.Keys) { Set-Content -LiteralPath (Join-Path $dir $name) -Value $files[$name] -Encoding UTF8 -NoNewline }
        }
        New-FakePackage 'Good.Expr' '1.0.0' '<license type="expression">MIT</license>'
        New-FakePackage 'Bad.Expr' '1.0.0' '<license type="expression">GPL-3.0-only</license>'
        New-FakePackage 'Url.Only' '1.0.0' '<licenseUrl>https://example.invalid/license</licenseUrl>'
        New-FakePackage 'File.Pinned' '1.0.0' '<license type="file">LICENSE</license>' @{ LICENSE = 'pinned text' }
        New-FakePackage 'File.Changed' '2.0.0' '<license type="file">LICENSE</license>' @{ LICENSE = 'a different text' }
        New-FakePackage 'File.Escape' '1.0.0' '<license type="file">../../outside.txt</license>'
        Set-Content -LiteralPath (Join-Path $nuget 'outside.txt') -Value 'pinned text' -Encoding UTF8 -NoNewline
        $pinnedHash = (Get-FileHash -LiteralPath (Join-Path $nuget 'file.pinned/1.0.0/LICENSE') -Algorithm SHA256).Hash
        $overrides = @([pscustomobject]@{ id = 'File.Pinned'; license = 'BSD-3-Clause'; licenseFileSha256 = $pinnedHash.ToLower() },
                       [pscustomobject]@{ id = 'File.Escape'; license = 'MIT'; licenseFileSha256 = $pinnedHash })

        $dirOf = { param($id, $version) Join-Path $nuget "$($id.ToLower())/$version" }
        $good = Get-PackageLicense 'Good.Expr' (& $dirOf 'Good.Expr' '1.0.0') $overrides
        Expect $good.Expression 'MIT' 'an SPDX expression is read from the nuspec'
        $url = Get-PackageLicense 'Url.Only' (& $dirOf 'Url.Only' '1.0.0') $overrides
        Expect $url.Expression $null 'a licenseUrl-only package has no readable license'
        $pinned = Get-PackageLicense 'File.Pinned' (& $dirOf 'File.Pinned' '1.0.0') $overrides
        Expect $pinned.Expression 'BSD-3-Clause' 'a license file with a pinned hash takes the pinned license'
        $changed = Get-PackageLicense 'File.Changed' (& $dirOf 'File.Changed' '2.0.0') $overrides
        Expect $changed.Expression $null 'a license file whose text changed is not covered by the pin'
        $other = Get-PackageLicense 'Other.Id' (& $dirOf 'File.Pinned' '1.0.0') $overrides
        Expect $other.Expression $null 'a pin belongs to its package id'
        $escape = Get-PackageLicense 'File.Escape' (& $dirOf 'File.Escape' '1.0.0') $overrides
        Expect $escape.Expression $null 'a license file outside the package folder is ignored'
        $missing = Get-PackageLicense 'Gone' (Join-Path $nuget 'nowhere') $overrides
        Expect $missing.Expression $null 'a package folder without a nuspec has no license'

        $project = Join-Path $scratch 'repo/obj'
        New-Item -ItemType Directory -Path $project -Force | Out-Null
        $libraries = [ordered]@{}
        foreach ($name in 'Good.Expr/1.0.0', 'Bad.Expr/1.0.0', 'File.Pinned/1.0.0', 'File.Changed/2.0.0') {
            $libraries[$name] = @{ type = 'package'; path = $name.ToLower() }
        }
        $libraries['Some.Project/1.0.0'] = @{ type = 'project'; path = 'x' }
        $assets = @{ packageFolders = @{ $nuget = @{} }; libraries = $libraries } | ConvertTo-Json -Depth 5
        Set-Content -LiteralPath (Join-Path $project 'project.assets.json') -Value $assets -Encoding UTF8
        $second = Join-Path $scratch 'repo/other/obj'
        New-Item -ItemType Directory -Path $second -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $second 'project.assets.json') -Value $assets -Encoding UTF8

        $policyPath = Join-Path $scratch 'policy.json'
        @{ allowedLicenses = @('MIT', 'BSD-3-Clause'); allowedExceptions = @(); licenseFileOverrides = $overrides } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $policyPath -Encoding UTF8

        $restored = Get-RestoredPackages (Join-Path $scratch 'repo')
        Expect $restored.AssetFiles 2 'both assets files are read'
        Expect $restored.Packages.Count 4 'a package seen in two projects counts once and project references are skipped'

        $rows = @(Invoke-LicenseCheck (Join-Path $scratch 'repo') $policyPath)
        $verdict = @{}
        foreach ($row in $rows) { $verdict[$row.Package] = $row.Allowed }
        Expect $verdict['Good.Expr 1.0.0'] $true 'an allowed expression passes'
        Expect $verdict['Bad.Expr 1.0.0'] $false 'a license outside the policy fails'
        Expect $verdict['File.Pinned 1.0.0'] $true 'a pinned license file passes'
        Expect $verdict['File.Changed 2.0.0'] $false 'a changed license file fails until the new text is pinned'

        $empty = Join-Path $scratch 'empty'
        New-Item -ItemType Directory -Path $empty -Force | Out-Null
        $threw = $false
        try { Invoke-LicenseCheck $empty $policyPath | Out-Null } catch { $threw = $true }
        Expect $threw $true 'a root without restored packages must not pass'
    } finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Self-test passed ($($script:tests) checks)."
}

if ($SelfTest) { Invoke-SelfTest; return }

$rows = @(Invoke-LicenseCheck $Root $PolicyPath)
$rows | Format-Table Package, License, Source, Allowed -AutoSize | Out-String -Width 200 | Write-Host
$denied = @($rows | Where-Object { -not $_.Allowed })

if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @('## Dependency licenses', '', '| Package | License | Source | Verdict |', '| --- | --- | --- | --- |')
    foreach ($row in $rows) {
        $verdict = if ($row.Allowed) { 'allowed' } else { "**denied** $($row.Detail)".Trim() }
        $lines += "| $($row.Package) | $($row.License) | $($row.Source) | $verdict |"
    }
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $lines
}

foreach ($row in $denied) {
    $reason = if ($row.Detail) { $row.Detail } else { "license $($row.License) is not in allowedLicenses" }
    Write-Host "::error title=License not allowed::$($row.Package): $reason"
}
if ($denied.Count -gt 0) { Write-Host "$($denied.Count) of $($rows.Count) packages are outside the license policy."; exit 1 }
Write-Host "All $($rows.Count) packages are within the license policy."
