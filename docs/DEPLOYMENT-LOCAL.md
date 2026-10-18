# Uruchomienie lokalne — Windows

Procedura dla przygotowanego laptopa i tego samego konta Windows. Wymaga lokalnego katalogu `artifacts/local` ze skryptami, opublikowaną aplikacją, SDK, certyfikatami i konfiguracją Google. Katalog nie jest wersjonowany; dla nowej instalacji zobacz [konfigurację środowiska](DEPLOYMENT.md).

## Start z rzeczywistym Google Drive

1. Otwórz PowerShell w katalogu repozytorium i wykonaj:

   ```powershell
   .\artifacts\local\Start-GoogleDrive.ps1
   ```

   Backend i Web uruchomią się w tle.

2. Otwórz [https://localhost:7043](https://localhost:7043), zaloguj się w razie potrzeby, wybierz folder i kliknij **Otwórz kolekcję**.
3. Na TV uruchom DigitalSorter i użyj adresu serwera z lokalnej konfiguracji LAN. Laptop i TV muszą być w tej samej sieci. Jeśli aplikacja wymaga parowania, zatwierdź kod na zalogowanym laptopie pod [https://localhost:7043/pair](https://localhost:7043/pair).

Jeżeli przygotowano dostęp TV przez LAN, bieżący adres i port można odczytać z katalogu repozytorium:

```powershell
Get-Content .\artifacts\local\tv\lan-settings.json -Raw |
    ConvertFrom-Json | Select-Object Address, Port
```

Adres wpisywany na TV ma postać `https://<Address>:<Port>`. Brak tego pliku oznacza, że trzeba najpierw przygotować [konfigurację LAN i certyfikat](DEPLOYMENT.md#qr-przez-lokalny-adres-ip).

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
