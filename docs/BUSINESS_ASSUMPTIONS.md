# Założenia biznesowe — DigitalSorter

**Wersja:** 0.2

## Cel produktu

- Chronologiczne przeglądanie zdjęć i filmów przechowywanych w wielu folderach Google Drive.
- Zapewnienie dostępu do materiałów bez ich modyfikowania.
- Udostępnienie aplikacji w przeglądarce oraz na Android TV.

## Kanały dostępu

- Aplikacja przeglądarkowa — technologia i framework do ustalenia.
- Aplikacja Android TV — warstwa prezentacji oparta na .NET MAUI.

## Zakres funkcjonalny

- Logowanie do konta Google za zgodą użytkownika.
- Wybór jednego lub wielu folderów Google Drive.
- Prezentowanie zdjęć i filmów na wspólnej osi czasu.
- Sortowanie od najstarszego do najnowszego materiału.
- Wyświetlanie wyłącznie plików z poprawnie rozpoznaną datą i czasem wykonania.
- Tryb podstawowy: aktualny materiał oraz miniatury kilku następnych.
- Tryb pełnoekranowy: aktualny materiał oraz przechodzenie do poprzedniego i następnego pliku.
- Informowanie o plikach niedostępnych lub nieobsługiwanych.

## Reguły porządkowania

- Podstawą kolejności jest data i czas wykonania zapisane w metadanych.
- Data przesłania, utworzenia lub modyfikacji w Google Drive nie zastępuje daty z metadanych.
- Nazwa, typ, rozmiar, lokalizacja GPS i zawartość pliku nie wpływają na kolejność.
- Pliki bez wiarygodnej daty i czasu nie są wyświetlane.

## Bezpieczeństwo danych

- Dostęp do Google Drive jest ograniczony do odczytu.
- Aplikacja nie przechowuje hasła użytkownika.
- Token dostępu jest przechowywany lokalnie na urządzeniu i przesyłany wyłącznie do usług Google.
- Aplikacja nie modyfikuje plików, folderów ani metadanych.

## Poza zakresem

- Praca offline.
- Tworzenie, kopiowanie, przenoszenie, usuwanie i zmiana nazw plików lub folderów.
- Eksportowanie materiałów do lokalnego katalogu.
- Edycja lub uzupełnianie metadanych.
- Sortowanie według innych kryteriów.
- Rozpoznawanie osób, obiektów lub zdarzeń.
