# PSJRiver – version 0.1.0

En läsande PowerShell-provider för JRiver Media Center. Första versionen låter dig navigera i JRivers vyer med `cd` och `dir`, gå in i album och läsa låtarnas metadata. Den ändrar inga taggar och startar ingen uppspelning.

## Kom igång

Kör i Windows PowerShell 5.1 eller PowerShell 7:

```powershell
# Kör från projektmappen efter att du har packat upp eller klonat den.
.\Start-JRiver.ps1
# Ange MCWS-lösenordet i dialogen.

cd Music:\
dir
cd 'Rock Albums\A\AC%2FDC'
dir
cd '1980-07-25 - Back In Black'
dir
```

Alternativt skapar du enheten själv:

```powershell
Import-Module .\JRiver\JRiver.psd1
$credential = Get-Credential
New-PSDrive -Name Music -PSProvider JRiver -Root '\' -Credential $credential
```

Anslutningen går som standard till `http://localhost:52199/MCWS/v1/`, och enheten börjar i Audio (`RootId=1`). Lösenordet sparas inte i projektet. JRiver måste vara igång med MCWS aktiverat.

## Läsa metadata

```powershell
$songs = @(dir)
$songs | Select-Object 'Track #', Name, Artist, Album, Genre, SubGenre, InMyCollection
$songs[0] | Format-List *
$songs[0].'Album Rating'
$songs[0].'Date Purchased'

# Varje låt går även att adressera som ett enskilt objekt.
Get-Item -LiteralPath $songs[0].PSPath | Format-List *
dir '*Hells*'
dir -Name
cd ..
```

Standardlistan är kompakt, men objekten behåller alla metadatafält som MCWS returnerar, inklusive egna fält. Numeriska värden och datum lämnas i JRivers ursprungliga JSON-format. `Filename` är musikfilens verkliga sökväg; provider-sökvägen är en separat virtuell adress.

## Namn och sökvägar

`Name` är originalnamnet. `PathName` och `PSChildName` är namnet att använda i sökvägen. Snedstreck och PowerShell-specialtecken procentkodas: `AC/DC` blir `AC%2FDC`, `[` blir `%5B`, `]` blir `%5D`, och `%` blir `%25`. Tab-komplettering och `dir -Name` använder dessa sökbara namn.

Låtar får namn som `01. Hells Bells~21001778`; slutdelen är JRivers filnyckel och gör även lika låttitlar entydiga. Vyer med samma namn under samma förälder får ett motsvarande ID-suffix. Använd gärna `-LiteralPath` när du använder objektens `PSPath`.

## Andra inställningar

```powershell
# Hela trädet, inklusive Audio, TV och Playlists:
New-PSDrive AllMedia JRiver '\' -Credential $credential -RootId -1

# Annan MCWS-adress och utan lokal cache:
New-PSDrive Other JRiver '\' -Credential $credential `
    -ServerUri 'https://media.example.com/MCWS/v1/' -CacheSeconds 0

# Hämta om innehållet i aktuell vy:
dir -Force

# Avsluta enheten efter att ha lämnat den:
cd $HOME
Remove-PSDrive Music
```

Providern använder `Browse/Children` och, när en vy saknar undervyer, `Browse/Files?Action=JSON`. Innehåll mellanlagras i 30 sekunder per enhet. `-Force` kringgår denna lokala cache. Rekursiv listning stöds, men börja med en begränsad gren eller `-Depth 1` eftersom ett helt musikbibliotek kan innebära många anrop.

JRivers browse-ID:n kan ändras när dess vyträd byggs om. Skapa om enheten om du får fel eller oväntade resultat efter en sådan ändring. En ny PowerShell-session behövs när C#-koden ändras, eftersom inlästa .NET-typer inte kan ersättas i samma session.

## Verifiering

Livekontrollerna använder ett exempel med Back In Black och kan köras i båda PowerShell-versionerna. Sökvägen förutsätter motsvarande vyer i ditt eget bibliotek:

```powershell
.\tests\Test-JRiver.Live.ps1 -Credential (Get-Credential)
```

Albumets sökväg och förväntade antal spår kan ändras med `-AlbumPath` och `-ExpectedTrackCount`. Testerna läser biblioteket och verifierar även att skrivning avvisas. Inga musikfiler eller taggar ändras.

## Underlag

MCWS-parametrarna kontrollerades mot den lokala [JRiver-dokumentationen](http://localhost:52199/MCWS/v1/doc). Modulens inläsning följer Microsofts beskrivning av [dynamiska binärmoduler](https://devblogs.microsoft.com/powershell/dynamic-binary-modules/).

## Versionshantering

Källkoden finns på [GitHub](https://github.com/friddens/PSJRiver). Hämta projektet med:

```powershell
git clone https://github.com/friddens/PSJRiver.git
cd PSJRiver
```

Modulversionen anges i `JRiver/JRiver.psd1`. Ändringar dokumenteras i [CHANGELOG.md](CHANGELOG.md), och utgivna versioner märks med Git-taggar som `v0.1.0`. Första versionen är en tidig, läsande version. PSJRiver är ett fristående projekt och är inte en officiell JRiver-produkt.
