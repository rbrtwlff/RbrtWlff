# AkteTimer

Schlanke Windows-Tray-App zur Akten-Zeiterfassung (Phase 1).

## Download & Start (Anfänger)

### 1) ZIP aus GitHub Actions
1. Öffne das Repository auf GitHub.
2. Klicke auf **Actions**.
3. Wähle den neuesten Workflow-Lauf „build-windows“ (oder einen gewünschten Lauf).
4. Scrolle zu **Artifacts** und lade eines der ZIPs herunter:
   - **AkteTimer-win-x64.zip** (Framework-dependent, benötigt .NET Runtime)
   - **AkteTimer-win-x64-selfcontained.zip** (Self-contained, keine Runtime nötig)

### 2) ZIP aus GitHub Releases
1. Öffne **Releases** im Repository.
2. Wähle den gewünschten Release.
3. Unter **Assets** findest du die ZIPs (gleich wie oben).

### 3) Starten
1. ZIP entpacken.
2. **AkteTimer.exe** starten.
   - Beim ersten Start kann Windows SmartScreen eine Warnung anzeigen.

### Ausgabe-Pfade in der Pipeline
Die GitHub Actions Pipeline erzeugt die Publish-Ausgaben in diesen Ordnern:
- `artifacts/publish/win-x64` (framework-dependent)
- `artifacts/publish/win-x64-selfcontained` (self-contained)

Die ZIPs liegen danach unter:
- `artifacts/AkteTimer-win-x64.zip`
- `artifacts/AkteTimer-win-x64-selfcontained.zip`

## Build & Run

> Voraussetzung: .NET 8 SDK (Windows, WPF).

```bash
dotnet build AkteTimer.sln
```

```bash
dotnet run --project src/AkteTimer/AkteTimer.csproj
```

## Hinweise

- SQLite-Datenbank wird automatisch unter `%APPDATA%\AkteTimer\aktetimer.db` angelegt.
- Globaler Hotkey Standard: `Ctrl+Alt+T`.
- Tray-Menü: Öffnen, Heute, Auswertung, Einstellungen, Beenden.
