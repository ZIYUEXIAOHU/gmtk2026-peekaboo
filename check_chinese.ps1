$files = Get-ChildItem -Path 'C:\code\2d\gmtk2026-peekaboo\Assets\Scripts\UI\' -Filter '*.cs'
foreach ($f in $files) {
    $content = Get-Content -Raw -Encoding UTF8 $f.FullName
    if ($content -match '[\u4e00-\u9fff]') {
        Write-Output $f.Name
    }
}
