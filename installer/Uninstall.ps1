param([switch]$Quiet)
$ErrorActionPreference='Stop'
try {
    $installRoot=[IO.Path]::GetFullPath($PSScriptRoot)
    $expectedRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Minova Cloud'))
    if($installRoot -ne $expectedRoot -or ((Get-Item -LiteralPath $installRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected installation location. No files were removed.'}
    Add-Type -AssemblyName System.Windows.Forms
    if(-not $Quiet){$answer=[Windows.Forms.MessageBox]::Show('Uninstall Minova Cloud from this PC? Your cloud storage and downloaded files are kept. Online-only files remain available on the website.','Uninstall Minova Cloud','YesNo','Question');if($answer -ne 'Yes'){exit 0}}
    $appExecutable=Join-Path $installRoot 'Minova.Cloud.exe'
    $exitProcess=Start-Process -FilePath $appExecutable -ArgumentList '--exit' -WindowStyle Hidden -PassThru -Wait
    foreach($running in (Get-Process -Name 'Minova.Cloud' -ErrorAction SilentlyContinue)){if($running.Path -eq $appExecutable){if(-not $running.WaitForExit(15000)){throw 'Close Minova Cloud and try uninstalling again.'}}}
    $removeIntegration=Start-Process -FilePath $appExecutable -ArgumentList '--remove-integration' -WindowStyle Hidden -PassThru -Wait
    if($removeIntegration.ExitCode -ne 0){throw 'Windows could not disconnect the Minova folder. Your app and files were kept.'}
    $manifest=Get-Content -LiteralPath (Join-Path $installRoot 'installed-files.json') -Raw | ConvertFrom-Json
    $directories=New-Object 'System.Collections.Generic.HashSet[string]'
    foreach($relative in $manifest){
        $target=[IO.Path]::GetFullPath((Join-Path $installRoot $relative))
        if(-not $target.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid uninstall file list.'}
        for($parent=[IO.Path]::GetDirectoryName($target);$parent -ne $installRoot;$parent=[IO.Path]::GetDirectoryName($parent)){
            if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'A linked installation folder was found. No linked files were removed.'}
            [void]$directories.Add($parent)
        }
        if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
    }
    Remove-Item -LiteralPath (Join-Path $installRoot 'installed-files.json') -Force
    foreach($directory in ($directories | Sort-Object Length -Descending)){try{[IO.Directory]::Delete($directory,$false)}catch{}}
    $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Minova Cloud.lnk';if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut -Force}
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\MinovaCloud',$false)
    try{[IO.Directory]::Delete($installRoot,$false)}catch{}
} catch {
    if(-not $Quiet){[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Minova Cloud uninstall','OK','Error') | Out-Null}
    exit 1
}
