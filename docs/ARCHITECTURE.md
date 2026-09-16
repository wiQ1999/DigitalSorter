# Architektura — DigitalSorter

## Warstwy rozwiązania

- `DigitalSorter.Core` — logika aplikacji, walidacja metadanych, klasyfikowanie materiałów i sortowanie.
- `DigitalSorter.Data.GoogleDrive` — uwierzytelnianie oraz odczyt plików i metadanych z Google Drive.
- `DigitalSorter.AndroidTV` — warstwa prezentacji Android TV oparta na .NET MAUI.
- `DigitalSorter.Web` — warstwa prezentacji dla przeglądarki; technologia pozostaje do ustalenia.

## Core

- Implementacja w języku C# na platformie .NET 10.0.
- Brak zależności od Google Drive oraz frameworków warstw prezentacji.
- Definiowanie interfejsów dostępu do danych i wspólnych modeli.
- Wspólne reguły biznesowe dla wszystkich warstw prezentacji.

## Zależności

- Warstwy prezentacji korzystają wyłącznie z interfejsów i modeli Core.
- Warstwa danych implementuje interfejsy zdefiniowane w Core.
- Konfiguracja aplikacji łączy implementacje warstw przez wstrzykiwanie zależności.
- Logika dostępu do Google Drive nie występuje w Core ani w warstwach prezentacji.

## Standard testów

### Testy jednostkowe

- Core musi być pokryty testami jednostkowymi zgodnymi ze schematem Arrange–Act–Assert.
- Testy muszą być deterministyczne i niezależne od sieci, systemu plików oraz usług zewnętrznych.
- Minimalne pokrycie Core: 80% linii i 80% gałęzi.
- Reguły walidacji daty, klasyfikowania materiałów i sortowania wymagają testów wszystkich istotnych przypadków.

### Testy integracyjne

- Warstwa Google Drive jest testowana przez jej publiczne interfejsy z użyciem kontrolowanych danych testowych.
- Testy obejmują uwierzytelnianie, wiele folderów, brak metadanych oraz błędy dostępu i sieci.
- Testy nie mogą korzystać z danych rzeczywistych użytkowników.

### Pozostałe warstwy

- Dla warstw prezentacji zalecane są testy logiki widoku, nawigacji, licznika i trybu pełnoekranowego.
- Potok CI odrzuca zmianę, gdy testy nie przechodzą lub Core nie spełnia wymaganego pokrycia.

## Decyzje otwarte

- Technologia i framework aplikacji przeglądarkowej.
- Miejsce uruchamiania Core dla wersji przeglądarkowej: przeglądarka albo backend.
