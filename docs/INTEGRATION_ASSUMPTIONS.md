# Założenia integracyjne — Google Drive

## Dostęp

- Aplikacja wymaga połączenia z Internetem.
- Użytkownik uwierzytelnia się bezpośrednio w usługach Google.
- Aplikacja nie przechowuje hasła użytkownika.
- Token dostępu jest przechowywany lokalnie na urządzeniu i przesyłany wyłącznie do usług Google.
- Zakres uprawnień jest ograniczony do niezbędnego odczytu.

## Źródła danych

- Użytkownik może wskazać wiele folderów Google Drive.
- Wielokrotnie wskazane identyczne źródła są uwzględniane tylko raz.
- Aplikacja odczytuje zdjęcia, filmy i ich metadane.
- Dane plików i metadane nie są trwale zapisywane przez aplikację.

## Ograniczenia operacji

- Integracja nie tworzy, nie nadpisuje, nie przenosi, nie usuwa ani nie zmienia nazw plików i folderów.
- Integracja nie modyfikuje metadanych.
- Data i czas Google Drive nie zastępują brakującej daty wykonania w metadanych pliku.
- Pliki bez poprawnie rozpoznanej daty i czasu nie są przekazywane do warstwy prezentacji.
