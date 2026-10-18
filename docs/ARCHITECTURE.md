# Architektura — DigitalSorter

## Warstwy rozwiązania

- `DigitalSorter.Core` — logika aplikacji, walidacja metadanych, klasyfikowanie materiałów i sortowanie.
- `DigitalSorter.Data.GoogleDrive` — odczyt plików i metadanych z Google Drive.
- `DigitalSorter.Auth` — wspólny backend OAuth i sesji dla aplikacji webowej oraz Android TV.
- `DigitalSorter.AndroidTV` — warstwa prezentacji Android TV oparta na .NET MAUI.
- `DigitalSorter.Web` — warstwa prezentacji dla przeglądarki oparta na standalone Blazor WebAssembly.
- `DigitalSorter.Contracts` — kontrakty HTTP brokera, niezależne od Core.

## Core

- Implementacja w języku C# na platformie .NET 10.0.
- Brak zależności od Google Drive, brokera OAuth oraz frameworków warstw prezentacji.
- Definiowanie interfejsów dostępu do danych i wspólnych modeli.
- Wspólne reguły biznesowe dla wszystkich warstw prezentacji.

## Zależności

- Warstwy prezentacji korzystają z interfejsów i modeli Core oraz adapterów sesji brokera.
- Warstwa danych implementuje interfejsy zdefiniowane w Core.
- Konfiguracja aplikacji łączy implementacje warstw przez wstrzykiwanie zależności.
- Logika dostępu do Google Drive nie występuje w Core ani w warstwach prezentacji.
- Broker obsługuje autoryzację Google, sesje klientów i parowanie TV; nie realizuje logiki Core ani przesyłania multimediów.

## Wspólny broker OAuth

- Web i Android TV korzystają z jednej implementacji OAuth Google w `DigitalSorter.Auth`: zgoda, walidacja `state` i callbacku, wymiana kodu, zapis, odświeżanie i unieważnianie tokenów.
- Google OAuth 2.0 Authorization Code Flow używa `access_type=offline` oraz zakresów `openid` i `drive.readonly`; konto jest identyfikowane przez zweryfikowane `sub` OpenID Connect.
- Web i TV współdzielą magazyn poświadczeń Google i usługę wydawania tokenów `access_token`; osobne są sesje webowe, sesje urządzeń i endpointy parowania TV.
- Backend trwale przechowuje zaszyfrowany `refresh_token`, identyfikator konta, klienta OAuth i przyznane zakresy; klucze szyfrowania oraz `client_secret` pozostają w magazynie sekretów.
- `refresh_token` i Google `client_secret` nie trafiają do przeglądarki ani TV. Klient pobiera `access_token` przez HTTPS, przechowuje go w pamięci i wywołuje Drive bezpośrednio.
- Odświeżanie dostępu nie wymaga ponownej zgody, dopóki autoryzacja Google pozostaje ważna; brak nowego `refresh_token` w odpowiedzi nie usuwa zapisanego tokenu.
- Utrata autoryzacji Google wymaga ponownego połączenia konta. Wylogowanie klienta unieważnia tylko jego sesję; odłączenie Google cofa zgodę i usuwa poświadczenia oraz sesje powiązanych klientów.

## Magazyn danych brokera

Decyzja przyjęta 18.10.2026: serwisy OAuth, uwierzytelnianie i endpointy korzystają z `IBrokerStore`, a uruchomienie aplikacji z `IBrokerStoreInitializer`. Kontrakty nie udostępniają `DbContext`, `DbSet` ani `IQueryable`. Zapytania, zapis i transakcje są zamknięte w `DigitalSorter.Auth/Persistence`; obecna implementacja to `EfBrokerStore` z modelem `BrokerDb`.

SQLite pozostaje domyślnym silnikiem. Bez dodatkowej konfiguracji aplikacja otwiera dotychczasowy plik `Broker:DataDirectory/broker.db`. Nazwy tabel, kolumn i indeksów oraz zapis dat jako liczby ticków UTC pozostają zgodne z istniejącą bazą; nie jest potrzebne przenoszenie danych. Opcjonalny `ConnectionStrings:Broker` (`ConnectionStrings__Broker` w środowisku) zastępuje wyłącznie connection string SQLite. `Broker:DataDirectory` nadal zawiera zaszyfrowane klucze sesji i lokalny klucz deweloperski.

Zmiana silnika po wybraniu hostingu wymaga dodania zgodnego z EF Core providera i zastąpienia w `Program.cs` rejestracji `AddSqliteBrokerStorage(...)` rejestracją `AddRelationalBrokerStorage(options => options.Use...(...))` z konfiguracją nowego providera. Nie wymaga zmian w logice OAuth, sesji, parowania ani klientach. Alternatywny magazyn może również zastąpić oba interfejsy przez DI bez używania EF. Pozostawienie dostępu bezpośrednio przez EF w serwisach byłoby prostsze, ale wiązałoby je z jego operacjami zapisu i transakcji; wybrany kontrakt zamyka te zależności w jednym miejscu.

`EfBrokerStoreInitializer` zachowuje dotychczasowe `EnsureCreatedAsync`. To tworzenie brakującego schematu, a nie mechanizm aktualizacji istniejącej bazy. Przy wdrażaniu innego silnika trzeba przygotować i zweryfikować jego schemat, migracje oraz osobne przeniesienie danych. Strategię inicjalizacji lub migracji można podmienić przez `IBrokerStoreInitializer`. Aktualnie projekt nie zawiera konfiguracji ani migracji dla nieustalonego jeszcze silnika hostingu.

`TryConsumePairingAsync` obejmuje jedną transakcją warunkową zmianę parowania z `approved` na `consumed` i utworzenie sesji. Każda przyszła implementacja musi zachować jednokrotność odbioru oraz wycofanie zmiany statusu przy błędzie zapisu sesji. Testy obejmują równoległy odbiór, błąd zapisu i ponowną próbę, granice ważności w UTC, wcześniejszy schemat SQLite i pełny przepływ HTTP z testowym magazynem bez EF/SQLite. Nowy provider wymaga uruchomienia testów integracyjnych również na docelowym silniku.

## Android TV — parowanie przez telefon

- Aplikacja MAUI hostuje wspólny interfejs `DigitalSorter.Web` w WebView; adapter natywny zapewnia `SecureStorage`, konfigurację serwera i obsługę Wstecz. Logowanie Google odbywa się na telefonie.
- Ikona i ekran startowy MAUI używają tego samego `wwwroot/icon.svg`, co znak w nagłówku Web. Kafelek launchera `Resources/Images/tv_banner.svg` zawiera ten znak i nazwę aplikacji z tekstem zamienionym na ścieżki wektorowe.
- TV inicjuje parowanie i wyświetla QR oraz krótki kod; sekret żądania pozostaje na TV.
- Użytkownik otwiera stronę brokera na telefonie, autoryzuje konto Google wspólnym przepływem OAuth i potwierdza powiązanie telewizora.
- Przed zatwierdzeniem broker sprawdza autoryzację Google; ponowna zgoda zachowuje kod parowania.
- `Broker:PairingOrigin` wskazuje adres QR niezależnie od `Broker:PublicOrigin`, który pozostaje kanonicznym adresem OAuth. Oba muszą być jawnie skonfigurowanymi originami HTTPS. Host nagłówka żądania nie steruje adresem QR.
- Strona `/pair` inicjuje wyłącznie konfigurację i sesję brokera, także po odpowiedzi 401 wymagającej ponownej zgody. Nie uruchamia odtwarzacza ani service workera; ich niedostępność na telefonie nie powoduje błędu połączenia z brokerem.
- Kod parowania jest jednorazowy, ma krótki termin ważności i limit prób. Odbiór sesji wymaga sekretu pierwotnego żądania TV.
- TV odpytuje broker o wynik i po zatwierdzeniu otrzymuje niezależną sesję urządzenia. Parowanie jest mechanizmem brokera, a nie Google Device Flow.
- Poświadczenie sesji TV jest zapisywane przez MAUI `SecureStorage`, wykluczone z kopii zapasowych i usuwane przy wylogowaniu; utrata magazynu wymaga ponownego parowania.

## Aplikacja webowa

- `DigitalSorter.Web` jest aplikacją standalone Blazor WebAssembly wykonywaną po stronie przeglądarki.
- Interfejs jest budowany z Razor Components (`.razor`) i HTML5; zdjęcia i filmy są prezentowane przez natywne elementy przeglądarki.
- Stylowanie wykorzystuje CSS, w tym CSS isolation dla stylów przypisanych do komponentów.
- Logika interfejsu i stan aplikacji są realizowane przede wszystkim w C#; JavaScript jest ograniczony do niewielkich modułów wywoływanych przez JS interop, gdy wymagają tego API przeglądarki lub biblioteki zewnętrzne.
- TypeScript ani dodatkowy framework JavaScript nie są częścią podstawowego stosu technologicznego.
- Interfejs Web i TV ma stałą ciemną paletę z akcentami niebieskim, zielonym i żółtym. TV obsługuje nawigację pilotem.
- Materiał wykorzystuje całą sekcję odtwarzacza z zachowaniem proporcji. Przezroczysty pasek opisu jest nakładką; w pełnym ekranie również nawigacja nie rezerwuje wysokości. Nakładki przy filmie pozostawiają dostęp do natywnych kontrolek wideo.
- Potwierdzenie odłączenia konta używa modalnego `dialog`: fokus zaczyna na „Anuluj”, tło jest nieaktywne, strzałki poruszają się w obrębie okna, a Escape/Wstecz zamyka je i przywraca poprzedni fokus.
- Na komputerze tło odtwarzacza próbkuje kolory z brzegów bieżącej klatki zdjęcia lub filmu; pełny ekran ma czarne tło.
- Adapter przeglądarkowy w `DigitalSorter.Data.GoogleDrive` strumieniuje żądania Range z nagłówkiem OAuth przez service worker. Nie zapisuje plików ani tokenów w cache. Na Web HEIC/HEIF dekoduje klient; TV najpierw korzysta z podglądu Drive dopasowanego do ekranu. Gdy CORS Google ukrywa `Content-Range`, adapter odtwarza go z żądanego zakresu, długości odpowiedzi i rozmiaru pliku pobranego z API. Weryfikuje granice; pamięta najwyżej 64 rozmiary dla klienta, wyłącznie w RAM.
- Krótkotrwały podpisany adres podglądu jest pobierany z uwierzytelnionego API. Pobranie obrazu z dozwolonego hosta Google nie zawiera nagłówka OAuth, który powodował nieobsługiwany preflight CORS; żądania pomijają cookies i trwały cache.
- Gdy miniatura Drive jest niedostępna lub blokowana przez CORS, klient tworzy podgląd z oryginalnego zdjęcia albo klatki filmu; bez pośrednictwa backendu. Na TV kosztowne awaryjne dekodowanie miniatur filmów i HEIF zastępuje ikona.
- Źródło biblioteki raportuje `MediaReadProgress` przez `IProgress`: wyszukiwanie bez znanej całości, następnie odczyt unikalnych obsługiwanych materiałów. `ResizeObserver` dopasowuje liczbę miniatur do szerokości paska; Core zwraca żądaną liczbę kolejnych pozycji.
- Sesja webowa używa cookie `HttpOnly`, `Secure`, z określoną ważnością, polityką `SameSite` i ochroną CSRF.

- Odczyt JPEG pomija dane obrazu po nagłówkach; trzy zadania równolegle odczytują brakujące metadane, zachowując kolejność i anulowanie. Drive `createdTime` i `modifiedTime` nie zastępują daty wykonania.
- Klient przygotowuje jedno następne zdjęcie w RAM: podgląd 1920 px na Web i 1280 px na TV. Pełny ekran na Web pobiera oryginał dla pełnej jakości. Przekazuje przygotowany element obrazu do odtwarzacza bez ponownego pobierania; wylogowanie i zmiana kolekcji zwalniają go. Nie używa CacheStorage, IndexedDB ani localStorage do mediów/tokenów.
- Broker wylicza rewizję CSS/JS; klient wersjonuje adres modułu i arkuszy, żeby WebView po aktualizacji nie zachowywał starego układu. Zasoby interfejsu wymagają rewalidacji; nie wpływa to na zakaz trwałego cache materiałów.

## Hosting i wdrożenie

- Hosting uruchamia backend ASP.NET Core na .NET 10 i jednocześnie udostępnia pliki `DigitalSorter.Web`; dostawca pozostaje dowolny.
- Frontend i backend działają pod jednym originem HTTPS (schemat, host i port); komunikacja web–broker używa adresów względnych.
- HTTPS obowiązuje również w testach Web i TV. Procedury certyfikatów i lokalnego backendu: [wdrożenie](DEPLOYMENT.md); instalacja i publikacja: [Android TV](ANDROID_TV.md).
- Ścieżki: `/` — frontend z `base href="/"`, `/auth/` — OAuth i sesja webowa, `/api/` — API brokera, `/pair` — zatwierdzanie TV na telefonie.
- Routing SPA zwraca `index.html` dla tras frontendu; ścieżki API i OAuth nie podlegają temu fallbackowi.
- Opublikowane zasoby są udostępniane bez zmiany zawartości, z właściwymi typami MIME i obsługą kompresji.
- Magazyny poświadczeń, sesji i sekretów zachowują dane po restarcie oraz ponownym wdrożeniu.
- GitHub Actions buduje i wdraża frontend oraz backend razem, z użyciem `dotnet publish`.

## Weryfikacja

Standard testów, wymagane pokrycie Core, zakres zestawów i polecenia uruchomienia opisuje [dokumentacja testowania](TESTING.md). Wyniki konkretnych przebiegów są publikowane w datowanych raportach wskazanych w [indeksie dokumentacji](README.md#wyniki-historyczne-i-licencje).
