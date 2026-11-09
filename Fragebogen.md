# Fragebogen zum Programmentwurf Webengineering 2

## Gruppeninformationen

Gruppenname: Stoetzel Reservations

Gruppenteilnehmer:

- Nicolai Stötzel

Arbeitsform: Einzelarbeit

Kurs: TINF24B3

## Quellcode

Vorgesehenes Repository für den Quellcode:

- https://github.com/Chickenniggo/Reservation.git
- Ein gemeinsames Repository für Backend, Tests, Container und Kustomize-Anpassungen ist vorgesehen.

## Containerregistry

Geplant ist die GitHub Container Registry (GHCR) mit dem Image-Namen `ghcr.io/chickenniggo/reservation`.
Build und Veröffentlichung sollen über GitHub Actions erfolgen. Für die spätere Implementierungsabgabe
soll das Image öffentlich zugänglich gemacht oder alternativ als OCI-Archiv mitgeliefert werden.

## Lizenz

Vorgesehener SPDX-License-Identifier und geplanter Dateiname im Quellcode:

- Für eigenen Backend-Quellcode, Tests und Deployment-Anpassungen ist `MIT` mit der Lizenzdatei `LICENSE` vorgesehen.
- Die jeweiligen Lizenzen externer Abhängigkeiten und der biletado-Umgebung sollen berücksichtigt werden.

## Sprache und Framework

| Frage | Geplante Auswahl |
|---|---|
| Programmiersprache | C# |
| Sprachversion | C# 12, Zielplattform .NET 8 LTS |
| Framework (FW) | ASP.NET Core, Minimal APIs |
| FW-Version | ASP.NET Core 8.0; JWT-Paket 8.0.27 |
| Website zum FW | https://learn.microsoft.com/aspnet/core/ |
| Prepared statements/ORM | Npgsql, parametrisierte SQL-Abfragen und automatische Prepared Statements in der Standardkonfiguration; kein ORM |
| ORM-/Treiberversion | Npgsql 8.0.8 |
| Website zum Treiber | https://www.npgsql.org/ |
| Datenbank | PostgreSQL 17 |

## Geplante Automatisierung

- GitHub Actions soll Paketwiederherstellung, automatisierte Tests und den Release-Build ausführen.
- Ein mehrstufiger Container-Build ist vorgesehen: SDK-Image zum Bauen und ein schlankes ASP.NET-Laufzeit-Image
    ohne Compiler für den Betrieb als Nicht-Root-Benutzer.
- Die Anwendung soll über eigene Kustomize-Anpassungen in die vorgegebene Kubernetes-Umgebung integriert werden.
- Nach erfolgreichen Tests soll das Container-Image gebaut und in GHCR veröffentlicht werden.
- Für die spätere Abgabe soll ein Quellcodearchiv einschließlich Dokumentation und Testergebnissen erstellt werden.

## Geplante Testautomatisierung

- Eigene xUnit-Tests sollen Eingabevalidierung, HTTP-Statuscodes, JWT-Signatur und Gültigkeit, Reservierungsablauf,
    Soft Delete, Wiederherstellung, Filter, Überschneidungen, konkurrierende Schreibzugriffe, ETags und Readiness prüfen.
- Integrationstests gegen eine separate PostgreSQL-Testdatenbank sind vorgesehen.
- Die eigenen Tests sollen automatisiert in GitHub Actions ausgeführt werden.
- Ergänzend sollen die bereitgestellten httpyac-Tests zur Prüfung der API-Konformität verwendet werden;
    sie sollen die eigenen Tests nicht ersetzen.

Wie sollen die Ergebnisse einzusehen sein?

- Testergebnisse sollen im Protokoll des GitHub-Actions-Testlaufs im angegebenen Repository einsehbar sein.
- Zusätzlich sollen Testberichte als herunterladbare Artefakte bereitgestellt werden.
- Für lokale Testläufe und die spätere Abgabe sind Testberichte im TRX-Format vorgesehen.

## Geplanter Funktionsumfang

Geplant ist ein Backend für reservations-v4 gemäß PDF und OpenAPI **4.0.0** mit öffentlichen Lesezugriffen,
JWT-geschützten CRUD-Zugriffen, Raumprüfung über die assets-API, Readiness mit assets-Liveness,
Soft Delete/Wiederherstellung, Filtern und ETag/If-Match.

Öffentliche Schreibzugriffe aus der neueren Online-Spezifikation 4.1 sind gemäß PDF nicht Bestandteil dieser Planung.
Eine README mit Betriebsanleitung und Beschreibung der Konfigurationsparameter soll ebenfalls erstellt werden.