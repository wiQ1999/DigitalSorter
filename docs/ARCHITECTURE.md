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

## Android TV — parowanie przez telefon

- Aplikacja MAUI hostuje wspólny interfejs `DigitalSorter.Web` w WebView; adapter natywny zapewnia `SecureStorage`, konfigurację serwera i obsługę Wstecz. Logowanie Google odbywa się na telefonie.
- TV inicjuje parowanie i wyświetla QR oraz krótki kod; sekret żądania pozostaje na TV.
- Użytkownik otwiera stronę brokera na telefonie, autoryzuje konto Google wspólnym przepływem OAuth i potwierdza powiązanie telewizora.
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
- Tło odtwarzacza próbkuje kolory z brzegów bieżącej klatki zdjęcia lub filmu; pełny ekran ma czarne tło.
- Adapter przeglądarkowy w `DigitalSorter.Data.GoogleDrive` strumieniuje żądania Range z nagłówkiem OAuth przez service worker. Nie zapisuje plików ani tokenów w cache; HEIC/HEIF dekoduje klient.
- Sesja webowa używa cookie `HttpOnly`, `Secure`, z określoną ważnością, polityką `SameSite` i ochroną CSRF.

## Hosting i wdrożenie

- Hosting uruchamia backend ASP.NET Core na .NET 10 i jednocześnie udostępnia pliki `DigitalSorter.Web`; dostawca pozostaje dowolny.
- Frontend i backend działają pod jednym originem HTTPS (schemat, host i port); komunikacja web–broker używa adresów względnych.
- Ścieżki: `/` — frontend z `base href="/"`, `/auth/` — OAuth i sesja webowa, `/api/` — API brokera, `/pair` — zatwierdzanie TV na telefonie.
- Routing SPA zwraca `index.html` dla tras frontendu; ścieżki API i OAuth nie podlegają temu fallbackowi.
- Opublikowane zasoby są udostępniane bez zmiany zawartości, z właściwymi typami MIME i obsługą kompresji.
- Magazyny poświadczeń, sesji i sekretów zachowują dane po restarcie oraz ponownym wdrożeniu.
- GitHub Actions buduje i wdraża frontend oraz backend razem, z użyciem `dotnet publish`.

## Standard testów

### Testy jednostkowe

- Core musi być pokryty testami jednostkowymi zgodnymi ze schematem Arrange–Act–Assert.
- Testy muszą być deterministyczne i niezależne od sieci, systemu plików oraz usług zewnętrznych.
- Minimalne pokrycie Core: 80% linii i 80% gałęzi.
- Reguły walidacji daty, klasyfikowania materiałów i sortowania wymagają testów wszystkich istotnych przypadków.

### Testy integracyjne

- Warstwa Google Drive jest testowana przez jej publiczne interfejsy z użyciem kontrolowanych danych testowych.
- Testy obejmują uwierzytelnianie, odświeżanie tokenów, wiele folderów, brak metadanych oraz błędy dostępu i sieci.
- Testy brokera obejmują wspólny cykl OAuth dla obu klientów, odświeżanie po restarcie, ważność i jednokrotność parowania, powiązanie sesji z właściwym TV oraz wylogowanie i odłączenie Google.
- Testy nie mogą korzystać z danych rzeczywistych użytkowników.

### Pozostałe warstwy

- Dla warstw prezentacji zalecane są testy logiki widoku, nawigacji, licznika i trybu pełnoekranowego.
- Potok CI odrzuca zmianę, gdy testy nie przechodzą lub Core nie spełnia wymaganego pokrycia.
