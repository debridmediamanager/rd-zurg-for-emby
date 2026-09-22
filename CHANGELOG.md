# Changelog

## 1.0.0.0 - unreleased

First build. Publishes a Real-Debrid account as two Emby libraries of `.strm` files and serves
playback from a signed route inside Emby.

- Episode and title parsing vendored from the MIT-licensed original Emby.Naming, held to the same
  11,069-row corpus and the same title and series output as the Jellyfin plugins.
- A ledger keeps every published path fixed, because in Emby a path is the item's identity.
- Playback: signed URLs, byte ranges, HEAD, stored-RAR members, and a CDN answer that is checked
  before any header is sent.
- Libraries are created with everything that would open a stream turned off.

### Fixed since the first validation build (`fb0d022`)

- A resolution is no longer read as a year: `[BDRip 1920x1080 HEVC]` filed a release as a 1920 film, a folder
  Emby matches nothing against.
