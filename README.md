# ScaleVoteBenchmark

Rješenje je ciljano na **.NET 10 (LTS, podrška do studenog 2028.)**. Potreban je .NET 10 SDK.

Rješenje sadrži dva projekta:

- **ScaleVoteBenchmark.Lib** — class library s modelima, repozitorijima (MSSQL i MySQL), predmemorijom i simulacijom opterećenja
- **ScaleVoteBenchmark.Api** — REST API, izdaje i validira JWT tokene, jedini komunicira s bazom podataka

Aplikacija nema korisničko sučelje — namjerno se koristi isključivo kao API koji vanjska skripta za generiranje opterećenja poziva izravno (npr. `POST /api/vote/add?option=yes` ili `?option=no`, nasumično birano po pozivu). Svaki poziv upisuje glas u bazu i usput generira umjetno CPU/memorijsko opterećenje čiji je intenzitet definiran u `appsettings.json` (`Load:CpuIterationsPerVote`, `Load:MemoryMegabytesPerVote`) — to je svrha benchmarka. Rezultati (`GET /api/vote/counts`, zaštićeno JWT-om) kasnije se mogu očitati kao statistika direktno iz baze ili preko tog endpointa.

## Verzije paketa

| Paket | Verzija |
| --- | --- |
| Microsoft.Data.SqlClient | 7.0.2 |
| MySqlConnector | 2.6.1 |
| Microsoft.Extensions.Caching.Memory | 10.0.10 |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.0 |
| Azure.Identity | 1.21.0 |
| Microsoft.Identity.Client | 4.87.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 |

Napomena: verzija paketa `Microsoft.AspNetCore.Authentication.JwtBearer` vezana je uz verziju .NET runtimea (dio je ASP.NET Core dijeljenog frameworka), zbog čega je cijelo rješenje prebačeno na .NET 10 kako bi verzija 10.0.10 uopće bila korištiva. `Microsoft.Extensions.*` paketi nisu na isti način vezani uz runtime, ali se drže usklađenima radi jednostavnosti održavanja.

## Spajanje na Azure SQL — dva podržana načina

`ScaleVoteBenchmark.Api` podržava dva načina spajanja na Azure SQL, birana postavkom `UseManagedIdentity` u `appsettings.json`:

- **`false` (default) — connection string autentikacija.** Koristi se klasičan `SqlConnection` s korisničkim imenom i lozinkom iz `ConnectionStrings:MsSql`. Jednostavno za lokalni razvoj i scenarije izvan Azurea, ali **manje sigurno** jer lozinka baze ostaje zapisana u konfiguracijskoj datoteci u čitljivom obliku.
- **`true` — Managed Identity autentikacija.** Uz `Azure.Identity` paket, `MsSqlRepository` pribavlja privremeni pristupni token putem dodijeljenog Azure identiteta aplikacije, bez potrebe da lozinka uopće postoji u konfiguraciji. Funkcionira samo kada je aplikacija pokrenuta unutar Azure okruženja (App Service, VM) s dodijeljenim identitetom koji ima pristup Azure SQL bazi.

Oba načina koriste isti connection string za adresu poslužitelja i naziv baze; u Managed Identity modu se iz njega samo ignorira dio s korisničkim imenom i lozinkom. MySQL grana (`MySqlRepository`) trenutno podržava isključivo connection string autentikaciju.

## Provjera dostupnosti baze pri pokretanju

`ScaleVoteBenchmark.Api` pri svakom pokretanju odmah pokušava otvoriti konekciju prema konfiguriranoj bazi podataka (bez izvršavanja upita), prije nego što počne primati HTTP zahtjeve. Ponašanje pri neuspjehu bira se postavkom `Startup:FailFastOnDbCheck`:

- **`true` (default)** — aplikacija se odmah zaustavlja uz jasnu grešku u konzoli/logu ako baza nije dostupna (kriva lozinka, pogrešan poslužitelj, zatvoren firewall na Azureu)
- **`false`** — greška se samo zapisuje kao kritični log zapis, a aplikacija nastavlja raditi; korisno ako želiš da API ostane dostupan (npr. za health-check endpoint) i dok baza privremeno ne radi

Bez ove provjere, pogrešna konfiguracija baze inače se ne bi primijetila sve do prvog stvarnog glasa ili dohvaćanja rezultata (`SqlRepository`/`MySqlRepository` otvaraju konekciju tek "lijeno", kod stvarnog poziva, a ne pri pokretanju aplikacije).

## Deploy putem GitHub Actions

U `.github/workflows/` nalazi se workflow `deploy-api.yml` koji gradi i deploya `ScaleVoteBenchmark.Api` na Azure App Service, a aktivira se na promjene unutar `ScaleVoteBenchmark.Api/`, `ScaleVoteBenchmark.Lib/` ili samog workflowa.

### Priprema prije prvog pokretanja workflowa

1. Kreirati Azure App Service resurs (npr. `scalevotebenchmark-api`), s .NET 10 runtimeom
2. U `deploy-api.yml` zamijeniti `YOUR-API-APP-SERVICE-NAME` stvarnim nazivom App Service resursa
3. Preuzeti *Publish Profile* (Azure Portal → App Service → "Get publish profile") i spremiti ga u GitHub repozitoriju pod **Settings → Secrets and variables → Actions** kao `AZURE_WEBAPP_PUBLISH_PROFILE_API`

### appsettings.json na Azureu — Application Settings, ne datoteka

Budući da je `appsettings.json` namjerno u `.gitignore` (sadrži tajne), **ne postoji u repozitoriju niti u onome što se deploya**. Umjesto toga, sve postavke treba postaviti kao **Application Settings** u Azure Portalu (App Service → Configuration), gdje se ugniježđeni ključevi pišu s dvostrukom podvlakom `__`:

| appsettings.json ključ | Application Setting naziv |
| --- | --- |
| `DatabaseProvider` | `DatabaseProvider` |
| `UseManagedIdentity` | `UseManagedIdentity` |
| `ConnectionStrings:MsSql` | `ConnectionStrings__MsSql` |
| `ConnectionStrings:MySql` | `ConnectionStrings__MySql` |
| `Jwt:Key` | `Jwt__Key` |
| `Jwt:Issuer` | `Jwt__Issuer` |
| `Jwt:Audience` | `Jwt__Audience` |
| `Jwt:ExpirationMinutes` | `Jwt__ExpirationMinutes` |
| `AdminUser:Username` | `AdminUser__Username` |
| `AdminUser:Password` | `AdminUser__Password` |
| `Load:CpuIterationsPerVote` | `Load__CpuIterationsPerVote` |
| `Load:MemoryMegabytesPerVote` | `Load__MemoryMegabytesPerVote` |
| `Startup:FailFastOnDbCheck` | `Startup__FailFastOnDbCheck` |

## Priprema baze podataka

Pokrenuti odgovarajuću skriptu iz `sql/` direktorija na odabranoj bazi:

- `sql/mssql_schema.sql` za Azure SQL
- `sql/mysql_schema.sql` za Azure Database for MySQL

## Konfiguracija

Prije prvog pokretanja potrebno je napraviti kopiju predloška:

```bash
cp ScaleVoteBenchmark.Api/appsettings.json.example ScaleVoteBenchmark.Api/appsettings.json
```

Stvarna `appsettings.json` datoteka namjerno je u `.gitignore` (sadrži connection stringove i tajne), dok `appsettings.json.example` ostaje pod verzijskom kontrolom kao predložak.

U `ScaleVoteBenchmark.Api/appsettings.json` potrebno je postaviti:

- `DatabaseProvider` — `"MsSql"` ili `"MySql"`, bira koja se implementacija repozitorija koristi
- `ConnectionStrings:MsSql` i `ConnectionStrings:MySql` — connection stringovi za obje baze (nije potrebno da oba budu ispravna, koristi se samo onaj koji odgovara odabranom provideru)
- `Load:CpuIterationsPerVote` i `Load:MemoryMegabytesPerVote` — intenzitet umjetnog CPU i memorijskog opterećenja po glasu
- `Jwt:Key` — nasumični tajni ključ, minimalno 32 znaka
- `AdminUser:Username` / `AdminUser:Password` — kredencijali za administratorsku prijavu

## Pokretanje

```bash
cd ScaleVoteBenchmark.Api
dotnet run
```

Glasovi se generiraju izravnim pozivima na `POST /api/vote/add?option=yes` ili `POST /api/vote/add?option=no` (anonimni pristup, bez potrebe za JWT tokenom) — npr. skriptom za load testing koja nasumično bira `yes`/`no` po pozivu. Zbrojeni rezultati dostupni su na `GET /api/vote/counts` (zahtijeva JWT dobiven putem `POST /api/auth/login`), a mogu se i izravno očitati iz tablice `Vote` u bazi.
