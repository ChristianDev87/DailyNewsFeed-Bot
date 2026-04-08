# DailyNewsFeed — Bot

> Dieses Repository ist Teil des **DailyNewsFeed**-Systems — einer selbst gehosteten Discord-Nachrichtenplattform.

| [🤖 Bot](https://github.com/ChristianDev87/DailyNewsFeed-Bot) | [🌐 Frontend](https://github.com/ChristianDev87/DailyNewsFeed-Frontend) | [🐍 Watchdog](https://github.com/ChristianDev87/DailyNewsFeed-Watchdog) |
|:---:|:---:|:---:|
| .NET 9 Discord Bot | PHP 8 Web-Interface | Python Watchdog |

*Irgendwann sagt man Ja — entstanden aus den Wünschen guter Freunde.* 🙌

---

.NET 9 Discord Bot für DailyNewsFeed. Sendet RSS-Digests in Discord-Kanäle, unterstützt Slash-Commands und einen integrierten Scheduler.

## Voraussetzungen

- .NET 9 SDK oder Runtime
- MySQL 8 / MariaDB 10.6+
- Ein Discord-Bot-Account mit aktivierten Gateway Intents

## Installation

### 1. Repository klonen und bauen

```bash
git clone https://github.com/ChristianDev87/DailyNewsFeed-Bot.git /opt/daily-news-bot
cd /opt/daily-news-bot
dotnet publish DailyNewsBot -c Release -o /opt/daily-news-bot/publish/
```

### 2. Umgebungsvariablen konfigurieren

```bash
nano /opt/daily-news-bot/publish/.env
```

| Variable | Pflicht | Beschreibung |
|---|---|---|
| `DISCORD_BOT_TOKEN` | ✅ | Bot-Token aus dem Discord Developer Portal |
| `TOKEN_ENCRYPTION_KEY` | ✅ | 32-Byte-Schlüssel als Base64 — **muss identisch mit dem Frontend sein** |
| `DB_USER` | ✅ | Datenbankbenutzer |
| `DB_PASS` | ✅ | Datenbankpasswort |
| `DB_HOST` | — | Datenbank-Host (Standard: `localhost`) |
| `DB_PORT` | — | Datenbank-Port (Standard: `3306`) |
| `DB_NAME` | — | Datenbankname (Standard: `daily_news`) |
| `DB_MAX_POOL_SIZE` | — | Connection-Pool-Größe (Standard: `20`) |
| `MAX_PARALLEL_FEEDS` | — | Parallele Feed-Abrufe (Standard: `10`) |
| `DASHBOARD_URL` | — | URL des Web-Interfaces (z. B. `https://deine-domain.de`) |

Schlüssel generieren (identisch mit Frontend verwenden):
```bash
openssl rand -base64 32
```

### 3. Discord-Bot einrichten

1. [discord.com/developers/applications](https://discord.com/developers/applications) → Applikation auswählen → **Bot**
2. Token kopieren → `DISCORD_BOT_TOKEN`
3. **Privileged Gateway Intents**: `Server Members Intent` und `Message Content Intent` aktivieren
4. Bot zum Server einladen: **OAuth2 → URL Generator** → Scopes: `bot`, `applications.commands` → Berechtigungen: `Send Messages`, `Read Message History`, `Create Public Threads`

### 4. Als systemd-Service betreiben

Service-Datei kopieren:

```bash
cp /opt/daily-news-bot/daily-news-bot.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable daily-news-bot
systemctl start daily-news-bot
```

Status prüfen:

```bash
systemctl status daily-news-bot --no-pager
journalctl -u daily-news-bot -f
```

### 5. Manuell testen (vor systemd)

```bash
dotnet /opt/daily-news-bot/publish/DailyNewsBot.dll
```

Der Bot meldet sich mit `Bot online als Daily News` wenn die Verbindung steht.

## Slash-Commands

| Befehl | Beschreibung |
|---|---|
| `/dnews setup` | Kanal für den Bot registrieren |
| `/dnews senden` | Digest sofort auslösen |
| `/dnews status` | Aktuellen Status anzeigen |
| `/dnews feeds` | Konfigurierte Feeds anzeigen |
| `/dnews pause` | Automatischen Digest pausieren |
| `/dnews fortsetzen` | Digest fortsetzen |

## Verzeichnisstruktur

```
DailyNewsBot/
├── DailyNewsBot/
│   ├── Commands/        ← Slash-Commands (/dnews)
│   ├── Data/            ← Datenbankzugriff (MySqlConnector + Dapper)
│   ├── Models/          ← Datenmodelle
│   ├── Processing/      ← TextProcessor, ChunkBuilder, FeedFetcher
│   ├── Services/        ← BotService, DigestService, SchedulerService
│   ├── Program.cs       ← Einstiegspunkt
│   └── appsettings.json
├── DailyNewsBot.Tests/  ← Unit Tests (xUnit)
└── daily-news-bot.service ← systemd Service-Vorlage
```

## Technologie-Stack

- **.NET 9** — Generic Host / Worker Service
- **Discord.Net 3.15** — Gateway + REST Client
- **MySqlConnector + Dapper** — Datenbankzugriff
- **CodeHollow.FeedReader** — RSS/Atom Parsing
- **Serilog** — Structured Logging (Console + JSON-Datei, Loki-ready)

## Logging

Logs werden geschrieben nach:
- **Console** — lesbares Format mit Timestamp und Log-Level
- **`logs/bot-YYYYMMDD.log`** — CLEF/JSON (täglich rollend, 14 Tage Aufbewahrung)

Die Log-Dateien liegen relativ zum Arbeitsverzeichnis (`WorkingDirectory` im systemd-Service).

## Geplant

- **Docker** — alle drei Komponenten sollen als einzelne Container betrieben werden können; Konfiguration über externe Umgebungsvariablen — entweder direkt per `docker run -e` oder über `docker-compose` mit externer `.env`-Datei
- **Benutzer-Zeitzone** — Digest-Zeiten pro Kanal konfigurierbar statt global (aktuell: `Europe/Berlin`)
