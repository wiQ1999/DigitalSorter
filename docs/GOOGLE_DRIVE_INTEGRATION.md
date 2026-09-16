# Integracja z Google Drive

## Dostęp i uwierzytelnianie

- Aplikacja wymaga połączenia z Internetem.
- Użytkownik uwierzytelnia się bezpośrednio w usługach Google; aplikacja nie przechowuje loginu ani hasła.
- Aplikacja webowa używa Google OAuth 2.0 Authorization Code Flow z `access_type=offline` i zakresem `drive.readonly`.
- Backend OAuth przechowuje zaszyfrowany `refresh_token` i automatycznie uzyskuje nowe `access_tokeny`, dopóki autoryzacja użytkownika pozostaje ważna.
- `access_token` jest przekazywany do aplikacji webowej, przechowywany wyłącznie w pamięci i używany bezpośrednio do wywołań Google Drive API.
- Backend nie pośredniczy w pobieraniu zdjęć ani filmów z Google Drive.

## Źródła danych

- Użytkownik może wskazać wiele folderów Google Drive.
- Wielokrotnie wskazane identyczne źródła są uwzględniane tylko raz.
- Integracja odczytuje zdjęcia, filmy i ich metadane.
- Wszystkie odczytane materiały, również bez poprawnej daty i czasu, są przekazywane do warstwy Core.
- Dane plików i metadane nie są trwale zapisywane przez integrację.

## Ograniczenia operacji

- Integracja nie tworzy, nie nadpisuje, nie przenosi, nie usuwa ani nie zmienia nazw plików i folderów.
- Integracja nie modyfikuje metadanych.
- Unieważnienie lub wygaśnięcie trwałej autoryzacji wymaga ponownego połączenia konta Google.
