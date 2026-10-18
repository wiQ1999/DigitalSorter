# DigitalSorter

Galeria zdjęć i filmów z Google Drive, sortowana według daty wykonania. Web i Android TV korzystają z tego samego interfejsu; multimedia są pobierane bezpośrednio z Drive, bez modyfikowania materiałów źródłowych.

Obsługiwane zdjęcia: JPEG, PNG, WebP i HEIC/HEIF. Wideo: MP4, WebM i MOV; odtwarzanie zależy również od kodeków urządzenia. Materiały bez poprawnej daty wykonania trafiają do osobnej listy. Daty Drive `createdTime`/`modifiedTime` nie zastępują daty wykonania; daty bez strefy są interpretowane jako UTC.

## Pierwsze uruchomienie

Na przygotowanym laptopie Windows skorzystaj z [lokalnej instrukcji startu](docs/DEPLOYMENT-LOCAL.md). Nowe środowisko przygotuj według [instrukcji wdrożenia](docs/DEPLOYMENT.md).

Wymagane: SDK wskazane w `global.json`, Node.js 22+ i HTTPS. Po przygotowaniu certyfikatu oraz konfiguracji Google według [procedury lokalnego backendu](docs/DEPLOYMENT.md#4-lokalny-backend-z-rzeczywistym-google-drive--opcjonalnie), uruchom z katalogu repozytorium:

```bash
npm ci --ignore-scripts
npm run assets
dotnet restore DigitalSorter.slnx --locked-mode
dotnet run --project src/DigitalSorter.Auth --launch-profile https
```

Otwórz `https://localhost:7043`, połącz Google Drive, wybierz foldery i otwórz kolekcję. Bez konta Google możesz uruchomić [demo z syntetycznymi danymi](docs/DEPLOYMENT.md#3-lokalne-demo-bez-konta-google).

## Dokumentacja

Pełna [mapa dokumentacji](docs/README.md) wskazuje przeznaczenie poszczególnych plików.

- [Wymagania i funkcje produktu](docs/PRODUCT_REQUIREMENTS.md) oraz [integracja z Google Drive](docs/GOOGLE_DRIVE_INTEGRATION.md).
- [Architektura i magazyn danych brokera](docs/ARCHITECTURE.md).
- [Konfiguracja, certyfikaty, wdrożenie i backup](docs/DEPLOYMENT.md).
- [Instalacja Android TV i publikacja w Google Play](docs/ANDROID_TV.md).
- [Uruchamianie testów i standard weryfikacji](docs/TESTING.md).
- [Backlog](docs/BACKLOG.md) i [raport weryfikacji z 18.10.2026](docs/VALIDATION-2026-10-18.md).

## Licencja

Własny kod i materiały repozytorium obejmuje [DigitalSorter Personal Use License 1.0](LICENSE.md) — licencja własna, wyłącznie do użytku prywatnego i niekomercyjnego osób fizycznych oraz ich gospodarstw domowych. Użycie komercyjne, zawodowe i organizacyjne wymaga osobnej zgody właścicieli praw.

Zależności zachowują własne licencje. Dekoder HEIC/HEIF `heic-to` korzysta z LGPL 3.0, której częścią są postanowienia GPL 3.0. [Informacje, licencje i źródła dekodera](src/DigitalSorter.Web/wwwroot/licenses/THIRD_PARTY_NOTICES.txt) pozostają w Web i są publikowane pod `/licenses/` dla Web oraz Android TV.
