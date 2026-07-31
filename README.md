# ScaleVoteBenchmark

Rješenje je ciljano na **.NET 10 (LTS, podrška do studenog 2028.)**. Potreban je .NET 10 SDK.

Rješenje sadrži tri projekta:

- **ScaleVoteBenchmark.Lib** — class library s modelima, repozitorijima (MSSQL i MySQL), predmemorijom i simulacijom opterećenja
- **ScaleVoteBenchmark.Api** — REST API, izdaje i validira JWT tokene, jedini komunicira s bazom podataka
- **ScaleVoteBenchmark.Web** — MVC prezentacijski sloj, sa slojem poslovne logike komunicira isključivo putem HTTP poziva prema ScaleVoteBenchmark.Api

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

## Deploy putem GitHub Actions (odvojeno za API i Web)

U `.github/workflows/` nalaze se dva odvojena workflowa:

- **`deploy-api.yml`** — gradi i deploya `ScaleVoteBenchmark.Api` na Azure App Service, aktivira se samo na promjene unutar `ScaleVoteBenchmark.Api/` ili `ScaleVoteBenchmark.Lib/`
- **`deploy-web.yml`** — gradi i deploya `ScaleVoteBenchmark.Web` na Azure App Service, aktivira se samo na promjene unutar `ScaleVoteBenchmark.Web/` ili `ScaleVoteBenchmark.Lib/`

Svaki workflow deploya na **svoj vlastiti App Service resurs** (odvojeno skaliranje, odvojen deploy ciklus), kako je i predviđeno arhitekturom rješenja.

### Priprema prije prvog pokretanja workflowa

1. Kreirati dva Azure App Service resursa (npr. `scalevotebenchmark-api` i `scalevotebenchmark-web`), s .NET 10 runtimeom
2. U oba `.yml` workflowa zamijeniti `YOUR-API-APP-SERVICE-NAME` / `YOUR-WEB-APP-SERVICE-NAME` stvarnim nazivima App Service resursa
3. Preuzeti *Publish Profile* za svaki App Service (Azure Portal → App Service → "Get publish profile") i spremiti ih u GitHub repozitoriju pod **Settings → Secrets and variables → Actions**:
   - `AZURE_WEBAPP_PUBLISH_PROFILE_API`
   - `AZURE_WEBAPP_PUBLISH_PROFILE_WEB`

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
| `AllowedWebOrigin` (samo API) | `AllowedWebOrigin` |
| `ApiBaseUrl` (samo Web) | `ApiBaseUrl` |

`AllowedWebOrigin` na API-ju treba pokazivati na stvarnu URL adresu deployanog Web App Servicea, a `ApiBaseUrl` na Webu treba pokazivati na stvarnu URL adresu deployanog API App Servicea — obje adrese poznate su tek nakon prvog deploya oba resursa.

## Priprema baze podataka

Pokrenuti odgovarajuću skriptu iz `sql/` direktorija na odabranoj bazi:

- `sql/mssql_schema.sql` za Azure SQL
- `sql/mysql_schema.sql` za Azure Database for MySQL

## Konfiguracija

Prije prvog pokretanja, u oba projekta potrebno je napraviti kopiju predloška:

```bash
cp ScaleVoteBenchmark.Api/appsettings.json.example ScaleVoteBenchmark.Api/appsettings.json
cp ScaleVoteBenchmark.Web/appsettings.json.example ScaleVoteBenchmark.Web/appsettings.json
```

Stvarna `appsettings.json` datoteka namjerno je u `.gitignore` (sadrži connection stringove i tajne), dok `appsettings.json.example` ostaje pod verzijskom kontrolom kao predložak.

U `ScaleVoteBenchmark.Api/appsettings.json` potrebno je postaviti:

- `DatabaseProvider` — `"MsSql"` ili `"MySql"`, bira koja se implementacija repozitorija koristi
- `ConnectionStrings:MsSql` i `ConnectionStrings:MySql` — connection stringovi za obje baze (nije potrebno da oba budu ispravna, koristi se samo onaj koji odgovara odabranom provideru)
- `Load:CpuIterationsPerVote` i `Load:MemoryMegabytesPerVote` — intenzitet umjetnog CPU i memorijskog opterećenja po glasu
- `Jwt:Key` — nasumični tajni ključ, minimalno 32 znaka
- `AdminUser:Username` / `AdminUser:Password` — kredencijali za administratorsku prijavu

U `ScaleVoteBenchmark.Web/appsettings.json` potrebno je postaviti:

- `ApiBaseUrl` — adresa na kojoj se pokreće ScaleVoteBenchmark.Api

Napomena: ScaleVoteBenchmark.Web namjerno nema connection stringove niti direktan pristup bazi podataka — cijela komunikacija s podatkovnim slojem odvija se preko ScaleVoteBenchmark.Api, čime su API i MVC dio potpuno odvojeni i mogu se zasebno pokretati, deployati i skalirati.

## Pokretanje

```bash
# Terminal 1 - API
cd ScaleVoteBenchmark.Api
dotnet run

# Terminal 2 - MVC
cd ScaleVoteBenchmark.Web
dotnet run
```

Potrebno je uskladiti port na kojem se pokreće ScaleVoteBenchmark.Api s vrijednošću `ApiBaseUrl` u `ScaleVoteBenchmark.Web/appsettings.json`, kao i s vrijednošću `AllowedWebOrigin` u `ScaleVoteBenchmark.Api/appsettings.json` (CORS).
