# Weryfikacja Web i Android TV — 18.10.2026

## Środowisko i zakres

Windows, .NET SDK 10.0.402, Chrome, rzeczywisty Google Drive oraz Philips 55POS9002/12 (TPM171E), Android 8/API 26, ARM32, WebView 138.0.7204.179. WebView ma obszar 960×540 CSS px przy obrazie TV 1920×1080. Testy automatyczne używają oddzielnego hosta i syntetycznych danych; sprawdzenia rzeczywistego Dysku wykonano osobno.

## Wyniki

| Sprawdzenie | Wynik |
|---|---|
| Locked restore i build rozwiązania Release z `-warnaserror` | Powodzenie |
| Core | 67/67; pokrycie 95,65% linii, 100% gałęzi |
| Integracja | 69/69 |
| Przeglądarka i service worker | 27/27 |
| Natywny scenariusz na fizycznym Philipsie | 1/1 |
| Android Debug i Release | Build bez ostrzeżeń i błędów; APK zawiera ARM32, ARM64 i x64 |

Razem 164 testy. Scenariusz natywny sprawdza start APK, QR, zatwierdzenie, SecureStorage po restarcie procesu, zdjęcia/HEIF, MP4/WebM/MOV, pilot, pełny ekran, Wstecz, fokus modalnego potwierdzenia i wylogowanie. Układ parowania sprawdzono również w przeglądarce przy 960×540, 1280×720 i 1920×1080. Nie uruchamiano lokalnie CI z emulatorem; test natywny wykonano na fizycznym TV. Obecność ABI w pakiecie nie zastępuje testów fizycznych urządzeń ARM64/x64.

Kolejne poprawki obejmują przezroczyste kontrolki nad całą powierzchnią zdjęcia/filmu, pełną wysokość odtwarzacza w pełnym ekranie oraz wspólny znak Web/ikony MAUI/ekranu startowego i nowy kafelek launchera w APK 0.8.1. Testy sprawdzają rozmiar powierzchni, odtwarzanie, początkowy fokus „Anuluj”, strzałki ograniczone do dialogu, Wstecz/Escape i przywrócenie fokusu. Parowanie oraz ponowną zgodę Google sprawdzono także przy celowo niedostępnym service workerze. Nie wykonywano zdalnego testu fizycznego telefonu użytkownika; aktualna konfiguracja LAN nadal celowo wymaga zatwierdzenia na laptopie.

Każdy symulowany klient przeglądarkowy otrzymuje osobny adres z puli dokumentacyjnej przez zaufany lokalny proxy testowy. Dzięki temu niezależne scenariusze nie zużywają wspólnego limitu prób parowania na adresie loopback. Limity produkcyjne pozostają aktywne i są sprawdzane testami integracyjnymi.

Manualnie na Web otwarto 1453 rzeczywiste materiały: 1443 z datą i 10 bez poprawnej daty. Sprawdzono postęp, przejścia między zdjęciami oraz podgląd 1920 px i pobranie oryginału w pełnym ekranie. Na rzeczywistym TV karta QR kończyła się na 465 px przy wysokości 540 px; wysokość dokumentu również wynosiła 540 px.

Zgłoszony `VID_20260816_110807.mp4` odtworzono w Chrome i na Philipsie: H.264/AAC, 1080×1920, 41,617 s. Na TV sprawdzono start oraz przewinięcie do 12 s. Google zwracał 206, ale CORS nie udostępniał `Content-Range`; jego odtworzenie z żądania, długości odpowiedzi i uwierzytelnionych metadanych rozmiaru umożliwiło odczyt końcowego atomu `moov`. Krótkotrwałe podglądy Google wymagają pobrania podpisanego URL bez nagłówka OAuth, który powodował błąd preflight.

## Pomiary na rzeczywistych zdjęciach

Porównanie tych samych trzech zdjęć i połączenia, bez trwałego zapisu mediów. Dla metadanych porównano implementację bazową z `master` z nowym odczytem nagłówków JPEG; wyniki dat były identyczne.

| Próbka | Rozmiar oryginału | Metadane wcześniej | Metadane obecnie | Transfer obecnie |
|---|---:|---:|---:|---:|
| 1 | 7 113 376 B | 6384 ms / 7 żądań | 618 ms / 1 żądanie | 65 536 B |
| 2 | 6 801 340 B | 5476 ms / 7 żądań | 630 ms / 1 żądanie | 65 536 B |
| 3 | 5 649 223 B | 4915 ms / 6 żądań | 663 ms / 1 żądanie | 65 536 B |

Pomiar wyświetlenia na TV obejmuje pobranie, dekodowanie i dwie klatki renderowania. Porównano oryginał 3072×4096 z podglądem 960×1280, odpowiadającym ekranowi tego telewizora.

| Próbka | Oryginał | Podgląd TV | Transfer podglądu |
|---|---:|---:|---:|
| 1 | 3564 ms | 1199 ms | 300 681 B |
| 2 | 3661 ms | 1083 ms | 268 679 B |
| 3 | 3495 ms | 1015 ms | 147 736 B |

To wyniki pojedynczych porównań w lokalnej sieci, bez gwarancji takiego czasu dla każdego pliku/połączenia. Przygotowanie następnego zdjęcia eliminuje powtórne pobranie gotowego podglądu; osobny test przeglądarkowy sprawdza ten warunek. TV pomija kosztowne rozmycia i próbkowanie klatek tła oraz awaryjne dekodowanie miniatur filmów/HEIF.

## Utrzymanie i odtworzenie

- `scripts/verify.sh`: build, Core, integracja i przeglądarka; na Windows sam zestaw przeglądarkowy uruchamia `scripts/test-browser.ps1`.
- `tests/native/tv.spec.mjs`: rzeczywisty APK/WebView. `TEST_KEEP_TV_DATA=1` zachowuje ustawienia adresu, ale scenariusz testuje wylogowanie; po teście wymaga ponownego parowania. Nie używać kontrolowanego hosta z rzeczywistymi sekretami.
- `scripts/check-tv-apk.ps1 -Path <apk>`: obecność trzech runtime ABI.
- Logi, zrzuty i pomiary pozostają w ignorowanych `test-results/` oraz `artifacts/local/`; identyfikatory prywatnych plików, poświadczenia i certyfikaty prywatne nie należą do repozytorium.
- Lokalny QR używa `Broker:PairingOrigin` z adresem LAN. Logowanie Google pozostaje na `Broker:PublicOrigin` laptopa, zgodnie z konfiguracją klienta OAuth; docelowe parowanie telefonem wymaga wspólnej domeny HTTPS. Szczegóły: [wdrożenie](DEPLOYMENT.md#qr-przez-lokalny-adres-ip).

Przy refaktoryzacji policzono złożoność funkcji JavaScript jako 1 + warunki, pętle, catch i operatory logiczne/warunkowe, bez zagnieżdżonych funkcji. `onKey`: 18 → 9, `mountMedia`: 14 → 7, `stream`: 16 → 9; nowe wydzielone funkcje mają maksymalnie 10. Publiczne API modułów pozostało bez zmiany dla istniejących wywołań.
