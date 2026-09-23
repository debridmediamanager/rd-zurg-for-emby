# Releasing

## Before a tag

1. Bump `<Version>` in `src/Emby.Plugin.RdZurg/Emby.Plugin.RdZurg.csproj` and write the changelog.
2. `dotnet test tests/Emby.Plugin.RdZurg.Tests -c Release`, `./build.sh`,
   `python3 scripts/verify-package.py`.
3. Install the built DLL into an **isolated** Emby, never zen's live services, with a test account
   and its own programdata and port. The rig used so far is in [VALIDATION.md](VALIDATION.md).
4. Work through the checks below and record the measurements in `VALIDATION.md`. A check that was
   not run is written down as not run.
5. Wait for CI on the exact commit, then tag `v<version>`.

## The checks

**Settings**

- The settings page loads, saves and reloads. The token shows as a password and the signing key is
  not on the page.
- The signing key and the account id survive a restart.
- The plugin's name is never changed. Emby names the settings file after it, so a rename starts from
  empty settings and a new signing key, which invalidates every published URL.

**Sync**

- A bounded pass (torrent limit 25) publishes files and creates both libraries.
- A second pass writes nothing, rewrites nothing and leaves every file's timestamp alone.
- Lowering the torrent limit removes nothing.
- Changing the API token to another account rewrites every file exactly once, and the pass after
  that rewrites nothing.

**Cost**

- A library scan, a full metadata and image refresh, and the nightly tasks make zero requests to
  `/RdZurg/Stream`. Count them in Emby's log.

**Playback**

- Unsigned and tampered URLs give 401.
- HEAD, a byte range, a suffix range and an invalid range behave (200, 206, 206, 416).
- The same mid-file range twice gives identical bytes.
- ffprobe reads the file through the route, and ffmpeg can seek.
- A real player plays a film and an episode, and a stored-RAR release if one is to hand.

**Safety**

- An account that suddenly lists nothing does not empty the library: the pass refuses and says so.
- Files the plugin did not write are never touched.

## Distribution

Emby has no third-party plugin catalogue. A tag builds the DLL and its checksum, publishes them to
DMM's sponsor download page at `/emby`, and creates a **draft** GitHub release with the same two
files. Publishing that draft is what makes the release the one the README's install steps point at.
