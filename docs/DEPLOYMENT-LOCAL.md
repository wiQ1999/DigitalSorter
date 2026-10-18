# Uruchomienie lokalne — Windows

Procedura dla przygotowanego laptopa i tego samego konta Windows. Wymaga lokalnego katalogu `artifacts/local` ze skryptami, opublikowaną aplikacją, SDK, certyfikatami i konfiguracją Google. Katalog nie jest wersjonowany; dla nowej instalacji zobacz [konfigurację środowiska](DEPLOYMENT.md).

## Start z rzeczywistym Google Drive

1. Otwórz PowerShell i wykonaj:

   ```powershell
   Set-Location D:\Projects\DigitalSorter\DigitalSorter
   .\artifacts\local\Start-GoogleDrive.ps1
   ```

   Backend i Web uruchomią się w tle.

2. Otwórz [https://localhost:7043](https://localhost:7043), zaloguj się w razie potrzeby, wybierz folder i kliknij **Otwórz kolekcję**.
3. Na TV uruchom DigitalSorter. Zapisany adres serwera: `https://192.168.0.222:7443`. Laptop i TV muszą być w tej samej sieci. Jeśli aplikacja wymaga parowania, zatwierdź kod na zalogowanym laptopie pod [https://localhost:7043/pair](https://localhost:7043/pair).

## Sprawdzenie i zatrzymanie

Sprawdź backend:

```powershell
Invoke-RestMethod https://localhost:7043/health
```

Oczekiwane: `status: ok`, `googleConfigured: True`. Druga wartość potwierdza konfigurację Google, a nie poprawność logowania.

Zatrzymaj backend:

```powershell
.\artifacts\local\Stop-DigitalSorter.ps1
```

Zamknięcie karty przeglądarki nie zatrzymuje backendu. Logi: `artifacts/local/google-server.log` i `artifacts/local/google-server-error.log`.

## Demo bez konta Google

W katalogu projektu wykonaj:

```powershell
.\artifacts\local\Stop-DigitalSorter.ps1
.\artifacts\local\Start-DigitalSorter.ps1
```

Otwórz [https://localhost:7043](https://localhost:7043) i wybierz **Połącz Google Drive → Zatwierdź dostęp testowy**. Ten wariant działa domyślnie tylko na laptopie, używa syntetycznych danych i kończy się przez `Ctrl+C`.

Skrypty uruchamiają ostatnią opublikowaną wersję z `artifacts/local/published`; po zmianach kodu potrzebna jest ponowna publikacja. Zmiana IP laptopa wymaga aktualizacji konfiguracji LAN, certyfikatu i reguły zapory.
