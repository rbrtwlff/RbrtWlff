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

## Hinweise

- SQLite-Datenbank wird automatisch unter `%APPDATA%\AkteTimer\aktetimer.db` angelegt.
- Globaler Hotkey Standard: `Ctrl+Alt+T`.
- Tray-Menü: Öffnen, Heute, Auswertung, Einstellungen, Beenden.
