# Integracja z Google Drive

## Dostęp i uwierzytelnianie

- Aplikacja wymaga połączenia z Internetem.
- Użytkownik uwierzytelnia się bezpośrednio w usługach Google; aplikacja nie przechowuje loginu ani hasła.
- Web i Android TV korzystają ze wspólnego `DigitalSorter.Auth`; TV jest parowany z kontem przez stronę brokera na telefonie.
- Google `refresh_token` jest przechowywany wyłącznie na backendzie, w postaci zaszyfrowanej; broker odnawia dostęp dla obu klientów.
- Klient przechowuje `access_token` w pamięci i pobiera multimedia bezpośrednio z Drive. Backend nie pośredniczy w przesyłaniu plików.
- Przepływ OAuth, sesje i parowanie określa [architektura](ARCHITECTURE.md#wspólny-broker-oauth).

## Źródła danych

- Użytkownik może wskazać wiele folderów Google Drive.
- Wielokrotnie wskazane identyczne źródła są uwzględniane tylko raz.
- Zakres `drive.readonly` daje odczyt całego dostępnego Drive; wybór folderów ogranicza przetwarzanie w aplikacji.
- Integracja odczytuje zdjęcia, filmy i ich metadane.
- Wszystkie odczytane materiały, również bez poprawnej daty i czasu, są przekazywane do warstwy Core.
- Dane plików i metadane nie są trwale zapisywane przez integrację.

## Ograniczenia operacji

- Integracja nie tworzy, nie nadpisuje, nie przenosi, nie usuwa ani nie zmienia nazw plików i folderów.
- Integracja nie modyfikuje metadanych.
- Unieważnienie lub wygaśnięcie trwałej autoryzacji wymaga ponownego połączenia konta Google.
- Wylogowanie jednego klienta nie cofa zgody Google; odłączenie konta obejmuje web i powiązane telewizory.
