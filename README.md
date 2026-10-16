# DigitalSorter

Galeria zdjęć i filmów z Google Drive, sortowana według daty wykonania. Web i Android TV korzystają z tego samego interfejsu; multimedia są pobierane bezpośrednio z Drive.

## Uruchomienie

Instrukcje krok po kroku:

- [Lokalne testy i wdrożenie produkcyjne przez HTTPS, certyfikaty](docs/DEPLOYMENT.md).
- [Instalacja Android TV przez USB i publikacja w Google Play](docs/ANDROID_TV.md).

Wymagane: SDK z `global.json`, Node.js 22+, HTTPS. Dla TV: workload `maui-android`, JDK 21, Android SDK 36.

Lokalne demo bez konta Google: przygotuj certyfikat według [instrukcji](docs/DEPLOYMENT.md), wykonaj `npm ci --ignore-scripts` i `bash scripts/run-local-demo.sh`. Ten sam backend może obsługiwać TV i telefon w LAN przez HTTPS.

Uruchomienie backendu z rzeczywistym Google przez HTTPS:

```bash
npm ci --ignore-scripts
npm run assets
dotnet restore DigitalSorter.slnx --locked-mode
dotnet run --project src/DigitalSorter.Auth --launch-profile https
```

Konfiguracja przez zmienne środowiskowe lub magazyn sekretów:

| Zmienna | Wartość |
|---|---|
| `Google__ClientId` | Identyfikator klienta OAuth typu Web application |
| `Google__ClientSecret` | Sekret klienta Google |
| `Broker__PublicOrigin` | Publiczny origin HTTPS, bez ścieżki |
| `Broker__TokenEncryptionKey` | Stały klucz AES, 32 bajty w Base64; wymagany w produkcji |
| `Broker__DataDirectory` | Trwały katalog SQLite i zaszyfrowanych kluczy sesji |
| `AllowedHosts` | Dozwolona nazwa hosta |
| `Broker__KnownProxies__0` | Adres zaufanego reverse proxy, jeśli jest używane |

W Google Cloud włącz Drive API, zakresy `openid` i `drive.readonly` oraz callback `<PublicOrigin>/auth/callback`. Status Testing Google ogranicza trwałość tokenów Drive do siedmiu dni. Publiczna aplikacja wymaga weryfikacji zakresu restricted.

W Development aplikacja tworzy lokalny klucz szyfrowania. W produkcji podaj klucz z magazynu sekretów i zachowuj go wraz z backupem danych. Utrata klucza uniemożliwia odszyfrowanie poświadczeń. Backend używa SQLite i jest wdrażany jako jedna instancja.

## Android TV

```bash
dotnet workload install maui-android --skip-manifest-update
dotnet build src/DigitalSorter.AndroidTV -c Debug -p:RuntimeIdentifier=android-arm64
```

Zainstaluj `*Signed.apk`, wpisz adres HTTPS serwera i wybierz „Połącz przez telefon”. Telefon zatwierdza kod/QR. Sekret sesji TV pozostaje w natywnym `SecureStorage`; Google `refresh_token` pozostaje na backendzie. Pilot: strzałki, OK, Play/Pause i Wstecz.

## Testy

```bash
npm ci --ignore-scripts
npx playwright install --with-deps chrome
scripts/verify.sh
```

- Jednostkowe: deterministyczne reguły Core; wymagane ≥80% linii i gałęzi.
- Integracyjne: SQLite, trwałe sesje, szyfrowanie, OIDC z podpisami RSA i PKCE, tokeny, parowanie, publiczny adapter Drive i prawdziwe kontenery multimediów.
- Przeglądarkowe: opublikowany WebAssembly, kontrolowany OAuth/Drive, Web i kontrakt adaptera TV, odtwarzanie, szukanie w filmie, HEIC/HEIF, zastępcze miniatury, nawigacja, pełny ekran, dynamiczne tło i układ mobilny.
- Natywne TV: osobny job CI uruchamia APK na emulatorze Android TV z KVM — start, parowanie, rzeczywisty `SecureStorage`, restart, formaty mediów i pilot. Wyniki zawierają zrzuty ekranu i logcat.
- Dane testowe są syntetyczne; testy nie wymagają konta Google. `Testing` i host kontrolowanego OAuth służą wyłącznie testom i nie są częścią obrazu produkcyjnego.

Domyślnie testy używają Google Chrome z kodekiem H.264. `TEST_CHROMIUM` wskazuje opcjonalny lokalny plik przeglądarki z tym kodekiem. Wyniki, pokrycie i zrzuty trafiają do `test-results/`. Regeneracja mediów: `tests/fixtures/generate.py` z Pillow 11.3.0, pillow-heif 1.1.0 i ffmpeg.

Test natywny: `scripts/test-native-tv.sh`, uruchomiony emulator z ADB oraz artefakt `native-tv-fixture` z CI. Konfigurację natywną obsługuje systemowy UI Automator, a WebView — Playwright. Tymczasowy CA jest dodawany przez `DigitalSorterTestCertificate` tylko do Debug; Release odrzuca ten parametr. Domyślne APK nie zawierają testowego CA.

## Wdrożenie

Wymagania hostingu: [architektura](docs/ARCHITECTURE.md#hosting-i-wdrożenie).

`compose.yaml` uruchamia wspólną aplikację i Caddy z HTTPS. Na serwerze ustaw w lokalnym `.env`: `APP_DOMAIN`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `TOKEN_ENCRYPTION_KEY`. Domenę skieruj na serwer; udostępnij porty 80/443. Sekretów nie zapisuj w repozytorium.

```bash
docker compose up -d --build
```

Workflow `Validate` buduje aplikacje, wykonuje piramidę testów i publikuje artefakty. Ręczny workflow `Deploy` najpierw wykonuje walidację, następnie wdraża wspólny obraz na skonfigurowany serwer Docker. Środowisko GitHub `production`: zmienne `DEPLOY_HOST`, `DEPLOY_USER`; sekrety `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS`. Użytkownik SSH musi mieć dostęp do Dockera i `/opt/digitalsorter`; `.env` przygotowuje się tam przed pierwszym wdrożeniem.

Materiały bez poprawnej daty trafiają do osobnej listy. Daty Drive `createdTime`/`modifiedTime` nie zastępują daty wykonania. Daty bez strefy są interpretowane jako UTC. Brak obsługi konkretnego kodeka lub uszkodzony plik daje komunikat w odtwarzaczu.

## Licencja

Własny kod i materiały repozytorium obejmuje [DigitalSorter Personal Use License 1.0](LICENSE.md) — licencja własna, wyłącznie do użytku prywatnego i niekomercyjnego osób fizycznych oraz ich gospodarstw domowych. Użycie komercyjne, zawodowe i organizacyjne wymaga osobnej zgody właścicieli praw.

Zależności zachowują własne licencje. Dekoder HEIC/HEIF `heic-to` korzysta z LGPL 3.0, której częścią są postanowienia GPL 3.0. [Informacje, licencje i źródła dekodera](src/DigitalSorter.Web/wwwroot/licenses/THIRD_PARTY_NOTICES.txt) pozostają w Web i są publikowane pod `/licenses/` dla Web oraz Android TV.
