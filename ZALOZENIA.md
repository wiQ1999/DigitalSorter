# Założenia produktu — DigitalSorter

**Wersja:** 0.1  
**Podstawa:** analiza prototypu `wiQ1999/DigitalSorter` (gałąź `master`) oraz ustalenia biznesowe.

## Cel biznesowy

- Ułatwienie chronologicznego porządkowania zdjęć i filmów przechowywanych na Google Drive.
- Ograniczenie kryterium sortowania wyłącznie do daty i czasu zapisanych w metadanych pliku.
- Zapewnienie bezpiecznego, tylko do odczytu dostępu do materiałów użytkownika.

## Użytkownik i środowisko

- Użytkownikiem jest osoba posiadająca konto Google i dostęp do wskazanych folderów.
- Aplikacja działa jako program desktopowy dla systemu Windows.
- Do logowania, przeglądania folderów oraz odczytu plików wymagane jest połączenie z Internetem.
- Brak połączenia z Internetem uniemożliwia wykonanie pełnego procesu.

## Zakres funkcjonalny

- Logowanie do Google Drive za zgodą użytkownika.
- Wybór jednego lub wielu folderów źródłowych.
- Odczyt listy zdjęć i filmów z wybranych folderów.
- Eliminowanie wielokrotnie wskazanych, identycznych źródeł.
- Odczyt metadanych plików bez ich trwałego pobierania.
- Ustalenie daty i czasu wykonania materiału na podstawie metadanych, np. EXIF `DateTimeOriginal`.
- Sortowanie materiałów wyłącznie według ustalonej daty i czasu.
- Prezentowanie plików bez poprawnych metadanych jako nierozpoznanych; bez używania daty modyfikacji Google Drive jako zamiennika.
- Informowanie o plikach pominiętych, nieobsługiwanych lub niedostępnych.

## Bezpieczeństwo danych

- Dostęp do Google Drive odbywa się w trybie tylko do odczytu.
- Aplikacja nie tworzy, nie nadpisuje, nie przenosi, nie usuwa ani nie zmienia nazw plików i folderów na Google Drive.
- Aplikacja nie modyfikuje metadanych.
- Dane plików i odczytane metadane nie są trwale zapisywane przez aplikację.
- Uprawnienia Google są ograniczone do minimalnego zakresu potrzebnego do odczytu.
- Dane logowania i tokeny dostępu są chronione zgodnie z mechanizmami systemu operacyjnego.

## Reguły sortowania

- Podstawą kolejności jest data i czas wykonania zapisane w metadanych.
- Nazwa pliku, data przesłania oraz data utworzenia lub modyfikacji w Google Drive nie wpływają na kolejność.
- Lokalizacja GPS, zawartość obrazu i inne metadane nie wpływają na kolejność.
- Dla zdjęć i filmów obowiązuje jedna, wspólna oś czasu.
- Pliki bez wiarygodnej daty i czasu nie są automatycznie umieszczane na osi czasu.

## Poza zakresem

- Praca offline.
- Zmiana struktury folderów na Google Drive.
- Kopiowanie lub eksportowanie plików do lokalnego katalogu.
- Zmiana nazw plików według daty, czasu lub lokalizacji.
- Sortowanie według lokalizacji, nazwy, typu, rozmiaru lub treści.
- Edycja i uzupełnianie brakujących metadanych.
- Rozpoznawanie osób, obiektów lub zdarzeń na zdjęciach i filmach.

## Założenia techniczne wynikające z prototypu

- Technologia: WPF, .NET 7, architektura zbliżona do MVVM.
- Biblioteka odczytu metadanych: `MetadataExtractor` 2.8.1.
- Obecny prototyp odczytuje datę i czas wykonania z EXIF dla pliku wskazanego ścieżką lokalną.
- Integracja Google Drive, obsługa filmów, wybór folderów i właściwy proces sortowania wymagają implementacji.
- Dostępne w kodzie warianty nazewnictwa i lokalizacji nie należą do przyjętego zakresu biznesowego.
