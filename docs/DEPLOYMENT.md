# Uruchomienie testowe i wdrożenie produkcyjne

Polecenia: Bash na Linux/macOS lub w WSL. Wykonuj je z katalogu repozytorium, poza poleceniami przygotowania serwera. Instalację TV opisuje [osobna instrukcja](ANDROID_TV.md).

## 1. Przygotowanie lokalnego środowiska

Zainstaluj Git, SDK .NET wskazane w `global.json` i Node.js 22+. Do automatycznych testów przeglądarkowych potrzebny jest Chrome; do testów HTTPS także OpenSSL.

```bash
git clone --branch feature/full-digital-sorter https://github.com/wiQ1999/DigitalSorter.git
cd DigitalSorter
dotnet --version
node --version
npm ci --ignore-scripts
```

Branch zawiera bieżącą implementację. Po scaleniu używaj wydanego tagu lub zatwierdzonego commita.

## 2. Lokalne demo przez HTTP, bez konta Google

```bash
bash scripts/run-local-demo.sh
```

1. Poczekaj na komunikat `Controlled OAuth/Drive test host ready`.
2. Otwórz `http://localhost:7080` na tym samym komputerze.
3. Wybierz „Połącz Google Drive”, następnie „Zatwierdź dostęp testowy”.
4. Wybierz folder „Podróże i wspomnienia” i „Otwórz kolekcję”.
5. Sprawdź zdjęcia, filmy, miniatury, zmianę materiału i pełny ekran.
6. Zakończ serwer przez `Ctrl+C`.

Demo używa syntetycznych plików oraz kontrolowanego OAuth/Drive. Nie wymaga klienta OAuth, sekretów Google, certyfikatu ani zmian w Google Cloud. Skrypt publikuje Web, uruchamia osobny host testowy i usuwa jego tymczasowe pliki oraz dane po zakończeniu. Następne uruchomienie zaczyna nową sesję. Inny port: `TEST_PORT=7081 bash scripts/run-local-demo.sh`.

HTTP działa wyłącznie na loopback w `Development`, z osobnymi cookies deweloperskimi. Używaj dokładnego adresu z komunikatu; nie zastępuj `localhost` adresem LAN ani `0.0.0.0`. Przeglądarka traktuje `http://localhost` jako zaufany kontekst dla service workera. Demo nie jest środowiskiem dla fizycznego TV ani telefonu.

## 3. Lokalnie przez HTTP z rzeczywistym Google Drive — opcjonalnie

Do wykonania tego wariantu potrzebna jest własna konfiguracja Google; demo z poprzedniego punktu jej nie używa.

### Google Cloud

1. Utwórz osobny projekt testowy w [Google Cloud Console](https://console.cloud.google.com/).
2. Włącz **Google Drive API**.
3. W **Google Auth Platform** uzupełnij Branding, Audience i dane kontaktowe. Dla kont prywatnych wybierz External, pozostaw Testing i dodaj własny adres do użytkowników testowych.
4. W Data Access dodaj `openid` i `https://www.googleapis.com/auth/drive.readonly`.
5. W Clients utwórz klienta **Web application**. Android TV korzysta ze wspólnego brokera, więc nie twórz dla niego osobnego klienta Android.
6. Dodaj Authorized redirect URI: `http://localhost:7080/auth/callback`. Port, schemat i ścieżka muszą zgadzać się dokładnie. Pole Authorized JavaScript origins nie jest wymagane przez przepływ backendu.
7. Zachowaj Client ID i Client Secret w lokalnym magazynie sekretów lub zmiennych procesu.

### Uruchomienie

Zatrzymaj demo, jeżeli zajmuje port 7080. Poniższe pytania terminala nie zapisują sekretu w historii poleceń:

```bash
npm run assets
dotnet restore DigitalSorter.slnx --locked-mode
read -r -p 'Google Client ID: ' Google__ClientId
read -r -s -p 'Google Client Secret: ' Google__ClientSecret
printf '\n'
export Google__ClientId Google__ClientSecret
ASPNETCORE_ENVIRONMENT=Development \
Broker__PublicOrigin=http://localhost:7080 \
AllowedHosts=localhost \
dotnet run --project src/DigitalSorter.Auth --no-launch-profile --urls http://localhost:7080
```

Otwórz `http://localhost:7080`, połącz Google i wskaż foldery. W drugiej konsoli `curl --fail http://localhost:7080/health` powinien zwrócić `status: ok` i `googleConfigured: true`.

Klucz deweloperski, SQLite i klucze sesji pozostają w `src/DigitalSorter.Auth/data/`; katalog jest ignorowany przez Git. Zachowanie katalogu pozwala odtworzyć sesję po restarcie. Po zakończeniu pracy usuń zmienne z powłoki: `unset Google__ClientId Google__ClientSecret`.

W Testing Google token z zakresem Drive zwykle wygasa po siedmiu dniach; ponowna zgoda jest wtedy oczekiwana. HTTP dla localhost jest wyjątkiem Google dla testów, nie konfiguracją produkcyjną.

## 4. Testy automatyczne

```bash
npx playwright install --with-deps chrome
bash scripts/verify.sh
TEST_HTTP=1 TEST_PORT=7080 bash scripts/test-browser.sh
```

Pierwsze polecenie testowe obejmuje Core, integracje i scenariusze przeglądarkowe po HTTPS. Drugie sprawdza te same scenariusze przeglądarkowe po lokalnym HTTP; jego wyniki trafiają do `test-results/http/`. Testy mają własny OAuth i dane; nie używają ustawionych poświadczeń Google. Porty 7443 i 7080 muszą być wolne. W WSL uruchamiaj przeglądarkę testową zainstalowaną w WSL.

## 5. Produkcja — przygotowanie hostingu

Wymagania infrastruktury określa [architektura](ARCHITECTURE.md#hosting-i-wdrożenie). Poniższa procedura używa serwera Linux z Docker Engine i wtyczką Docker Compose v2; hostuje Web oraz backend razem.

1. Przygotuj serwer i użytkownika wdrożeniowego z dostępem do Dockera.
2. Skieruj rekord DNS domeny, np. `galeria.example.com`, na serwer. Jeżeli ustawiasz AAAA, serwer musi również obsługiwać IPv6.
3. Udostępnij porty 80 i 443; port aplikacji 8080 pozostaje wewnątrz sieci kontenerów.
4. Zweryfikuj `docker version` i `docker compose version`.
5. Pobierz repozytorium do `/opt/digitalsorter`, należącego do użytkownika wdrożeniowego, i wybierz zatwierdzoną wersję:

```bash
git clone --branch feature/full-digital-sorter https://github.com/wiQ1999/DigitalSorter.git /opt/digitalsorter
cd /opt/digitalsorter
```

Przed publicznym udostępnieniem skonfiguruj osobny projekt i klienta Google OAuth dla produkcji według punktu 3, z callbackiem `https://galeria.example.com/auth/callback`. Uzupełnij zweryfikowaną domenę, politykę prywatności i dane aplikacji. `drive.readonly` jest zakresem restricted: przejdź wymaganą weryfikację Google oraz ocenę bezpieczeństwa, jeżeli Google wymaga jej dla sposobu przetwarzania danych. Przełączenie Audience na produkcję nie zastępuje weryfikacji OAuth ani publikacji w Google Play.

## 6. Pierwsze uruchomienie produkcyjne

W katalogu `/opt/digitalsorter` utwórz `.env` dostępny tylko dla użytkownika wdrożeniowego (`chmod 600 .env`):

```dotenv
APP_DOMAIN=galeria.example.com
GOOGLE_CLIENT_ID=wartosc-z-google-cloud
GOOGLE_CLIENT_SECRET=sekret-z-google-cloud
TOKEN_ENCRYPTION_KEY=staly-klucz-base64
```

Zastąp przykładowe wartości własnymi. `APP_DOMAIN` to sama nazwa hosta, bez `https://` i ścieżki. Dla `TOKEN_ENCRYPTION_KEY` jednorazowo wygeneruj 32 losowe bajty w Base64, np. `openssl rand -base64 32`, i zachowaj wynik w magazynie sekretów oraz kopii zapasowej. Nie generuj nowego klucza przy aktualizacjach.

```bash
docker compose up -d --build
docker compose ps
curl --fail https://galeria.example.com/health
```

Caddy pobiera i odnawia certyfikat HTTPS. Pierwsze wystawienie wymaga poprawnego DNS i dostępu do portów. Wewnątrz kontenerów Caddy komunikuje się z backendem przez HTTP; publiczny origin aplikacji pozostaje HTTPS. `compose.yaml` ustawia origin, zaufany adres proxy i trwały wolumen danych.

Sprawdź w przeglądarce: logowanie, foldery, odtwarzanie zdjęcia i filmu, ponowne wejście po restarcie oraz wylogowanie. Następnie wykonaj parowanie TV. `googleConfigured: true` potwierdza obecność konfiguracji, nie poprawność logowania Google.

## 7. Aktualizacje i kopie zapasowe

Aktualizacja ręczna: pobierz wybraną wersję kodu, zachowaj `.env` i uruchom ponownie `docker compose up -d --build`. Nie używaj `docker compose down -v`: usuwa trwałe dane. Zmiana Client ID może wymagać ponownej zgody Google; utrata klucza szyfrowania uniemożliwia odzyskanie zapisanych poświadczeń.

Przed aktualizacją wykonaj spójną kopię SQLite, kluczy sesji i konfiguracji, podczas krótkiej przerwy w pracy:

```bash
umask 077
backup_dir="$PWD/backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
docker compose stop app
docker compose cp app:/app/data "$backup_dir/data"
cp .env "$backup_dir/.env"
docker compose start app
```

Jeśli kopiowanie się nie powiedzie, również uruchom `docker compose start app`. Kopię przechowuj zaszyfrowaną poza serwerem; zawiera sekrety. Przy odtwarzaniu zatrzymaj backend, przywróć komplet danych do wolumenu `/app/data` oraz odpowiadający mu `.env`, zachowaj uprawnienia zapisu dla użytkownika kontenera i dopiero uruchom aplikację. Sprawdź logowanie i odświeżenie dostępu po restarcie.

Automatyczne wdrożenie przez GitHub Actions:

1. Przygotuj `/opt/digitalsorter/.env` na serwerze.
2. W repozytorium utwórz GitHub Environment `production`.
3. Dodaj zmienne `DEPLOY_HOST`, `DEPLOY_USER` oraz sekrety `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS`. Klucz hosta SSH zweryfikuj niezależnie przed zapisaniem go do `DEPLOY_KNOWN_HOSTS`.
4. Zapewnij użytkownikowi SSH dostęp do katalogu wdrożenia i Dockera.
5. Uruchom ręcznie workflow `Deploy` dla wybranej wersji. Najpierw wykonuje `Validate`, następnie przesyła obraz i uruchamia Compose. Nie tworzy domeny, `.env` ani kopii zapasowej.
6. Sprawdź `/health`, logowanie i istniejącą sesję TV.

## 8. Diagnostyka

| Objaw | Sprawdzenie |
|---|---|
| Zajęty port lokalny | Zatrzymaj poprzedni serwer albo zmień port także w originie i callbacku OAuth. |
| HTTP odrzucane | Wymagane `Development`, origin loopback i ten sam host/port; na TV i produkcji wymagane HTTPS. |
| `redirect_uri_mismatch` | Dokładny callback i właściwy klient Web application w Google Cloud. |
| Brak certyfikatu produkcyjnego | DNS, porty 80/443, `docker compose logs --tail=100 https`. |
| Błąd backendu lub 502 | `docker compose logs --tail=100 app`, wartości `.env`, prawa do wolumenu i wolne miejsce. |
| Konieczna ponowna zgoda | Status Testing Google, cofnięta zgoda, zmiana Client ID lub wygaśnięta sesja. |

Źródło: [Google OAuth dla aplikacji webowych i wyjątek localhost](https://developers.google.com/identity/protocols/oauth2/web-server).
