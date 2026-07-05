# MHTML Viewer

A simple MHTML viewer using WebView2.

<a href="#"><img src="Preview.gif" width="800"></a>

## Project Layout

- `src/App/` contains startup, application paths, JSON source generation, and persisted UI state.
- `src/Windows/` contains the Win32/WebView2 host and native interop layer.
- `src/Services/` contains document indexing, tree generation, MHTML parsing, offline asset lookup, and cache storage.
- `src/Models/` contains DTOs and domain records shared by the viewer and services.
- `src/Utilities/` contains small reusable helpers such as embedded resource loading and natural sorting.
- `src/Web/` contains embedded HTML and JavaScript used by the title bar, sidebar, and document viewer.
- `src/Resources/` contains application resources such as `app.ico`.

## Data Folders

- Put captured `.mhtml`, `.mht`, or related local `.html` files under `mhtml/`.
- Optional offline asset mappings are read from `assets/mhtml-uuid.tsv`.
- Runtime state and WebView2 user data are stored under the system temp directory in `MHTMLViewer/`.

## Build

```powershell
dotnet build -c Release
dotnet publish -c Release -f net10.0-windows -r win-x64 -o publish\win-x64
```

The non-Windows target currently builds only the console fallback; the WebView2 UI is implemented for Windows.
