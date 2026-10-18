# Dokumentacja DigitalSorter

Punkt wejścia do projektu i skrócone uruchomienie: [README](../README.md).

## Produkt i implementacja

| Dokument | Zawartość |
|---|---|
| [Wymagania produktowe](PRODUCT_REQUIREMENTS.md) | Cel, funkcje, formaty, zasady przetwarzania i granice produktu. |
| [Integracja z Google Drive](GOOGLE_DRIVE_INTEGRATION.md) | Źródła danych, zakres dostępu i ograniczenia operacji na plikach. |
| [Architektura](ARCHITECTURE.md) | Moduły, zależności, OAuth, sesje, magazyn danych, Web i TV oraz wymagania hostingu. |
| [Backlog](BACKLOG.md) | Zadania produktowe i status ich realizacji. |

## Uruchomienie i utrzymanie

| Dokument | Zawartość |
|---|---|
| [Start na przygotowanym Windows](DEPLOYMENT-LOCAL.md) | Obsługa istniejącej instalacji na laptopie; wymaga niewersjonowanych plików `artifacts/local/`. |
| [Konfiguracja i wdrożenie](DEPLOYMENT.md) | Przygotowanie nowego środowiska, konfiguracja brokera, HTTPS, demo, Google Cloud, hosting, aktualizacje i backup. |
| [Android TV](ANDROID_TV.md) | Wymagania urządzenia, budowanie i instalacja APK, parowanie oraz publikacja w Google Play. |
| [Testowanie](TESTING.md) | Standard testów, polecenia, środowiska, artefakty i zasady odtwarzania wyników. |

## Wyniki historyczne i licencje

[Raport z 18.10.2026](VALIDATION-2026-10-18.md) dokumentuje konkretną weryfikację oraz pomiary, wraz z ich ograniczeniami. Nie zastępuje instrukcji testowania ani potwierdzenia działania późniejszych zmian.

[Licencja projektu](../LICENSE.md) określa warunki użycia własnego kodu. [Informacje o zależnościach dekodera](../src/DigitalSorter.Web/wwwroot/licenses/THIRD_PARTY_NOTICES.txt) prowadzą do licencji i źródeł komponentów zewnętrznych.

Instrukcje pracy wyłącznie dla agenta AI znajdują się w [AGENTS.md](../AGENTS.md). Opis produktu i procedury dla ludzi pozostają w dokumentacji tematycznej.
