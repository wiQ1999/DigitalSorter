# Backlog — DigitalSorter

Zadania zgłoszone podczas lokalnych testów aplikacji na rzeczywistych danych, 17.10.2026. Kolejność odpowiada zgłoszeniu; priorytety nie zostały jeszcze ustalone. Wszystkie poniższe zadania mają status **Do realizacji**.

## BL-001 — Pokazywanie postępu odczytywania metadanych

Podczas odczytywania metadanych użytkownik powinien widzieć, że aplikacja pracuje, oraz postęp operacji.

**Kryteria akceptacji:**

- Wskaźnik jest widoczny od rozpoczęcia do zakończenia odczytu metadanych.
- Gdy znana jest liczba materiałów, aplikacja pokazuje liczbę przetworzonych materiałów względem całości lub odpowiadający jej procent.
- Gdy liczba materiałów nie jest jeszcze znana, aplikacja pokazuje informację o trwającym odczycie bez sugerowania nieznanego postępu procentowego.
- Zakończenie, anulowanie lub błąd operacji kończy wskazywanie postępu i daje odpowiednią informację.

## BL-002 — Szerszy panel wyboru folderów na komputerze

Panel boczny z wyborem folderów jest zbyt wąski w widoku komputerowym. Należy zwiększyć jego szerokość, aby nazwy folderów i kontrolki były czytelniejsze.

**Kryteria akceptacji:**

- W widoku komputerowym panel folderów jest szerszy niż obecnie.
- Układ pozostawia użyteczną przestrzeń dla bieżącego zdjęcia lub filmu.
- Zmiana nie pogarsza układu na telefonie ani obsługi Android TV.

## BL-003 — Ustalenie i usunięcie problemu odtwarzania części filmów MP4

Niektóre materiały wideo nie odtwarzają się i pokazują komunikat:

> **Nie można wyświetlić materiału**
>
> Plik jest niedostępny, uszkodzony lub jego kodek nie jest obsługiwany przez to urządzenie.

**Przykładowy plik:** `VID_20260816_110807.mp4`.

Przyczyna nie została jeszcze ustalona. Należy odtworzyć problem na wskazanym materiale i odróżnić problem pobierania, uprawnień lub obsługi żądań zakresowych od nieobsługiwanego kodeka albo uszkodzenia pliku. Rozszerzenie MP4 samo nie rozstrzyga przyczyny.

**Kryteria akceptacji:**

- Ustalono przyczynę problemu dla przykładowego pliku i zapisano warunki odtworzenia, w tym przeglądarkę i system.
- Jeżeli przyczyną jest błąd aplikacji, poprawka umożliwia odtwarzanie poprawnego, obsługiwanego materiału.
- Jeżeli przyczyną jest ograniczenie formatu lub kodeka, użytkownik otrzymuje możliwie konkretną informację zamiast nierozróżnialnego komunikatu o kilku potencjalnych przyczynach.
- Naprawa jest zweryfikowana na materiale odtwarzającym problem oraz na dotychczas działającym filmie.

## BL-004 — Liczba miniatur dopasowana do szerokości paska

Pasek podglądu kolejnych zdjęć i filmów powinien pokazywać tyle miniatur, ile mieści się w dostępnej szerokości na danym ekranie, zamiast ograniczać się do stałej liczby.

**Kryteria akceptacji:**

- Liczba widocznych miniatur wynika z szerokości paska i rozmiaru miniatur wraz z odstępami.
- Zmiana szerokości okna aktualizuje liczbę podglądów.
- Na szerokim ekranie pasek wykorzystuje dostępną przestrzeń, a na wąskim nie powoduje przepełnienia układu.
- Miniatury zachowują kolejność kolejnych materiałów; pod koniec kolekcji wyświetla się tylko dostępna liczba.

## BL-005 — Wyjaśnienie konfiguracji Google Cloud wymaganej przy łączeniu konta

Podczas podłączania własnego Google Drive użytkownik musiał dodać konto w panelu platformy Google. Należy wyjaśnić, które czynności są wykonywane jednorazowo dla aplikacji, a które są wymagane dla każdego podłączanego konta.

**Uwaga użytkownika:** jeżeli konfiguracja w panelu Google jest jednorazowa na aplikację, taki proces jest akceptowalny.

**Zakres i kryteria akceptacji:**

- Rozróżniono konfigurację projektu, włączenie Drive API i utworzenie klienta OAuth od dodawania kont do listy użytkowników testowych oraz zwykłego logowania i udzielania zgody.
- Wyjaśniono wymagania dla obecnego trybu testowego oraz docelowego sposobu udostępnienia aplikacji.
- Instrukcja jasno wskazuje, co wykonuje administrator aplikacji, a co użytkownik łączący swój Dysk.
- Jeżeli dodawanie każdego konta w panelu Google wynika z trybu testowego, opisano sposób obsługi kolejnych użytkowników w docelowym wariancie.
