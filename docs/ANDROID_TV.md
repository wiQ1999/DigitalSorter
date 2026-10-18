# Android TV — instalacja z laptopa i publikacja w Google Play

Backend może działać na hostingu albo lokalnie na laptopie. TV i telefon muszą mieć dostęp do tego samego adresu HTTPS z zaufanym certyfikatem. [Certyfikat i lokalny backend dla TV](DEPLOYMENT.md#2-certyfikat-do-lokalnego-backendu-dla-tv-i-telefonu) — domena, DNS-01, LAN i konfiguracja WSL.

Poniższe polecenia laptopa używają PowerShell na Windows. Android SDK Platform-Tools są dostępne także na Linux/macOS; polecenia `adb` pozostają takie same. Przykłady zakładają jedno urządzenie ADB; przy kilku dodawaj `-s NUMER_SERYJNY` po `adb`.

## 1. Sprawdzenie urządzenia i połączenia USB

Wymagany jest Android TV/Google TV, nie dowolny system Smart TV. Projekt obsługuje Android 7.0/API 24+ oraz architektury ARM 32-bit (`armeabi-v7a`), ARM64 (`arm64-v8a`) i x64 (`x86_64`). Aplikacja wymaga działającego Android System WebView.

1. Zainstaluj na laptopie [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools), dodaj katalog `platform-tools` do `PATH` i sprawdź `adb version`.
2. Na TV otwórz Ustawienia → Informacje o urządzeniu. Kilkukrotnie wybierz numer kompilacji, aż pojawi się informacja o włączeniu opcji programisty. Nazwy ekranów zależą od producenta.
3. W Opcjach programisty włącz debugowanie USB, jeżeli urządzenie je udostępnia.
4. Połącz laptop przewodem danych z portem TV przewidzianym przez producenta do połączenia ADB z komputerem. Zatwierdź na TV klucz RSA laptopa.
5. W Windows zainstaluj sterownik USB/ADB producenta, jeżeli urządzenie nie jest wykrywane.

```powershell
adb devices -l
adb shell getprop ro.product.cpu.abilist
adb shell getprop ro.build.version.sdk
```

Oczekiwany stan ADB to `device`. `unauthorized` oznacza brak zatwierdzenia klucza na TV. Wspierane ABI to `armeabi-v7a`, `arm64-v8a` i `x86_64`. Philips 55POS9002/12 / TPM171E z Androidem 8 wymaga ARM32 mimo 64-bitowych procesorów spotykanych w nowszych TV.

Wiele telewizorów ma wyłącznie porty USB host do pamięci i akcesoriów, bez trybu ADB po kablu. Sam kabel ani przejściówka nie dodają tej funkcji; nie łącz dwóch portów host zwykłym przewodem USB-A–USB-A. Jeśli producent nie wspiera ADB po USB, potrzebne jest obsługiwane przez niego debugowanie sieciowe lub instalacja z Google Play. [Dokumentacja ADB](https://developer.android.com/tools/adb) opisuje także parowanie bezprzewodowe.

## 2. Uzyskanie APK do instalacji testowej

### Z GitHub Actions

1. W repozytorium otwórz Actions → `Validate` i wybierz udany przebieg dla właściwego commita.
2. Pobierz artefakt `android-tv-test-apk` i rozpakuj ZIP.
3. Użyj znajdującego się w nim `pl.wiq1999.digitalsorter.androidtv-Signed.apk` z bibliotekami ARM32, ARM64 i x64.

Jest to pakiet do testów, podpisany kluczem środowiska budowania. Nie jest pakietem wydawcy dla Google Play; podpis może zmienić się między przebiegami CI. Artefakt `native-tv-fixture` zawiera specjalny pakiet emulatora x64 z testowym CA — nie używaj go do zwykłej instalacji na TV.

### Własny build na laptopie

Zainstaluj SDK .NET z `global.json`, JDK 21 i Android SDK: platformę API 36, Build-Tools 36.0.0 oraz Platform-Tools. Ustaw `JAVA_HOME` i `ANDROID_HOME` na rzeczywiste katalogi instalacji. Android SDK można przygotować przez SDK Manager w Android Studio.

Z katalogu repozytorium:

```powershell
dotnet workload install maui-android --skip-manifest-update
dotnet build src/DigitalSorter.AndroidTV -c Debug -p:RuntimeIdentifier=android-arm64 -p:RestoreLockedMode=true
```

Jeżeli SDK nie są wykrywane, dodaj argumenty `-p:AndroidSdkDirectory="$env:ANDROID_HOME"` i `-p:JavaSdkDirectory="$env:JAVA_HOME"`.

Wynik: `src/DigitalSorter.AndroidTV/bin/Debug/net10.0-android/android-arm64/pl.wiq1999.digitalsorter.androidtv-Signed.apk`. Projekt deklaruje trzy RID, więc APK zawiera wszystkie trzy ABI; katalog wyniku zależy od wybranego `RuntimeIdentifier`. Sprawdź pakiet przez `scripts/check-tv-apk.ps1 -Path <plik.apk>`. Nie nadpisuj `RuntimeIdentifiers` pojedynczym RID, jeśli potrzebujesz uniwersalnego pakietu. Native TV ładuje Web z serwera; nie kopiuje się frontendu osobno na telewizor.

## 3. Instalacja APK przez USB

Po pobraniu lub zbudowaniu APK ustaw jego rzeczywistą ścieżkę:

```powershell
$apk = "C:\sciezka\pl.wiq1999.digitalsorter.androidtv-Signed.apk"
adb install --no-streaming -r "$apk"
adb shell pm path pl.wiq1999.digitalsorter.androidtv
adb shell monkey -p pl.wiq1999.digitalsorter.androidtv -c android.intent.category.LEANBACK_LAUNCHER 1
```

`Success` potwierdza instalację. Aplikację można też uruchomić z listy aplikacji TV. `-r` zachowuje dane przy zgodnym podpisie i pakiecie.

Jeżeli pojawia się `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, podpis zainstalowanej i nowej wersji jest inny. Użyj pakietu podpisanego tym samym kluczem albo odinstaluj starą wersję przez ustawienia TV i zainstaluj nową; odinstalowanie usuwa adres serwera i sesję, więc wymaga ponownego parowania. `INSTALL_FAILED_NO_MATCHING_ABIS` oznacza niewłaściwą architekturę. Nie obniżaj numeru wersji podczas aktualizacji.

## 4. Pierwsze uruchomienie i parowanie

1. Uruchom backend. Wpisz na TV jego origin, np. `https://galeria.example.com` albo lokalny `https://tv-test.twojadomena.pl:7043`, bez dodatkowej ścieżki. `localhost` na TV oznacza sam telewizor.
2. Wybierz „Otwórz DigitalSorter”, następnie „Połącz przez telefon”.
3. Zeskanuj QR telefonem lub otwórz wskazany adres i wpisz kod.
4. Zaloguj się do Google w przeglądarce telefonu i przyznaj odczyt Drive. W lokalnym demo zamiast tego wybierz „Zatwierdź dostęp testowy”; konto Google nie jest potrzebne.
5. Sprawdź kod oraz nazwę urządzenia i zatwierdź powiązanie. Jeżeli kod wygasł, rozpocznij parowanie ponownie na TV.
6. Na TV wybierz foldery i otwórz kolekcję.
7. Sprawdź strzałki, OK, Play/Pause oraz Wstecz z pełnego ekranu. Zamknij i uruchom aplikację ponownie; sesja powinna pozostać aktywna. W demo pozostaw backend uruchomiony — jego zatrzymanie usuwa tymczasowe dane parowania.

Logowanie Google nie odbywa się w WebView telewizora. Backend zachowuje zaszyfrowany Google `refresh_token`, a TV własną sesję w `SecureStorage`. Wstecz poza odtwarzaczem otwiera menu umożliwiające zmianę serwera. Wylogowanie TV usuwa jego sesję; odłączenie Google unieważnia dostęp wszystkich powiązanych klientów.

Widok TV mieści parowanie, materiał, nawigację i pasek miniatur w całym obszarze WebView (także 960×540 CSS px przy obrazie 1920×1080). Po otwarciu kolekcji przycisk **Źródła** przełącza panel folderów. Długie listy przewijają się wewnątrz panelu, bez przewijania strony. TV używa podglądów Drive do 1280 px, bez rozmyć i ciągłego próbkowania filmu; brak miniatury wideo/HEIF pokazuje zastępczą ikonę zamiast uruchamiać dodatkowy dekoder. Oryginał pozostaje źródłem filmu i awaryjnego wyświetlania zdjęcia.

Opis i przyciski są przezroczystymi nakładkami, więc pełny ekran udostępnia materiałowi całą wysokość. Okno **Odłącz konto** przejmuje fokus na **Anuluj**; strzałki nie przechodzą do tła, a Wstecz anuluje okno i przywraca fokus. Wersja 0.8.1 aktualizuje także ikonę, ekran startowy i kafelek launchera do kolorowego znaku z nagłówka.

Do testów przez Wi-Fi na tym Philipsie działa autoryzowane `adb connect ADRES_TV:5555`. Używaj `--no-streaming`, jeżeli instalacja strumieniowa zatrzymuje się pod koniec transferu. Nie każdy TV udostępnia ADB przez LAN; po testach wyłącz debugowanie.

Po instalacji możesz odłączyć USB i wyłączyć debugowanie; działanie aplikacji go nie wymaga.

## 5. Rejestracja wydawcy i aplikacji w Google Play

To proces odrębny od konfiguracji Google OAuth w Google Cloud. Do publikacji użyj [Google Play Console](https://play.google.com/console/), nie formularza aplikacji OAuth.

1. Zarejestruj konto dewelopera jako osoba fizyczna albo organizacja, zgodnie z faktycznym statusem wydawcy. Zaakceptuj Developer Distribution Agreement i opłać rejestrację; aktualnie jest to jednorazowo 25 USD.
2. Zakończ weryfikację tożsamości, kontaktów oraz urządzenia, jeżeli wymaga jej konsola. Organizacje mogą potrzebować numeru D-U-N-S.
3. Wybierz „Create app”: nazwa DigitalSorter, język domyślny, typ Application i bezpłatna dystrybucja. Uzupełnij wymagane deklaracje.
4. Pierwszy przesłany pakiet ustali identyfikator `pl.wiq1999.digitalsorter.androidtv`. Jeśli potrzebujesz innego, zmień `ApplicationId` przed pierwszym uploadem; później nie można zmienić identyfikatora istniejącej aplikacji.
5. W ustawieniach dystrybucji/form factors dodaj **Android TV** i przejdź wymagany proces oceny jakości dla TV.

Publikację wykonuje właściciel praw lub uprawniony wydawca. [Licencja repozytorium](../LICENSE.md) określa prywatny, niekomercyjny użytek odbiorców i sama nie daje innym prawa do publikowania aplikacji w sklepie.

## 6. Klucz wydawcy i podpisany AAB

Google Play przyjmuje Android App Bundle (`.aab`). APK z CI nie zastępuje podpisanego AAB. Włącz **Play App Signing**: Google przechowuje klucz podpisujący aplikację, a wydawca używa osobnego upload key do przesyłania wydań.

Na laptopie utwórz katalog poza repozytorium i klucz przesyłania:

```powershell
$signingDir = Join-Path $env:USERPROFILE ".digitalsorter-signing"
New-Item -ItemType Directory -Force $signingDir | Out-Null
& "$env:JAVA_HOME\bin\keytool.exe" -genkeypair -v -keystore "$signingDir\upload.jks" -alias digitalsorter-upload -keyalg RSA -keysize 3072 -validity 10000
```

Ustaw to samo mocne hasło magazynu i klucza. Zapisz je jako pojedynczy wiersz w pliku `$signingDir\password.txt`, dostępnym wyłącznie dla swojego konta. Nie dodawaj magazynu ani hasła do Git. Zachowaj ich zaszyfrowaną kopię zapasową.

Zbuduj wydanie z katalogu repozytorium:

```powershell
dotnet publish src/DigitalSorter.AndroidTV -f net10.0-android -c Release `
  -p:UseDefaultPublishRuntimeIdentifier=false `
  -p:AndroidPackageFormats=aab `
  -p:AndroidKeyStore=true `
  "-p:AndroidSigningKeyStore=$signingDir\upload.jks" `
  -p:AndroidSigningKeyAlias=digitalsorter-upload `
  "-p:AndroidSigningStorePass=file:$signingDir\password.txt" `
  "-p:AndroidSigningKeyPass=file:$signingDir\password.txt" `
  -p:RestoreLockedMode=true
```

Projekt deklaruje ARM32, ARM64 oraz x64. `UseDefaultPublishRuntimeIdentifier=false` zapobiega dodaniu platformy laptopa do listy Android RID i błędowi locked restore. Wynik znajdziesz pod `src/DigitalSorter.AndroidTV/bin/Release/net10.0-android/`, w podkatalogu `publish/`; wybierz plik `*-Signed.aab`, a nie niepodpisany `.aab`. Parametr `file:` jest obsługiwany dla AAB; nie zamieniaj go na `env:`, które ma ograniczenia w tym formacie.

Przed każdym kolejnym wydaniem zwiększ `ApplicationVersion` (liczbowy versionCode) i ustaw `ApplicationDisplayVersion` w `.csproj`. Nie dodawaj `DigitalSorterTestCertificate` do publikacji; projekt odrzuca go w Release. Instalacja z Google Play jest podpisana kluczem App Signing i może wymagać usunięcia wcześniejszej wersji instalowanej przez USB z innym podpisem.

## 7. Materiały i deklaracje przed wysyłką

W Play Console przygotuj:

- Nazwę, krótki i pełny opis, kategorię, adres kontaktowy, ikonę 512×512, grafikę promocyjną 1024×500, banner sklepu Android TV 1280×720 i zrzuty ekranu z rzeczywistego TV/emulatora zgodne z aktualnymi wymaganiami konsoli.
- Banner launchera TV z nazwą aplikacji, zgodny z wymaganiem 320×180 px. Projekt generuje go z `Resources/Images/tv_banner.svg`; przed publikacją sprawdź wynik na docelowych launcherach. Nie zastępuje on osobnej grafiki promocyjnej sklepu.
- Publiczną politykę prywatności pod HTTPS, dostępną także z aplikacji: operator, przetwarzane dane i poświadczenia, cel, retencja, kopie zapasowe, usunięcie danych i kontakt. Tej strony obecnie nie ma w repozytorium; przygotuj ją dla rzeczywistego hostingu.
- Data safety zgodne z rzeczywistym działaniem brokera i WebView. Nie deklaruj braku zbierania danych: backend przechowuje identyfikator konta, poświadczenia i sesje. Pliki Drive są pobierane bezpośrednio przez klienta.
- Deklaracje reklam, grupy odbiorców, ocenę treści oraz App access. Dla recenzenta opisz działający adres HTTPS, konfigurację serwera na TV, logowanie na telefonie i parowanie; zapewnij możliwość przejścia pełnego scenariusza z syntetycznymi mediami. Nie przekazuj Google Client Secret ani tokenów.
- Dla kont brokera tworzonych przez logowanie Google zapewnij usuwanie danych w aplikacji i publiczny adres żądania usunięcia, zgodnie z polityką usuwania kont. „Odłącz Google” usuwa konto brokera i sesje; stronę zewnętrznego żądania usunięcia oraz zasady retencji kopii zapasowych trzeba przygotować przed publikacją.
- Dostęp do informacji o licencjach zależności pod `/licenses/`, również dla użytkowników TV.

Sprawdź checklistę TV: nawigacja bez dotyku, widoczny fokus, poprawne Wstecz, czytelność z kanapy, brak uciętych elementów, obsługa utraty sieci i wymagania odtwarzania/trybu Ambient. Sprawdź aktualne wymagania target API i natywnych bibliotek, w tym stron pamięci 16 KB, na przesyłanym AAB. Sam udany build nie zastępuje tej kontroli ani akceptacji Google.

## 8. Testowanie i publikacja

1. Utwórz wydanie na ścieżce **Internal testing**, prześlij podpisany AAB, dodaj testerów i udostępnij link do dołączenia.
2. Na urządzeniach zgodnych z katalogiem Google Play sprawdź instalację ze sklepu, aktualizację, parowanie, restart, formaty mediów, pilot i wylogowanie. Zweryfikuj raporty AAB oraz listę wspieranych urządzeń.
3. Dla osobistych kont dewelopera utworzonych po 13.11.2023 przeprowadź wymagany test zamknięty: obecnie co najmniej 12 testerów zapisanych nieprzerwanie przez 14 dni. Test wewnętrzny nie zastępuje tego wymogu. Następnie złóż wniosek o dostęp do produkcji i opisz wyniki testowania.
4. Uzupełnij wszystkie zadania Play Console, w tym osobną ocenę jakości Android TV i wymagane deklaracje. Zakończ niezależną weryfikację Google OAuth dla publicznego dostępu do Drive.
5. Wyślij wydanie do oceny, odpowiedz na uwagi i uruchom publikację zgodnie z wybraną strategią wdrożenia.
6. Dla aktualizacji używaj tego samego identyfikatora i upload key, zwiększaj versionCode, ponawiaj testy i aktualizuj deklaracje po zmianie sposobu przetwarzania danych.

Stan procedury: 16.10.2026. Nazwy ekranów i wymagania mogą zmieniać się; przy wysyłce rozstrzygające są zadania przypisane w Play Console.

## Źródła

- [Konto dewelopera i opłata](https://support.google.com/googleplay/android-developer/answer/6112435?hl=en)
- [Testy nowych kont osobistych](https://support.google.com/googleplay/android-developer/answer/14151465?hl=en)
- [Publikacja Android TV](https://developer.android.com/training/tv/publishing/distribute) i [checklista jakości TV](https://developer.android.com/training/tv/publishing/checklist)
- [Wymagania grafik sklepu](https://support.google.com/googleplay/android-developer/answer/1078870?hl=en)
- [Podpisywanie i Play App Signing](https://developer.android.com/studio/publish/app-signing)
- [Publikacja .NET MAUI z CLI](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0)
- [Data safety](https://support.google.com/googleplay/android-developer/answer/10787469?hl=en), [usuwanie kont](https://support.google.com/googleplay/android-developer/answer/13327111?hl=en) i [target API](https://support.google.com/googleplay/android-developer/answer/11926878?hl=en)
