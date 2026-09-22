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

The two torrents re-queried on every pass were not what this said. One is a torrent whose file list and
links disagree (Real-Debrid packed its 4,452 files into one link), left alone deliberately; the other was
a film carrying a second video, which nothing remembered. The audit build below fixes the second.

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

## Audit build - 2026-09-22, same rig

Built from the `audit-2026-09-22` branch (DLL sha256 `4d01d48b…020f0`) and installed over the 1.0.0.0
build above, keeping its ledger and tree. The account now holds 3,365 torrents.

| | |
|---|---|
| First pass, limit 25, over the old ledger | 0 written, 0 rewritten, 476 unchanged, 2 detail calls (recording the extras) |
| Second pass, limit 25 | 0 written, 0 rewritten, 1 detail call: the packed torrent |
| Limit raised to 440 | 416 detail calls, 757 written, 31 copies held back, 13 capped, 129 s |
| Second pass at 440 | 0 written, 0 rewritten, 1,233 unchanged, 4 detail calls, then 3 on the pass after |

The three torrents still read on every pass at 440 are all ones whose file list and links disagree
(`music`, `Big Buck Bunny`, `Sintel`). The fourth, once, was a copy of a capped Matrix release being
converted to the new capped state.

On zen's case-sensitive filesystem "House of the Dragon" (2022, S02E06) and "House Of The Dragon" (S01E09)
published into **one** series folder, both episode files named after it.

The limit-440 tree was a probe: the container was stopped before Emby's refresh fired, and the tree and
ledger were restored to the limit-25 state afterwards. Emby never scanned the extra 757 files.

Playback through the route, on Big Buck Bunny: unsigned 401, tampered signature 401, HEAD 200 with the full
length, `bytes=0-3` 206, suffix `-100` 206, open-ended `1000-` 206, a range past the end 416, a multi-range
416, the same 1 MB mid-file range twice byte-identical, and Emby's own ffprobe through the route reads
`mov,mp4` 596.591 s.

Not run on this build: a real player, a stored-RAR release, and an account switch.

## Not measured yet

- A native Emby app. Emby's shared playback code refuses direct play for remote sources, so clients
  should always go through the server, but only the web client has been watched doing it.
- A stored-RAR release end to end on Emby. The reader and its offset translation are covered by the
  ported unit tests, and the route serves ranges correctly, but no archive release was played here.
- Emby 4.9.5.0. Only 4.10.0.40 has run the plugin.
- A library larger than 25 torrents, and therefore the version cap and the cleanup path against a
  real account. Both are covered by the tests that replay the recorded 123-torrent account.
