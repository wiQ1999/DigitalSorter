# Architektura — DigitalSorter

## Warstwy rozwiązania

- `DigitalSorter.Core` — logika aplikacji, walidacja metadanych, klasyfikowanie materiałów i sortowanie.
- `DigitalSorter.Data.GoogleDrive` — odczyt plików i metadanych z Google Drive.
- `DigitalSorter.Auth` — backend pełniący rolę brokera OAuth dla aplikacji webowej.
- `DigitalSorter.AndroidTV` — warstwa prezentacji Android TV oparta na .NET MAUI.
- `DigitalSorter.Web` — warstwa prezentacji dla przeglądarki oparta na standalone Blazor WebAssembly.

## Core

- Implementacja w języku C# na platformie .NET 10.0.
- Brak zależności od Google Drive, brokera OAuth oraz frameworków warstw prezentacji.
- Definiowanie interfejsów dostępu do danych i wspólnych modeli.
- Wspólne reguły biznesowe dla wszystkich warstw prezentacji.

## Zależności

- Warstwy prezentacji korzystają wyłącznie z interfejsów i modeli Core.
- Warstwa danych implementuje interfejsy zdefiniowane w Core.
- Konfiguracja aplikacji łączy implementacje warstw przez wstrzykiwanie zależności.
- Logika dostępu do Google Drive nie występuje w Core ani w warstwach prezentacji.
- Broker OAuth odpowiada wyłącznie za uwierzytelnianie aplikacji webowej, sesję użytkownika i zarządzanie tokenami Google.

## Aplikacja webowa

- `DigitalSorter.Web` jest aplikacją standalone Blazor WebAssembly wykonywaną po stronie przeglądarki.
- Opublikowane pliki statyczne są hostowane przez GitHub Pages projektu.
- `DigitalSorter.Auth` działa jako niezależny backend OAuth/token broker; nie hostuje interfejsu ani logiki biznesowej Core.
- Wywołania Google Drive są wykonywane bezpośrednio z przeglądarki, dlatego zdjęcia i filmy nie przechodzą przez backend.
- Budowanie i publikowanie frontendu realizuje GitHub Actions z użyciem `dotnet publish`.

### Broker OAuth

- Dostęp webowy wykorzystuje Google OAuth 2.0 Authorization Code Flow z `access_type=offline` oraz zakresem `drive.readonly`.
- Backend wymienia kod autoryzacyjny na tokeny i trwale przechowuje wyłącznie zaszyfrowany `refresh_token` oraz dane wymagane do powiązania go z użytkownikiem.
- Sesja aplikacji webowej jest utrzymywana przez bezpieczne cookie `HttpOnly` i `Secure`.
- Po ponownym uruchomieniu aplikacji lub wygaśnięciu `access_token` backend używa `refresh_token` do uzyskania nowego tokenu bez ponownej zgody użytkownika, dopóki autoryzacja pozostaje ważna.
- `access_token` jest przekazywany do `DigitalSorter.Web`, przechowywany wyłącznie w pamięci aplikacji i używany do bezpośrednich wywołań Google Drive API.
- `refresh_token` i OAuth `client_secret` nigdy nie są przekazywane do przeglądarki.
- Unieważnienie autoryzacji wymaga ponownego połączenia konta Google.

### GitHub Pages — wymagania techniczne

- Aplikacja jest publikowana jako Project Pages pod ścieżką `/DigitalSorter/`, dlatego `base href` musi wskazywać `/DigitalSorter/`; wartość jest ustawiana w procesie publikacji.
- GitHub Pages nie obsługuje reguły SPA fallback do `index.html`; bezpośrednie wejście lub odświeżenie trasy Blazor wymaga `404.html`, który przekierowuje do aplikacji i odtwarza pierwotną trasę.
- Publikacja zawiera plik `.nojekyll`, aby katalog Blazor `_framework` nie był pomijany przez przetwarzanie Jekyll.
- Pliki JavaScript publikacji muszą być chronione przed zmianą zakończeń linii przez Git, np. przez odpowiednią regułę w `.gitattributes`, aby nie naruszyć kontroli integralności zasobów Blazor.
- Backend OAuth musi zezwalać na żądania wyłącznie z dozwolonych originów aplikacji webowej i obsługiwać HTTPS.

## Standard testów

### Testy jednostkowe

- Core musi być pokryty testami jednostkowymi zgodnymi ze schematem Arrange–Act–Assert.
- Testy muszą być deterministyczne i niezależne od sieci, systemu plików oraz usług zewnętrznych.
- Minimalne pokrycie Core: 80% linii i 80% gałęzi.
- Reguły walidacji daty, klasyfikowania materiałów i sortowania wymagają testów wszystkich istotnych przypadków.

### Testy integracyjne

- Warstwa Google Drive jest testowana przez jej publiczne interfejsy z użyciem kontrolowanych danych testowych.
- Testy obejmują uwierzytelnianie, odświeżanie tokenów, wiele folderów, brak metadanych oraz błędy dostępu i sieci.
- Testy nie mogą korzystać z danych rzeczywistych użytkowników.

### Pozostałe warstwy

- Dla warstw prezentacji zalecane są testy logiki widoku, nawigacji, licznika i trybu pełnoekranowego.
- Potok CI odrzuca zmianę, gdy testy nie przechodzą lub Core nie spełnia wymaganego pokrycia.
