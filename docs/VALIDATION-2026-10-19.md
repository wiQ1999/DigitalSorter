# Weryfikacja poprawki Wstecz na Android TV — 19.10.2026

Raport dotyczy poprawki `fab2765` na branchu `codex/fix-android-tv-back`. Instrukcje odtwarzania testów znajdują się w [dokumentacji testowania](TESTING.md).

## Błąd w CI i przyczyna

Sprawdzono przebiegi `Validate` dla [master, e4a4cc8](https://github.com/wiQ1999/DigitalSorter/actions/runs/39163751619) oraz [brancha dokumentacji, 2827303](https://github.com/wiQ1999/DigitalSorter/actions/runs/39163751889). W obu zadania `web-and-tests` i `android-tv` zakończyły się powodzeniem. Zadanie `android-tv-runtime` uruchomiło aplikację, zatwierdziło parowanie i odtworzyło sesję po restarcie, ale przekroczyło limit 60 sekund przy oczekiwaniu na zamknięcie okna „Odłącz konto” po `KEYCODE_BACK` (`disconnectFocus`, `tests/browser/ui-helpers.mjs:124`).

Emulator CI używa Android TV API 36. Aplikacja celuje w API 36 i używa MAUI 10.0.0, którego obsługa Wstecz przechodzi przez `OnBackPressed`. Android 16 domyślnie pomija ten mechanizm. Dodano `android:enableOnBackInvokedCallback="false"`, czyli opisane w [dokumentacji Androida](https://developer.android.com/about/versions/16/behavior-changes-16#predictive-back) ustawienie zgodności. Test natywny nadal wysyła rzeczywisty przycisk Wstecz i sprawdza zamknięcie okna oraz przywrócenie fokusu. Zmiana manifestu unieważnia dotychczasowy cache APK dzięki istniejącemu kluczowi obejmującemu `src/DigitalSorter.AndroidTV/**`.

## Środowisko i wykonane sprawdzenia

Windows, lokalne .NET SDK 10.0.402, Node.js 22.14.0, Chrome, Android SDK API 36 i JDK 21.0.8. Testy używały kontrolowanego hosta HTTPS i syntetycznych mediów, bez konta Google i prywatnych plików użytkownika.

| Sprawdzenie | Wynik |
|---|---|
| Core, Release, locked restore i kontrola progu pokrycia | 67/67; 95,65% linii i 100% gałęzi |
| Integracja, Release, locked restore | 76/76 |
| `scripts/test-browser.ps1`, pełny zestaw | 27/27, w tym dialog, fokus, adapter TV i pełny ekran |
| Android Debug, `RuntimeIdentifier=android-x64`, locked restore, `-warnaserror`, testowy CA | Build APK bez ostrzeżeń i błędów |
| `scripts/check-tv-apk.ps1` na zbudowanym podpisanym APK | Obecne biblioteki ARM32, ARM64 i x64 |
| Scalony manifest i manifest wewnątrz podpisanego APK, odczytany przez `aapt2 dump xmltree` | API docelowe 36; `enableOnBackInvokedCallback=false`, `allowBackup=false`, `usesCleartextTraffic=false` |
| `git diff --check` | Powodzenie |

Polecenia .NET odpowiadały zestawom z `scripts/verify.sh`: Core z `CollectCoverage=true`, formatem Cobertura, progiem 80% linii i gałęzi; integracja z raportem TRX. Oba zestawy używały `-c Release -p:RestoreLockedMode=true`. Pliki wynikowe znajdują się w ignorowanym `test-results/`.

Build Androida używał `dotnet build src/DigitalSorter.AndroidTV -c Debug -p:RuntimeIdentifier=android-x64 -p:RestoreLockedMode=true -warnaserror`, jawnych ścieżek do Android SDK/JDK oraz `DigitalSorterTestCertificate` wskazującego syntetyczny CA z `test-results/native-fixture/`. Nie budowano ponownie wariantu Release; utworzony APK z testowym CA służy wyłącznie kontrolowanemu środowisku testowemu.

## Ograniczenia

Nie uruchomiono lokalnie natywnego scenariusza na Android TV API 36 ani na fizycznym TV: brak podłączonego urządzenia i lokalnego obrazu emulatora API 36. Przejście 170 testów oraz kontrola manifestu nie potwierdzają jeszcze zachowania przycisku na Androidzie 16. Wymagany jest nowy przebieg CI dla poprawki; ponowienie starego przebiegu testowałoby poprzedni manifest. Nie wysyłano lokalnych commitów do GitHuba.

Przy lokalnym odtwarzaniu pierwsze próby w sandboxie nie ukończyły budowania lub uruchamiania procesów testowych; zestawy .NET i przeglądarkowe wykonano ponownie poza tym ograniczeniem. Restore Core zgłosił NU1900 z powodu niedostępnego indeksu audytu NuGet; testy i kontrola pokrycia zakończyły się powodzeniem, ale ta sesja nie potwierdza wyniku audytu podatności zależności.
