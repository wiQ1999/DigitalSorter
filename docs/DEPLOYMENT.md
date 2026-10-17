# Uruchomienie testowe i wdrożenie produkcyjne

Web i Android TV używają HTTPS również w testach. Wymagania hostingu: [architektura](ARCHITECTURE.md#hosting-i-wdrożenie). Instalacja TV: [Android TV](ANDROID_TV.md).

Polecenia: Bash na Linux/macOS lub WSL, z katalogu repozytorium. Wymagane: Git, SDK z `global.json`, Node.js 22+ i OpenSSL; do certyfikatu TV także Certbot oraz własna domena z możliwością edycji DNS.

```bash
git clone --branch master https://github.com/wiQ1999/DigitalSorter.git
cd DigitalSorter
npm ci --ignore-scripts
```

Do wdrożenia wybieraj wydany tag lub zatwierdzony commit.

## 1. Certyfikat do testów tylko na laptopie

```bash
umask 077
tls_dir="$HOME/.digitalsorter/tls"
mkdir -p "$tls_dir"
dotnet dev-certs https --trust
read -r -s -p 'Hasło pliku PFX: ' TEST_CERTIFICATE_PASSWORD
printf '\n'
export TEST_CERTIFICATE_PASSWORD
export TEST_CERTIFICATE="$tls_dir/localhost.pfx"
dotnet dev-certs https --export-path "$TEST_CERTIFICATE" --password "$TEST_CERTIFICATE_PASSWORD"
openssl pkcs12 -in "$TEST_CERTIFICATE" -passin env:TEST_CERTIFICATE_PASSWORD -nokeys -out "$tls_dir/localhost.crt"
export TEST_ORIGIN=https://localhost:7043 TEST_PORT=7043 TEST_BIND_ADDRESS=127.0.0.1 AllowedHosts=localhost
```

Potwierdź zaufanie w systemie. W WSL/Windows zaimportuj `localhost.crt` do zaufanych głównych certyfikatów Windows/przeglądarki. [Instrukcja .NET](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs). Certyfikat `localhost` nie służy do testów TV przez LAN.

## 2. Certyfikat do lokalnego backendu dla TV i telefonu

1. Zarezerwuj laptopowi stały adres LAN, np. `192.168.1.20`.
2. Wybierz subdomenę własnej domeny, np. `tv-test.twojadomena.pl`. Ustaw jej rekord A na adres LAN laptopa w DNS używanym przez laptop, TV i telefon. Jeżeli router blokuje publiczne odpowiedzi DNS z adresem prywatnym, dodaj rekord w lokalnym DNS routera.
3. Zainstaluj [Certbot](https://certbot.eff.org/) i uzyskaj bezpłatny certyfikat Let's Encrypt przez DNS-01. Zastąp przykładową domenę własną:

```bash
umask 077
test_domain=tv-test.twojadomena.pl
tls_dir="$HOME/.digitalsorter/tls"
mkdir -p "$tls_dir"
certbot certonly --manual --preferred-challenges dns \
  --config-dir "$tls_dir/acme" --work-dir "$tls_dir/work" --logs-dir "$tls_dir/logs" \
  -d "$test_domain"
```

4. Podaj dane wymagane przez Certbot. Dodaj wskazany rekord TXT `_acme-challenge` w publicznym DNS domeny i kontynuuj po jego propagacji. DNS-01 nie wymaga udostępniania backendu ani portów routera w Internecie.
5. Utwórz PFX z kluczem i pełnym łańcuchem certyfikatów:

```bash
read -r -s -p 'Hasło pliku PFX: ' TEST_CERTIFICATE_PASSWORD
printf '\n'
export TEST_CERTIFICATE_PASSWORD
export TEST_CERTIFICATE="$tls_dir/tv-test.pfx"
openssl pkcs12 -export -out "$TEST_CERTIFICATE" \
  -inkey "$tls_dir/acme/live/$test_domain/privkey.pem" \
  -in "$tls_dir/acme/live/$test_domain/cert.pem" \
  -certfile "$tls_dir/acme/live/$test_domain/chain.pem" \
  -passout env:TEST_CERTIFICATE_PASSWORD
export TEST_ORIGIN="https://$test_domain:7043" TEST_PORT=7043 TEST_BIND_ADDRESS=0.0.0.0 AllowedHosts="$test_domain"
```

6. Dopuść TCP 7043 w zaporze laptopa tylko z sieci lokalnej. Laptop, TV i telefon podłącz do tej samej sieci bez izolacji klientów.
7. Otwieraj nazwę domenową, nie adres IP: certyfikat obejmuje domenę. TV i telefon korzystają z systemowego zaufania do publicznego CA; nie trzeba instalować testowego CA ani zmieniać APK.
8. Przed wygaśnięciem certyfikatu powtórz wystawienie, odtwórz PFX i uruchom backend ponownie. Ręczny DNS-01 nie odnawia się automatycznie; automatyzacja wymaga integracji z API dostawcy DNS.

### WSL2 z siecią NAT

Dodatkowo przekieruj port Windows do WSL. PowerShell jako administrator:

```powershell
$wslIp = (wsl.exe hostname -I).Trim().Split(' ')[0]
netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=7043 connectaddress=$wslIp connectport=7043
New-NetFirewallRule -DisplayName "DigitalSorter LAN test" -Direction Inbound -Protocol TCP -LocalPort 7043 -RemoteAddress LocalSubnet -Profile Private -Action Allow
```

Rekord DNS wskazuje adres LAN Windows. Po zmianie adresu WSL zaktualizuj przekierowanie. Po testach usuń je przez `netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=7043` i regułę przez `Get-NetFirewallRule -DisplayName "DigitalSorter LAN test" | Remove-NetFirewallRule`.

## 3. Lokalne demo bez konta Google

W tej samej powłoce, po punkcie 1 albo 2:

```bash
bash scripts/run-local-demo.sh
```

1. Poczekaj na `Controlled OAuth/Drive test host ready` i otwórz adres `TEST_ORIGIN`.
2. Wybierz „Połącz Google Drive” → „Zatwierdź dostęp testowy”.
3. Wybierz folder „Podróże i wspomnienia” → „Otwórz kolekcję”.
4. Dla wariantu LAN zainstaluj APK według [instrukcji TV](ANDROID_TV.md#4-pierwsze-uruchomienie-i-parowanie) i wpisz ten sam adres. Na telefonie również zatwierdź kontrolowany OAuth.
5. Sprawdź zdjęcia, filmy, pilot, pełny ekran i ponowne uruchomienie TV. Backend pozostaw uruchomiony podczas restartu TV.
6. Zakończ backend przez `Ctrl+C`. Skrypt usuwa dane tymczasowe; kolejne uruchomienie wymaga nowego parowania.

Demo używa syntetycznych danych, bez sekretów Google. Host testowy nie wchodzi do produkcji. Sprawdzenie: `curl --fail "$TEST_ORIGIN/health"`; przy certyfikacie .NET dodaj `--cacert "$tls_dir/localhost.crt"`.

## 4. Lokalny backend z rzeczywistym Google Drive — opcjonalnie

W [Google Cloud Console](https://console.cloud.google.com/):

1. Utwórz projekt testowy i włącz Google Drive API.
2. W Google Auth Platform uzupełnij Branding, Audience i kontakt. Dla External/Testing dodaj własne konto do użytkowników testowych.
3. Dodaj zakresy `openid` i `https://www.googleapis.com/auth/drive.readonly`.
4. Utwórz klienta **Web application** z callbackiem `<TEST_ORIGIN>/auth/callback`, np. `https://localhost:7043/auth/callback` albo `https://tv-test.twojadomena.pl:7043/auth/callback`. Dla własnej domeny uzupełnij jej autoryzację/weryfikację, jeśli wymaga tego Google.
5. Zatrzymaj demo. W powłoce z konfiguracją certyfikatu z punktu 1 albo 2 uruchom:

```bash
npm run assets
dotnet restore DigitalSorter.slnx --locked-mode
read -r -p 'Google Client ID: ' Google__ClientId
read -r -s -p 'Google Client Secret: ' Google__ClientSecret
printf '\n'
export Google__ClientId Google__ClientSecret
ASPNETCORE_ENVIRONMENT=Development Broker__PublicOrigin="$TEST_ORIGIN" \
ASPNETCORE_Kestrel__Certificates__Default__Path="$TEST_CERTIFICATE" \
ASPNETCORE_Kestrel__Certificates__Default__Password="$TEST_CERTIFICATE_PASSWORD" \
dotnet run --project src/DigitalSorter.Auth --no-launch-profile --urls "https://$TEST_BIND_ADDRESS:$TEST_PORT"
```

Klient OAuth jest wspólny dla Web i TV. SQLite, klucz deweloperski i sesje pozostają w ignorowanym `src/DigitalSorter.Auth/data/`; ich zachowanie umożliwia ponowne połączenie po restarcie. W Testing Google token Drive zwykle wygasa po siedmiu dniach. Po pracy usuń sekrety z powłoki: `unset Google__ClientId Google__ClientSecret TEST_CERTIFICATE_PASSWORD`.

## 5. Testy automatyczne

```bash
npx playwright install --with-deps chrome
TEST_PORT=7443 bash scripts/verify.sh
```

Testy używają własnego certyfikatu, kontrolowanego OAuth i syntetycznych plików. Nie wymagają konta Google ani certyfikatu z punktów 1–2. Port 7443 musi być wolny. Wyniki: `test-results/`. Test natywnego TV wykonuje osobny job CI z emulatorem i tymczasowym CA w Debug.

## 6. Hosting produkcyjny i HTTPS

1. Przygotuj serwer Linux z Docker Engine, Compose v2 i użytkownikiem wdrożeniowym z dostępem do Dockera.
2. Skieruj DNS domeny na publiczny adres serwera; udostępnij TCP 80/443. Rekord AAAA ustawiaj tylko przy działającym IPv6.
3. Pobierz zatwierdzoną wersję repozytorium do `/opt/digitalsorter`, należącego do użytkownika wdrożeniowego.
4. Skonfiguruj osobny klient OAuth produkcji z callbackiem `https://galeria.example.com/auth/callback`. Uzupełnij domenę i politykę prywatności. Przejdź weryfikację restricted scope `drive.readonly` oraz ocenę bezpieczeństwa, jeżeli wymaga jej Google.
5. W katalogu wdrożenia utwórz `.env` z prawami `600`:

```dotenv
APP_DOMAIN=galeria.example.com
GOOGLE_CLIENT_ID=wartosc-z-google-cloud
GOOGLE_CLIENT_SECRET=sekret-z-google-cloud
TOKEN_ENCRYPTION_KEY=staly-klucz-base64
```

Zastąp przykłady własnymi wartościami. Klucz wygeneruj jednorazowo przez `openssl rand -base64 32`; zachowuj go przy aktualizacjach i w kopii zapasowej.

```bash
cd /opt/digitalsorter
docker compose up -d --build
docker compose ps
curl --fail https://galeria.example.com/health
```

Caddy automatycznie uzyskuje i odnawia publiczny certyfikat HTTPS; nie potrzeba Certbota z punktu 2. Web i backend są udostępniane razem. Wewnętrzne połączenie Caddy–backend przez HTTP pozostaje w sieci kontenerów. Sprawdź logowanie, zdjęcie, film, restart i parowanie TV. `googleConfigured: true` potwierdza konfigurację, nie poprawność logowania Google.

## 7. Aktualizacje, backup i diagnostyka

Aktualizacja: wybierz zatwierdzony tag/commit i wykonaj `docker compose up -d --build`, zachowując `.env` i wolumeny. Nie używaj `docker compose down -v`.

Spójna kopia danych podczas krótkiej przerwy:

```bash
umask 077
backup_dir="$PWD/backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
docker compose stop app
docker compose cp app:/app/data "$backup_dir/data"
cp .env "$backup_dir/.env"
docker compose start app
```

Uruchom backend również po błędzie kopiowania. Kopię z sekretami przechowuj zaszyfrowaną poza serwerem. Odtwarzaj kompletny katalog danych i odpowiadający mu `.env` przy zatrzymanym backendzie, z prawami zapisu dla użytkownika kontenera.

GitHub Actions: w Environment `production` ustaw `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS` (zweryfikowany klucz hosta). Przygotuj `/opt/digitalsorter/.env` i dostęp SSH/Docker. Uruchom workflow `Deploy`; wykona walidację i wdrożenie, bez tworzenia DNS, sekretów i backupu.

| Problem | Sprawdzenie |
|---|---|
| TV/telefon nie łączy się lokalnie | Ten sam DNS i LAN, port 7043, zapora, izolacja klientów, przekierowanie WSL. |
| Błąd certyfikatu | Nazwa domeny, pełny łańcuch, termin ważności, zegar TV i aktualizacje systemu; nie pomijaj walidacji TLS. |
| `redirect_uri_mismatch` | Dokładny callback, port i właściwy klient Web application. |
| Certyfikat produkcyjny / 502 | DNS, porty 80/443, `docker compose logs --tail=100 https app`. |

Źródła: [Let's Encrypt DNS-01](https://letsencrypt.org/docs/challenge-types/#dns-01-challenge), [Google OAuth](https://developers.google.com/identity/protocols/oauth2/web-server).
