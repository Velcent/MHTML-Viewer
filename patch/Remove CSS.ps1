Get-ChildItem -Path . -Filter *.mhtml -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace '(?ms)^Content-Location: https://dev\.epicgames\.com/documentation/assets/styles\..*?(?=^------MultipartBoundary)', ''
    Set-Content $_.FullName $content
}