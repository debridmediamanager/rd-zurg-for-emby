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
- Titles that differ only in case ("One More Shot" and "One more shot") publish into one folder spelling, the one
  already on disk if there is one. Emby groups versions only when each file starts with the folder's name.
- A pass over an unchanged account asks Real-Debrid about nothing. The videos a torrent holds besides its film
  or its episodes (featurettes, samples, a collection's other films) were remembered nowhere, so every such
  torrent cost a detail call on every pass.
- A copy of an episode is published once. Real-Debrid now gives a re-added release fresh link keys, so only the
  name and size recognise a copy; films were checked that way, episodes were not, and each copy became one more
  version of the same episode.
- A release held back comes back when the reason is gone. A copy held back because the same release was
  already published takes its place, in place, when that torrent is removed; and when one of a film's eight
  versions goes, the largest release the cap held back fills the gap. Both used to be written off for good.
- A folder never holds more than eight versions. A pass that may not delete (a torrent limit, or "Remove
  vanished items" off) left a vanished version's file in place but did not count it, so a new release made
  nine and Emby stopped grouping the film.
