# Validation

What has actually been measured, and where. Anything not listed here is unproven.

## 1.0.0.0 - 2026-09-18, Emby 4.10.0.40 on zen

Rig: Docker `emby/embyserver:4.10.0.40`, container `emby-rd-poc`, port 18110, 1 GiB and 0.75 CPU,
programdata `/home/ben/emby-rd-poc/config`. Account: the Real-Debrid **test** account, torrent limit
25. zen's own Jellyfin, Plex and zurg were not touched.

**Install and settings**

- The plugin loads on Emby 4.10.0.40 (`Loading Emby.Plugin.RdZurg, Version=1.0.0.0`), targeting
  net8.0 against the 4.9.1.90 SDK. Emby 4.9.5.0 ships .NET 8.0.25 and 4.10.0.40 ships 8.0.28.
- It mints and saves its signing key on first load, reads the account id from `/user`
  (`publishing for Real-Debrid account …`) and creates both libraries.
- The settings page renders with the token as a password field and the signing key hidden; saving
  from the page keeps the hidden values.

**One pass over 25 torrents**

| | |
|---|---|
| First pass | 476 files written, 0 rewritten, 25 detail calls, 8 seconds |
| Second pass | 0 written, 0 rewritten, 476 unchanged, 0 deleted, 23 of 25 torrents recognised without a detail call |
| File timestamps after the second pass | identical, so Emby re-probes nothing |
| Ledger | 476 published, 28 ignored (the non-video files in those torrents) |

The two torrents re-queried on every pass are the ones whose file list and links disagree; they are
left alone deliberately rather than remembered as decided.

**What Emby made of it**

- 6 movies, 7 series, 252 episodes, scanned within 75 seconds of the sync.
- TMDB matched the films, including a Portuguese title (`Narciso Em Ferias` to `Narcissus Off Duty`)
  and both Blender films.
- Libraries carry realtime monitoring off and no `Image Capture` image fetcher.

**Cost safety**

- A library scan, and then a full metadata and image refresh of both libraries, made **zero**
  requests to the plugin's playback route.
- After a container restart, all 439 items keep their ids and the signing key and account id are
  unchanged.

**Playback**

| Check | Result |
|---|---|
| Unsigned URL | 401 |
| Forged signature | 401 |
| Signed HEAD | 200, `Content-Length: 121075730`, `video/mp4` |
| `Range: bytes=0-3` | 206, and the bytes are the file's own `ftyp` header |
| Suffix range `-100` | 206, `bytes 121075630-121075729/121075730` |
| Same 1 MB mid-file range twice | byte-identical |
| Invalid range | 416 |
| ffprobe through the route | `mov,mp4,m4a` 596.591 s, 121075730 bytes |
| ffmpeg seek to 30 s | succeeds |
| Emby web, direct play (mp4) | Big Buck Bunny played from Real-Debrid, 18.5 s in, no error |
| Emby web, transcode (mkv) | an X-Files episode played, 21.9 s in, no error |

## Not measured yet

- A native Emby app. Emby's shared playback code refuses direct play for remote sources, so clients
  should always go through the server, but only the web client has been watched doing it.
- A stored-RAR release end to end on Emby. The reader and its offset translation are covered by the
  ported unit tests, and the route serves ranges correctly, but no archive release was played here.
- Emby 4.9.5.0. Only 4.10.0.40 has run the plugin.
- A library larger than 25 torrents, and therefore the version cap and the cleanup path against a
  real account. Both are covered by the tests that replay the recorded 123-torrent account.
