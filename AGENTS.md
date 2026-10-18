# Instrukcje dla agenta AI — DigitalSorter

Ten plik zawiera instrukcje pracy dla agenta AI. Opis produktu, architektura, konfiguracja i procedury użytkownika mają swoje źródło w dokumentacji wskazanej w [indeksie](docs/README.md); nie kopiuj ich tutaj.

## Commity i branche

Każdy większy, logicznie spójny etap pracy kończ commitem, korzystając ze skilla `conventional-commit` (`C:/Users/wikto/.agents/skills/conventional-commit/SKILL.md`). Nie odkładaj wszystkich zmian do jednego commita na końcu zadania. Większe etapy najlepiej realizuj na osobnym feature branchu, utworzonym przed rozpoczęciem zmian; domyślny prefiks brancha to `codex/`.

Jawna prośba użytkownika, aby nie commitować lub odłożyć commit, ma pierwszeństwo przed powyższą regułą. W takim przypadku pozostaw zmiany do przeglądu bez tworzenia commita.

## Praca z dokumentacją

Przed zmianą odczytaj dokument właściwy dla zadania, korzystając z indeksu. Wymagania i granice produktu sprawdzaj w [wymaganiach produktowych](docs/PRODUCT_REQUIREMENTS.md), zależności między modułami w [architekturze](docs/ARCHITECTURE.md), a zasady weryfikacji w [dokumentacji testów](docs/TESTING.md).

Instrukcje dotyczące zachowania agenta i nawigowania po repozytorium zapisuj tutaj. Informacje potrzebne użytkownikom, administratorom i programistom umieszczaj w `docs/`, w dokumencie danego tematu. `README.md` ma przedstawiać projekt, ułatwiać pierwsze uruchomienie i prowadzić do szczegółów. Przy dodaniu lub przeniesieniu dokumentu aktualizuj indeks i odnośniki zamiast powielać treść.

Wyniki sprawdzeń zapisuj jako raport z datą, zakresem i ograniczeniami. Historyczny raport nie potwierdza stanu bieżących zmian; kończąc zadanie, podaj faktycznie wykonane sprawdzenia. Backlog opisuje zadania produktowe, a nie plan działania agenta w pojedynczej sesji.

## Lokalne środowisko

Przed użyciem plików z ignorowanego `artifacts/local/` sprawdź ich obecność i przeczytaj [procedurę przygotowanego laptopa](docs/DEPLOYMENT-LOCAL.md). Nie zakładaj, że te pliki, adres LAN lub konfiguracja konta Windows istnieją w innym checkoutcie. Nie przenoś lokalnych poświadczeń, kluczy ani prywatnych identyfikatorów mediów do pamięci lub dokumentacji.
