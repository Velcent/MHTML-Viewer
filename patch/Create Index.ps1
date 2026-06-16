param(
    [string]$RootPath = ".",
    [string]$OutputFile = "index.html"
)

# =========================
# SORT KEY (NATURAL & SAFE)
# =========================
function Get-SortKey {
    param([string]$name)

    $parts = [regex]::Matches($name, '\d+|\D+')

    $key = @()

    foreach ($m in $parts) {
        $v = $m.Value

        if ($v -match '^\d+$') {
            # angka → padding biar string compare tetap benar
            $key += "{0:D10}" -f [int]$v
        } else {
            $key += $v.ToLower()
        }
    }

    # gabung jadi string (AMAN untuk semua PowerShell)
    return ($key -join "|")
}

# =========================
# GET FILES
# =========================
$files = Get-ChildItem -Path $RootPath -Recurse -Filter *.mhtml

# =========================
# BUILD TREE
# =========================
function Build-Tree {
    param($files, $root)

    $tree = @{}

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length).TrimStart("\")
        $parts = $relative -split "\\"

        $current = $tree

        for ($i = 0; $i -lt $parts.Length; $i++) {
            $part = $parts[$i]

            if ($i -eq $parts.Length - 1) {
                $baseName = [System.IO.Path]::GetFileNameWithoutExtension($part)

                if (-not $current.ContainsKey($baseName)) {
                    $current[$baseName] = @{
                        __file = $relative
                        __children = @{}
                    }
                } else {
                    $current[$baseName]["__file"] = $relative
                }

            } else {
                if (-not $current.ContainsKey($part)) {
                    $current[$part] = @{
                        __children = @{}
                    }
                }
                $current = $current[$part]["__children"]
            }
        }
    }

    return $tree
}

# =========================
# RENDER TREE
# =========================
function Render-Tree {
    param($node)

    $html = "<ul>"

    # ✅ COMPATIBLE SORT
    $keys = $node.Keys | Sort-Object { Get-SortKey $_ }

    foreach ($key in $keys) {
        $item = $node[$key]

        $hasChildren = $item.ContainsKey("__children") -and $item["__children"].Count -gt 0
        $hasFile = $item.ContainsKey("__file")

        $safePath = ""
        if ($hasFile) {
            $safePath = $item["__file"].Replace("\", "/")
        }

        $html += "<li>"

        # toggle
        if ($hasChildren) {
            $html += "<span class='toggle' onclick=""toggleFolder(event, this)"">▶</span>"
        } else {
            $html += "<span class='toggle empty'></span>"
        }

        # link / label
        if ($hasFile) {
            $html += "<span class='link' onclick=""loadFile('$safePath')"">$key</span>"
        } else {
            $html += "<span class='label'>$key</span>"
        }

        # children
        if ($hasChildren) {
            $html += "<div class='children'>"
            $html += Render-Tree $item["__children"]
            $html += "</div>"
        }

        $html += "</li>"
    }

    $html += "</ul>"
    return $html
}

# =========================
# GENERATE HTML
# =========================
$rootResolved = (Resolve-Path $RootPath).Path
$tree = Build-Tree -files $files -root $rootResolved
$toc = Render-Tree $tree

$html = @"
<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<title>MHTML Viewer</title>

<style>
body { margin:0; display:flex; height:100vh; font-family:Arial; }
#sidebar { width:20%; overflow:auto; background:#1e1e1e; color:#ccc; padding:10px; }
#content { width:80%; }
iframe { width:100%; height:100%; border:none; }

ul { list-style:none; padding-left:10px; }
li { margin:2px 0; }

.toggle { cursor:pointer; display:inline-block; width:16px; color:#ccc; }
.toggle.empty { visibility:hidden; }

.link { cursor:pointer; color:#4ec9b0; }
.link:hover { text-decoration:underline; }

.label { color:#ccc; }

.children { display:none; margin-left:14px; }
.open > .children { display:block; }
</style>
</head>

<body>

<div id="sidebar">
$toc
</div>

<div id="content">
    <iframe id="viewer"></iframe>
</div>

<script>
function toggleFolder(e, el) {
    e.stopPropagation();
    const li = el.parentElement;
    li.classList.toggle('open');
    el.textContent = li.classList.contains('open') ? "▼" : "▶";
}

function loadFile(path) {
    document.getElementById('viewer').src = path;
}
</script>

</body>
</html>
"@

$html | Out-File -Encoding UTF8 $OutputFile
