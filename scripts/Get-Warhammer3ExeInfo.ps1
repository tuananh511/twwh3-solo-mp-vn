Add-Type -AssemblyName System.Windows.Forms

$dialog = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Title = "Select Warhammer3.exe"
$dialog.Filter = "Warhammer3.exe|Warhammer3.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*"
$dialog.CheckFileExists = $true
$dialog.FileName = "Warhammer3.exe"

if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    Write-Host "No file selected."
    exit 1
}

$file = Get-Item -LiteralPath $dialog.FileName
$hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
$sha256 = $hash.Hash.ToLowerInvariant()

$jsonSnippet = @"
"exeSha256": "$sha256",
"exeSize": $($file.Length)
"@

Write-Host "Path:   $($file.FullName)"
Write-Host "Size:   $($file.Length)"
Write-Host "SHA256: $sha256"
Write-Host ""
Write-Host "patches.json values:"
Write-Host $jsonSnippet

try {
    Set-Clipboard -Value $jsonSnippet
    Write-Host ""
    Write-Host "Copied patches.json values to clipboard."
}
catch {
    Write-Host ""
    Write-Host "Could not copy to clipboard: $($_.Exception.Message)"
}
