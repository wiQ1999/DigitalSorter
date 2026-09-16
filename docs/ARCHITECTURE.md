# Architektura — DigitalSorter

## Warstwy rozwiązania

- `DigitalSorter.Core` — logika aplikacji, walidacja metadanych, klasyfikowanie materiałów i sortowanie.
- `DigitalSorter.Data.GoogleDrive` — uwierzytelnianie oraz odczyt plików i metadanych z Google Drive.
- `DigitalSorter.AndroidTV` — warstwa prezentacji Android TV oparta na .NET MAUI.
- `DigitalSorter.Web` — warstwa prezentacji dla przeglądarki oparta na standalone Blazor WebAssembly.

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

## Aplikacja webowa

- `DigitalSorter.Web` jest aplikacją standalone Blazor WebAssembly wykonywaną po stronie przeglądarki, bez backendu aplikacyjnego.
- Opublikowane pliki statyczne są hostowane przez GitHub Pages projektu.
- Wywołania Google Drive są wykonywane bezpośrednio z przeglądarki; GitHub Pages służy wyłącznie do dystrybucji plików aplikacji.
- Budowanie i publikowanie aplikacji realizuje GitHub Actions z użyciem `dotnet publish`.

### GitHub Pages — wymagania techniczne

- Aplikacja jest publikowana jako Project Pages pod ścieżką `/DigitalSorter/`, dlatego `base href` musi wskazywać `/DigitalSorter/`; wartość jest ustawiana w procesie publikacji.
- GitHub Pages nie obsługuje reguły SPA fallback do `index.html`; bezpośrednie wejście lub odświeżenie trasy Blazor wymaga `404.html`, który przekierowuje do aplikacji i odtwarza pierwotną trasę.
- Publikacja zawiera plik `.nojekyll`, aby katalog Blazor `_framework` nie był pomijany przez przetwarzanie Jekyll.
- Pliki JavaScript publikacji muszą być chronione przed zmianą zakończeń linii przez Git, np. przez odpowiednią regułę w `.gitattributes`, aby nie naruszyć kontroli integralności zasobów Blazor.

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
