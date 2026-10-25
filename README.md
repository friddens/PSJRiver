# PSJRiver – version 0.1.2

A read-only PowerShell provider for JRiver Media Center. Browse JRiver views with `cd` and `dir`, navigate into albums, and inspect track metadata. This initial version does not modify tags or start playback.

## Getting started

Run in Windows PowerShell 5.1 or PowerShell 7:

```powershell
# Run from the project folder after extracting or cloning it.
.\Start-JRiver.ps1
# Enter your MCWS username and password when prompted.

cd Music:\
dir

# Example navigation: view and album names depend on your JRiver library.
cd 'Rock Albums\A\AC%2FDC'
dir
cd '1980-07-25 - Back In Black'
dir
```

Alternatively, import the module and create a drive yourself:

```powershell
Import-Module .\JRiver\JRiver.psd1
$credential = Get-Credential
New-PSDrive -Name Music -PSProvider JRiver -Root '\' -Credential $credential
```

The default server address is `http://localhost:52199/MCWS/v1/`, and the drive starts at Audio (`RootId=1`). Credentials are not saved in the project. JRiver must be running with MCWS enabled.

## Reading metadata

```powershell
$songs = @(dir)
$songs | Select-Object 'Track #', Name, Artist, Album, Genre, SubGenre, InMyCollection
$songs[0] | Format-List *
$songs[0].'Album Rating'
$songs[0].'Date Purchased'

# Each track can also be addressed as an individual item.
Get-Item -LiteralPath $songs[0].PSPath | Format-List *
dir '*Hells*'
dir -Name
cd ..

# Choose columns for a particular listing.
$songs | Format-Table 'Track #', Name, Artist, Album, Genre -AutoSize
```

The default track table shows track number, name, artist, album, duration in seconds, and file key. Track requests use `Fields=Calculated`, so objects retain stored, custom, and calculated metadata returned by MCWS. Numeric values and dates remain in JRiver's original JSON format. `Filename` is the actual music file path; the provider path is a separate virtual address.

To change the default columns, edit `JRiver/JRiver.Format.ps1xml` and reload the format in your current session:

```powershell
# Run from the project folder.
Update-FormatData -PrependPath .\JRiver\JRiver.Format.ps1xml
```

## Date values

MCWS does not return every date field in the same numeric format when `Formatted=0`:

- Fields such as `Date` and `Date Original Release` can use OLE Automation dates: days since 1899-12-30, with the fractional part representing the time of day.
- Timestamp fields such as `Date Imported` can use Unix time: seconds since 1970-01-01 00:00:00 UTC. Convert these to the desired time zone after decoding them.
- Custom and calculated date fields can return other types, including formatted strings. Choose a conversion based on the actual field, rather than its name alone.

For example, the Unix timestamp `1657270041` represents `2022-07-08 08:47:21 UTC`. In Stockholm, daylight saving time makes this `2022-07-08 10:47:21`, matching the local date and time encoded by the internal OLE Automation value `44750.4495486111`.

```powershell
# Convert an OLE Automation date to a date string.
[datetime]::FromOADate(29427).ToString('yyyy-MM-dd')
# 1980-07-25

# Convert an imported timestamp to the local time zone of this computer.
[datetimeoffset]::FromUnixTimeSeconds(1657270041).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss zzz')

# Convert a track's imported timestamp to a DateTimeOffset object.
if ($null -ne $songs[0].'Date Imported') {
    [datetimeoffset]::FromUnixTimeSeconds([long]$songs[0].'Date Imported').ToLocalTime()
}
```

The provider preserves raw MCWS values. It does not automatically reinterpret dates or apply time zone conversions.

## Names and paths

`Name` contains the original name. Use `PathName` or `PSChildName` in a provider path. Slashes and PowerShell special characters are percent-encoded: `AC/DC` becomes `AC%2FDC`, `[` becomes `%5B`, `]` becomes `%5D`, and `%` becomes `%25`. Tab completion and `dir -Name` use these navigable names.

Track paths look like `01. Hells Bells~21001778`. The suffix is JRiver's file key, which makes tracks with identical titles distinguishable. Views with the same name under the same parent receive a similar ID suffix. Use `-LiteralPath` when addressing an item's `PSPath`.

## Wildcard matching

Wildcard paths match the original track or view name, without requiring the track-number prefix or file-key suffix:

```powershell
# Inside an album:
dir 'Hells*'
dir '*Bells'
dir '*love*'

# Inside an artist view, list albums whose names contain "Black":
dir '*Black*'
```

Matching is case-insensitive and supports PowerShell patterns such as `*`, `?`, and `[A-Z]`. Navigable path segments also match, so existing path-based patterns and tab completion remain usable. Use `-LiteralPath` with a complete `PSPath` to address one specific item without wildcard expansion.

## Configuration

```powershell
# Mount the full tree, including Audio, TV, and Playlists.
New-PSDrive AllMedia JRiver '\' -Credential $credential -RootId -1

# Use another MCWS server and disable the local cache.
New-PSDrive Other JRiver '\' -Credential $credential `
    -ServerUri 'https://media.example.com/MCWS/v1/' -CacheSeconds 0

# Refresh the current view, bypassing the local cache.
dir -Force

# Leave the drive before removing it.
cd $HOME
Remove-PSDrive Music
```

The provider uses `Browse/Children` and, when a view has no child views, `Browse/Files?Action=JSON`. Results are cached locally for 30 seconds per drive. `-Force` bypasses this local cache. Recursive listing is supported, but start with a small branch or `-Depth 1`: browsing an entire music library can require many requests.

JRiver browse IDs can change when its browse tree is rebuilt. Recreate the drive if you receive errors or unexpected results after such a change. After editing the C# source, start a new PowerShell session: loaded .NET types cannot be replaced in the same session.

## Validation

The optional live checks use a Back In Black example and can run in either supported PowerShell version. The default album path requires matching views in your own library:

```powershell
.\tests\Test-JRiver.Live.ps1 -Credential (Get-Credential)
```

Use `-AlbumPath` and `-ExpectedTrackCount` to specify a different album path and track count. The checks read the library and verify that deletion is rejected. They do not modify music files or tags.

## References

MCWS parameters were checked against the local [JRiver documentation](http://localhost:52199/MCWS/v1/doc). Module loading follows Microsoft's description of [dynamic binary modules](https://devblogs.microsoft.com/powershell/dynamic-binary-modules/).

## Version control

The source is hosted on [GitHub](https://github.com/friddens/PSJRiver). Clone the project with:

```powershell
git clone https://github.com/friddens/PSJRiver.git
cd PSJRiver
```

The module version is defined in `JRiver/JRiver.psd1`. Changes are recorded in [CHANGELOG.md](CHANGELOG.md), and released versions are marked with Git tags such as `v0.1.0`. This is an early, read-only version. PSJRiver is an independent project and is not an official JRiver product.

## Project language

Keep documentation, source comments, help text, examples, and project-authored messages in English for an international audience. Names and metadata returned by a user's JRiver library retain their original language.
