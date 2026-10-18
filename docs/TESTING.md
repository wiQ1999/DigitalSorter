# Testowanie DigitalSorter

Dokument opisuje standard weryfikacji i uruchamianie testów przez programistów. Wyniki konkretnego przebiegu znajdują się w datowanych raportach, np. [weryfikacji z 18.10.2026](VALIDATION-2026-10-18.md).

## Przygotowanie środowiska

Polecenia wykonuj z katalogu repozytorium. Potrzebne są SDK wskazane w `global.json`, Node.js 22+ i Google Chrome z kodekiem H.264. Skrypty Bash wymagają Linux/macOS lub WSL oraz OpenSSL i curl. Na Windows testy przeglądarkowe mają osobny skrypt PowerShell.

```bash
npm ci --ignore-scripts
npx playwright install --with-deps chrome
```

`TEST_CHROMIUM` może wskazywać lokalny plik przeglądarki z obsługą H.264. Testy korzystają z HTTPS, własnych certyfikatów, kontrolowanego OAuth/Drive i syntetycznych plików. Nie wymagają konta Google ani certyfikatów z instrukcji wdrożenia. Nie używaj danych rzeczywistych użytkowników ani rzeczywistych sekretów z kontrolowanym hostem testowym. Host `DigitalSorter.BrowserHost` i środowisko `Testing` służą wyłącznie testom; host nie jest częścią obrazu produkcyjnego.

## Pełna weryfikacja Web i backendu

```bash
bash scripts/verify.sh
```

Skrypt instaluje zależności npm, buduje zasoby, wykonuje locked restore, buduje rozwiązanie Release z `-warnaserror`, uruchamia testy Core z progiem pokrycia, integrację i testy przeglądarkowe. Sam nie buduje ani nie uruchamia aplikacji Android TV. Domyślny port hosta przeglądarkowego to 7443; musi być wolny. Inny port ustaw przez `TEST_PORT`, np. `TEST_PORT=7543 bash scripts/verify.sh`.

## Testy .NET

Core obejmuje walidację dat, klasyfikowanie materiałów i sortowanie; testy tych reguł powinny uwzględniać wszystkie istotne przypadki. Testy jednostkowe stosują Arrange–Act–Assert, są deterministyczne i niezależne od sieci, systemu plików oraz usług zewnętrznych. Wymagane pokrycie Core to co najmniej 80% linii i 80% gałęzi; próg egzekwuje `scripts/verify.sh`.

Testy integracyjne sprawdzają publiczny adapter Drive, metadane rzeczywistych formatów plików, wiele folderów, brak metadanych oraz błędy dostępu i sieci. Dla brokera obejmują OAuth z podpisami RSA i PKCE, odświeżanie tokenów po restarcie, SQLite i zastępowalny magazyn, szyfrowanie, ważność i jednokrotność parowania, przypisanie sesji do TV, wylogowanie i odłączenie Google. Inny silnik magazynu wymaga dodatkowej weryfikacji na tym silniku; zakres obecnych testów opisuje [architektura magazynu](ARCHITECTURE.md#magazyn-danych-brokera).

Wybrane zestawy możesz uruchomić osobno:

```bash
dotnet test tests/DigitalSorter.Core.Tests -c Release -p:RestoreLockedMode=true
dotnet test tests/DigitalSorter.IntegrationTests -c Release -p:RestoreLockedMode=true
```

Te dwa polecenia nie uruchamiają kontroli progu pokrycia z pełnego skryptu.

## Testy przeglądarkowe

Testy uruchamiają opublikowany WebAssembly oraz service worker z kontrolowanym brokerem. Obejmują Web i adapter TV, logowanie/parowanie, postęp i anulowanie odczytu, błędy, odtwarzanie, HEIC/HEIF, miniatury, zmianę szerokości, nawigację, pełny ekran, dynamiczne tło i fokus okna potwierdzenia. Każdy symulowany klient ma osobny adres z puli dokumentacyjnej przekazywany przez zaufany lokalny proxy testowy, aby scenariusze nie współdzieliły limitu prób parowania na loopback. Limity produkcyjne pozostają aktywne i mają testy integracyjne.

Przy zmianach warstw prezentacji weryfikuj logikę widoku, nawigację, licznik materiałów i tryb pełnoekranowy.

Linux/macOS lub WSL:

```bash
bash scripts/test-browser.sh
```

Windows, z PowerShell:

```powershell
pwsh -File scripts/test-browser.ps1
```

Wariant Windows domyślnie używa portu 7543. Opcje `-Port 7643` i `-NamePattern 'fragment nazwy'` pozwalają zmienić port i ograniczyć zestaw. Skrypty budują host i publikują aplikację, uruchamiają serwer oraz zatrzymują go po teście. Samo `npm run test:e2e` uruchamia scenariusze bez przygotowania hosta; do zwykłej weryfikacji używaj skryptów.

## Testy natywnego Android TV

Natywny scenariusz korzysta z rzeczywistego APK/WebView, systemowego UI Automator i Playwright. Sprawdza start, parowanie, `SecureStorage` po restarcie, formaty mediów, pilot i wylogowanie. Standardowa ścieżka CI uruchamia emulator Android TV z KVM.

Do lokalnego uruchomienia potrzebne są ADB z jednym wybranym urządzeniem/emulatorem, zależności npm i Chrome oraz rozpakowany do `test-results/native-fixture/` artefakt CI `native-tv-fixture`. Zawiera APK x64 i odpowiadający mu tymczasowy CA. Ten pakiet jest przeznaczony dla emulatora x64; instalację na fizycznym TV i jego ABI opisuje [instrukcja Android TV](ANDROID_TV.md).

```bash
bash scripts/test-native-tv.sh
```

Skrypt uruchamia host na porcie 7443 i instaluje APK. Scenariusz domyślnie używa adresu hosta emulatora `https://10.0.2.2:7443`; własną konfigurację urządzenia ustawiaj zgodnie z adresem i certyfikatem środowiska testowego. `TEST_KEEP_TV_DATA=1` zachowuje początkową konfigurację adresu zamiast czyszczenia danych aplikacji, ale test sprawdza wylogowanie i po zakończeniu wymaga ponownego parowania.

Tymczasowy publiczny CA dodawany przez `DigitalSorterTestCertificate` jest dozwolony tylko w Debug; projekt odrzuca go w Release. Zwykłe APK nie zawierają testowego CA. Zgodność trzech ABI w uniwersalnym APK sprawdza `scripts/check-tv-apk.ps1 -Path <plik.apk>`; obecność bibliotek nie zastępuje testów urządzenia.

## Wyniki, dane testowe i CI

Wyniki, pokrycie, logi i zrzuty trafiają do ignorowanego `test-results/`. Lokalne pomiary mogą również pozostawać w ignorowanym `artifacts/local/`; prywatne identyfikatory plików, poświadczenia i klucze nie należą do raportów w repozytorium. Syntetyczne media odtwarza `tests/fixtures/generate.py` z Pillow 11.3.0, pillow-heif 1.1.0 i ffmpeg.

Workflow `Validate` wykonuje pełną weryfikację Web/backendu, osobno buduje APK, sprawdza ABI i wykonuje natywny scenariusz emulatora. Odrzuca zmianę przy błędzie testów lub niespełnionym progu pokrycia Core. Publikuje artefakty `web-and-backend`, `test-results`, `android-tv-test-apk`, `native-tv-fixture` i `native-tv-results`; dokładny przebieg określa [konfiguracja CI](../.github/workflows/ci.yml).

W raporcie weryfikacji podawaj datę, środowisko, wykonane zestawy, wyniki i ograniczenia. Rozróżniaj testy syntetyczne od ręcznych sprawdzeń prawdziwego Drive oraz emulator od fizycznego TV. Procedura ręcznego demo znajduje się we [wdrożeniu](DEPLOYMENT.md#3-lokalne-demo-bez-konta-google); nie jest częścią automatycznych wyników.
