# AkteTimer

Schlanke Windows-Tray-App zur Akten-Zeiterfassung (Phase 1).

## Build & Run

> Voraussetzung: .NET 8 SDK (Windows, WPF).

```bash
dotnet build AkteTimer.sln
```

```bash
dotnet run --project src/AkteTimer/AkteTimer.csproj
```

## EXE aus GitHub Actions/Release herunterladen und starten

### Aus GitHub Actions (Artifacts)
1. Öffne den Tab **Actions** im Repository und wähle den gewünschten Workflow-Run.
2. Scrolle zu **Artifacts** und lade eine der ZIP-Dateien herunter:
   - `AkteTimer-win-x64.zip` (framework-dependent, benötigt .NET 8 Runtime)
   - `AkteTimer-win-x64-selfcontained.zip` (self-contained, keine zusätzliche Runtime nötig)
3. Entpacke die ZIP-Datei.
4. Starte `AkteTimer.exe` aus dem entpackten Ordner.

### Aus GitHub Releases (Release Assets)
1. Öffne den Tab **Releases** im Repository und wähle das gewünschte Release.
2. Lade dort eine der ZIP-Dateien (Assets) herunter.
3. Entpacke die ZIP-Datei.
4. Starte `AkteTimer.exe` aus dem entpackten Ordner.

## Hinweise

- SQLite-Datenbank wird automatisch unter `%APPDATA%\AkteTimer\aktetimer.db` angelegt.
- Globaler Hotkey Standard: `Ctrl+Alt+T`.
- Tray-Menü: Öffnen, Heute, Auswertung, Einstellungen, Beenden.
