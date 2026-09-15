# Architektura — DigitalSorter

## Podział rozwiązania

- `DigitalSorter.Core` — logika aplikacji, odczyt danych z Google Drive, obsługa metadanych i sortowanie.
- `DigitalSorter.AndroidTV` — warstwa prezentacji Android TV oparta na .NET MAUI.
- `DigitalSorter.Web` — warstwa prezentacji dla przeglądarki; technologia pozostaje do ustalenia.

## Core

- Implementacja w języku C# na platformie .NET 10.0.
- Brak zależności od kontrolek i frameworków warstw prezentacji.
- Udostępnianie funkcji przez jawne interfejsy i modele.
- Wspólne reguły biznesowe dla aplikacji przeglądarkowej i Android TV.

## Warstwy prezentacji

- Odpowiadają za interakcję z użytkownikiem i prezentowanie danych dostarczanych przez Core.
- Nie zawierają logiki dostępu do Google Drive ani reguł sortowania.
- Mogą mieć osobne widoki i nawigację dostosowane do urządzenia.

## Decyzje otwarte

- Technologia i framework aplikacji przeglądarkowej.
- Miejsce uruchamiania Core dla wersji przeglądarkowej: przeglądarka albo backend.
