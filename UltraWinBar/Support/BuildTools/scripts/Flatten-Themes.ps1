$ErrorActionPreference = 'Stop'
$themeRoot = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../../..')) 'Assets\Themes'
$presentation = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
$xaml = 'http://schemas.microsoft.com/winfx/2006/xaml'
$templateKeys = @{
    Taskbar='FlatPanelTemplate'; Tray='FlatTrayTemplate';
    TaskButton='FlatTaskTemplate'; TaskButtonActive='FlatTaskTemplate'; TaskButtonFlashing='FlatTaskTemplate';
    StartButton='FlatToggleTemplate'; ShowDesktopButton='FlatToggleTemplate'; TrayToggleButton='FlatTrayToggleTemplate';
    ToolbarThumb='FlatGripTemplate';
    ToolbarButton='FlatButtonTemplate'; TaskListScrollButton='FlatRepeatTemplate';
    TaskListScrollUpButton='FlatRepeatTemplate'; TaskListScrollDownButton='FlatRepeatTemplate'
}
function Set-StyleValue($document, $style, $property, $value) {
    $setter = $style.SelectSingleNode('./*[local-name()="Setter" and @Property="'+$property+'"]')
    if (-not $setter) { $setter=$document.CreateElement('Setter',$presentation); $null=$style.AppendChild($setter) }
    $setter.RemoveAll(); $setter.SetAttribute('Property',$property); $setter.SetAttribute('Value',$value)
}
foreach ($file in Get-ChildItem -LiteralPath $themeRoot -Filter '*.xaml' -Recurse) {
    $document=New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace=$true
    $document.Load($file.FullName)
    foreach ($gradient in @($document.SelectNodes('//*[local-name()="LinearGradientBrush" or local-name()="RadialGradientBrush"]'))) {
        $stops=@($gradient.SelectNodes('.//*[local-name()="GradientStop"]'))
        if ($stops.Count -eq 0) { throw "Gradient without stops: $($file.Name)" }
        $stop=$stops | Sort-Object { [Math]::Abs([double]::Parse($_.GetAttribute('Offset'),[Globalization.CultureInfo]::InvariantCulture)-0.5) } | Select-Object -First 1
        $brush=$document.CreateElement('SolidColorBrush',$presentation)
        foreach ($attribute in $gradient.Attributes) {
            if ($attribute.LocalName -in @('Key','Opacity')) { $null=$brush.Attributes.Append($document.ImportNode($attribute,$true)) }
        }
        $brush.SetAttribute('Color',$stop.GetAttribute('Color'))
        $null=$gradient.ParentNode.ReplaceChild($brush,$gradient)
    }
    foreach ($brush in $document.SelectNodes('//*[local-name()="SolidColorBrush"]')) {
        if ($brush.GetAttribute('Key',$xaml) -match 'Highlight|Shadow|^ButtonLight$|InnerLight|InnerDark') { $brush.SetAttribute('Color','Transparent') }
    }
    foreach ($shadow in $document.SelectNodes('//*[local-name()="DropShadowEffect"]')) {
        $shadow.SetAttribute('Opacity','0'); $shadow.SetAttribute('BlurRadius','0'); $shadow.SetAttribute('ShadowDepth','0')
    }
    foreach ($style in $document.SelectNodes('//*[local-name()="Style"]')) {
        $key=$style.GetAttribute('Key',$xaml)
        if ($templateKeys.ContainsKey($key)) {
            Set-StyleValue $document $style 'Template' ('{DynamicResource '+$templateKeys[$key]+'}')
            if ($key -match '^TaskButton') {
                Set-StyleValue $document $style 'Margin' '0'; Set-StyleValue $document $style 'Padding' '6,2'
                $foreground=if($key -eq 'TaskButtonFlashing'){'{DynamicResource ButtonFlashingForeground}'}else{'{DynamicResource FlatTaskText}'}
                Set-StyleValue $document $style 'Foreground' $foreground
            }
        }
        if ($key -eq 'StartIcon') {
            $style.RemoveAttribute('BasedOn')
            foreach ($child in @($style.ChildNodes)) { $null=$style.RemoveChild($child) }
            Set-StyleValue $document $style 'Source' '{DynamicResource FlatStartIcon}'
            Set-StyleValue $document $style 'Width' '20'; Set-StyleValue $document $style 'Height' '20'
            Set-StyleValue $document $style 'Margin' '0'
        }
        foreach ($property in @($style.ChildNodes | Where-Object { $_.LocalName.StartsWith('Style.') })) {
            $null=$style.RemoveChild($property); $null=$style.AppendChild($property)
        }
    }
    $settings=New-Object System.Xml.XmlWriterSettings
    $settings.Indent=$true; $settings.OmitXmlDeclaration=$true
    $settings.Encoding=New-Object System.Text.UTF8Encoding($false)
    $writer=[System.Xml.XmlWriter]::Create($file.FullName,$settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
}
