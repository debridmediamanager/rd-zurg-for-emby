# RD zurg for Emby

Your Real-Debrid library in Emby, with no mount, no rclone and no second service. The plugin
publishes your account as two Emby libraries and serves playback from inside Emby itself.

Built for **Emby 4.9 and 4.10** (both run .NET 8). It is a sibling of the Jellyfin plugins and
shares their provider code, but Emby and Jellyfin have different plugin APIs, so this is its own
build.

## Install

1. Stop Emby and back up its programdata directory.
2. Copy `Emby.Plugin.RdZurg.dll` into `<programdata>/plugins/`. Verify the adjacent SHA-256 file.
3. Start Emby. Open **Settings - Plugins - RD zurg**.
4. Enter the private token from [Real-Debrid](https://real-debrid.com/apitoken) and save.
5. Run **Settings - Scheduled Tasks - Sync Real-Debrid library**. It then runs every six hours.

For the Docker image the programdata directory is `/config`; native installs commonly use
`/var/lib/emby`.

## How it works

The plugin writes one `.strm` file per playable file into `<programdata>/data/rd-zurg/`, and points
two libraries at it. Each file holds a signed URL to the plugin's own playback route, which resolves
a fresh Real-Debrid link when something plays. Emby does the scanning, the metadata and the version
grouping, so the library behaves like any other Emby library.

Nothing opens a stream except playback. A library scan, a full metadata refresh and every nightly
task cost zero Real-Debrid calls, which is why the libraries are created with frame-grabbing image
extraction, chapter images, marker detection and realtime monitoring turned off.

| Setting | What it does |
|---|---|
| API token | A private API token. OAuth renewal is not implemented. |
| Movie library, Show library | Names used when the libraries are first created. Rename them in Emby afterwards. |
| Torrent limit | `0` publishes the whole account. A positive limit takes the newest torrents and removes nothing. |
| Remove vanished items | Removes files only after a complete listing of the account. Nothing on Real-Debrid is ever deleted. |
| Look inside RAR archives | Plays complete, unencrypted, stored video members of single-volume archives. |
| Redirect plain files | Advanced. Hands Emby the provider URL instead of copying the bytes through the plugin. Archives always pass through. |
| Allow large cleanups | Advanced. Off, a pass that would remove most of the library is refused instead. |
| Server address override | Advanced. Leave empty. Emby's own ffmpeg reads these URLs and cannot resolve host names, so the default is the loopback address. |

## What it does not do

- Real-Debrid only, and no torrent management: nothing is added, repaired or deleted on the account.
- Absolute-numbered anime and ambiguous release names can still be filed wrongly. The parser is held
  to 11,069 real release names, and the ones it gets wrong are the ones the Jellyfin plugins get
  wrong too.
- A film folder shows at most eight versions, because past eight Emby stops grouping them and shows
  every release as its own film. The largest eight are kept.
- Renaming or deleting the plugin's libraries in Emby does not delete the files it wrote. Remove the
  `rd-zurg` directory as well if you want them gone.

## Build

Needs the .NET 10 SDK to build, though the plugin itself targets .NET 8, which is what Emby runs.

```bash
dotnet test tests/Emby.Plugin.RdZurg.Tests -c Release
./build.sh                       # writes artifacts/rd-zurg-for-emby_<version>/
python3 scripts/verify-package.py
./build.sh /var/lib/emby         # also installs into <programdata>/plugins
```

See [docs/RELEASING.md](docs/RELEASING.md) for the checks a release has to pass and
[docs/VALIDATION.md](docs/VALIDATION.md) for what has actually been measured.
