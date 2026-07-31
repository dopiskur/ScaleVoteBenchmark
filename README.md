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
| Azure.Identity | 1.11.3 |
| Microsoft.Identity.Client | 4.60.3 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 |

Napomena: verzija paketa `Microsoft.AspNetCore.Authentication.JwtBearer` vezana je uz verziju .NET runtimea (dio je ASP.NET Core dijeljenog frameworka), zbog čega je cijelo rješenje prebačeno na .NET 10 kako bi verzija 10.0.10 uopće bila korištiva. `Microsoft.Extensions.*` paketi nisu na isti način vezani uz runtime, ali se drže usklađenima radi jednostavnosti održavanja.

## Spajanje na Azure SQL — dva podržana načina

`ScaleVoteBenchmark.Api` podržava dva načina spajanja na Azure SQL, birana postavkom `UseManagedIdentity` u `appsettings.json`:

- **`false` (default) — connection string autentikacija.** Koristi se klasičan `SqlConnection` s korisničkim imenom i lozinkom iz `ConnectionStrings:MsSql`. Jednostavno za lokalni razvoj i scenarije izvan Azurea, ali **manje sigurno** jer lozinka baze ostaje zapisana u konfiguracijskoj datoteci u čitljivom obliku.
- **`true` — Managed Identity autentikacija.** Uz `Azure.Identity` paket, `MsSqlRepository` pribavlja privremeni pristupni token putem dodijeljenog Azure identiteta aplikacije, bez potrebe da lozinka uopće postoji u konfiguraciji. Funkcionira samo kada je aplikacija pokrenuta unutar Azure okruženja (App Service, VM) s dodijeljenim identitetom koji ima pristup Azure SQL bazi.

Oba načina koriste isti connection string za adresu poslužitelja i naziv baze; u Managed Identity modu se iz njega samo ignorira dio s korisničkim imenom i lozinkom. MySQL grana (`MySqlRepository`) trenutno podržava isključivo connection string autentikaciju.

## Priprema baze podataka

Pokrenuti odgovarajuću skriptu iz `sql/` direktorija na odabranoj bazi:

- `sql/mssql_schema.sql` za Azure SQL
- `sql/mysql_schema.sql` za Azure Database for MySQL

## Konfiguracija

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
