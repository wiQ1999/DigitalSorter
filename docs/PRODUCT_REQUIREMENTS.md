# Wymagania produktowe — DigitalSorter

**Wersja:** 0.3

## Cel produktu

- Chronologiczne przeglądanie zdjęć i filmów bez modyfikowania materiałów źródłowych.
- Udostępnienie aplikacji w przeglądarce oraz na Android TV.

## Kanały dostępu

- Aplikacja przeglądarkowa — standalone Blazor WebAssembly hostowany na GitHub Pages projektu.
- Aplikacja Android TV — warstwa prezentacji oparta na .NET MAUI.

## Przetwarzanie materiałów

- Materiały z poprawną datą i czasem wykonania są sortowane od najstarszego do najnowszego i umieszczane na liście głównej.
- Data przesłania, utworzenia lub modyfikacji pliku nie zastępuje daty wykonania z metadanych.
- Materiały bez poprawnej daty i czasu są dalej przetwarzane, oznaczane jako niepoprawne i umieszczane na osobnej liście.
- Materiały niepoprawne nie są wyświetlane na liście głównej.
- Lista materiałów niepoprawnych jest dostępna do wglądu.
- Nazwa, typ, rozmiar, lokalizacja GPS i zawartość pliku nie wpływają na kolejność.

## Widok aplikacji

- Widok zawiera aktualny materiał oraz miniatury kilku następnych.
- Użytkownik może przechodzić do poprzedniego i następnego materiału.
- Licznik pokazuje numer aktualnego materiału i liczbę materiałów na bieżącej liście.
- Aktywny materiał można wyświetlić w trybie pełnoekranowym.
- Aplikacja informuje o materiałach niedostępnych lub nieobsługiwanych.

## Ochrona danych

- Korzystanie z aplikacji nie powoduje zmian w materiałach źródłowych.
- Dane użytkownika są wykorzystywane wyłącznie do realizacji funkcji aplikacji.

## Poza zakresem

- Praca offline.
- Edycja lub organizowanie materiałów źródłowych.
- Eksportowanie materiałów do lokalnego katalogu.
- Edycja lub uzupełnianie metadanych.
- Sortowanie według innych kryteriów.
- Rozpoznawanie osób, obiektów lub zdarzeń.
