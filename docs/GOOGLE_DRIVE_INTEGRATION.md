# Integracja z Google Drive

## Dostęp i uwierzytelnianie

- Aplikacja wymaga połączenia z Internetem.
- Użytkownik uwierzytelnia się bezpośrednio w usługach Google.
- Aplikacja nie przechowuje hasła użytkownika.
- Token dostępu jest przechowywany lokalnie na urządzeniu i przesyłany wyłącznie do usług Google.
- Zakres uprawnień jest ograniczony do niezbędnego odczytu.

## Źródła danych

- Użytkownik może wskazać wiele folderów Google Drive.
- Wielokrotnie wskazane identyczne źródła są uwzględniane tylko raz.
- Integracja odczytuje zdjęcia, filmy i ich metadane.
- Wszystkie odczytane materiały, również bez poprawnej daty i czasu, są przekazywane do warstwy Core.
- Dane plików i metadane nie są trwale zapisywane przez integrację.

## Ograniczenia operacji

- Integracja nie tworzy, nie nadpisuje, nie przenosi, nie usuwa ani nie zmienia nazw plików i folderów.
- Integracja nie modyfikuje metadanych.
