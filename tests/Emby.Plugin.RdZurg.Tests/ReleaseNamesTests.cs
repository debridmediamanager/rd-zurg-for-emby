using Emby.Plugin.RdZurg.Library;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class ReleaseNamesTests
{
    private readonly ReleaseNames _names = new();

    [Theory]
    [InlineData("Sitio.do.Picapau.Amarelo.S03", "/Sitio.do.Picapau.Amarelo.S03E83.1080p.GLBO.WEB.DL.mkv", 3, 83, "Sitio.do.Picapau.Amarelo")]
    [InlineData("Game.of.Thrones.S08.UHD", "/Game.of.Thrones.S08E03.The.Long.Night.2160p.mkv", 8, 3, "Game.of.Thrones")]
    [InlineData("Teen.Wolf.S04.1080p", "/Teen.Wolf.S04.1080p/Teen.Wolf.S04E12.Smoke.and.Mirrors.mkv", 4, 12, "Teen.Wolf")]
    public void ReadsEpisodesOutOfReleaseNames(string torrent, string file, int season, int episode, string series)
    {
        var parsed = _names.ParseEpisode(torrent, file);

        Assert.NotNull(parsed);
        Assert.Equal(season, parsed!.SeasonNumber);
        Assert.Equal(episode, parsed.EpisodeNumber);
        Assert.Equal(series, parsed.SeriesName);
    }

    /// <summary>
    /// The resolver's optimistic expressions read a release year as a season and episode pair, which
    /// turns a shelf of films into a shelf of one-episode shows. These must not parse.
    /// </summary>
    [Theory]
    [InlineData("The.Matrix.Reloaded.2003.2160p.UHD.BluRay", "/The.Matrix.Reloaded.2003.2160p.UHD.BluRay.mkv")]
    [InlineData("Interstellar.2014.2160p.IMAX.REMUX", "/Interstellar.2014.2160p.IMAX.REMUX.mkv")]
    [InlineData("The Matrix 4K", "/The Matrix 4K.mkv")]
    public void DoesNotMistakeAFilmForAnEpisode(string torrent, string file)
    {
        Assert.Null(_names.ParseEpisode(torrent, file));
    }

    /// <summary>
    /// Films that Jellyfin's non-optimistic expressions read as an episode. The bare <c>([0-9]+)-([0-9]+)</c>
    /// expression reads an audio or frame-rate tag (<c>AC3-2.0</c>, <c>5.1-4K4U</c>, <c>4.17-60fps</c>,
    /// <c>2.0-12GaugeShotgun</c>), a resolution (<c>2026-1080p</c>) or a collection's year range
    /// (<c>1999-2021</c>) as a season and episode, and an <c>NxNN</c> expression reads <c>5.1x265</c> as season
    /// 1 episode 265. The two Matrix packs, The Odyssey and BTCC were filed as shows in zen's RD library; the
    /// rest come from the Offcloud test account and DMM's RD and AllDebrid availability tables.
    /// </summary>
    [Theory]
    [InlineData("Matrix - The Matrix (1999-2021) KOLEKCJA.MULTi.2160p.UHD.BluRay.REMUX.DV.HDR.HEVC.TrueHD.7.1-MR ~ Lektor i Napisy PL", "/2.Matrix Reaktywacja (2003)/The.Matrix.Reloaded.2003.MULTi.2160p.UHD.BluRay.REMUX.DV.HDR.HEVC.TrueHD.7.1-MR.mkv")]
    [InlineData("The Matrix Complete 5 Movie Collection - Sci-Fi 1999-2021 Eng Rus Multi-Subs 1080p [H264-mp4]", "/The Matrix Complete 5 Movie Collection 1999 - 2021/02 The Matrix Reloaded Remastered - Sci-Fi 2003 Eng Rus Multi-Subs 1080p [H264-mp4].mp4")]
    [InlineData("The-Odyssey-2026-1080p-TS-V2-WEB.DL-GP-M-NLsubs {imdb-tt33764258}", "/The-Odyssey-2026-1080p-TS-V2-WEB.DL-GP-M-NLsubs.mp4")]
    [InlineData("BTCC.2026.Round13-15.Thruxton.Sunday.Coverage.ITVX.WEB.DL.1080p.h264.English-MWR", "/BTCC.2026.Round13-15.Thruxton.Sunday.Coverage.ITVX.WEB.DL.1080p.h264.English-MWR.mkv")]
    [InlineData("www.UIndex.org    -    Predator.Badlands.2025.1080p.CAM.v2.HEVC.AC3-2.0.EngHardSubbed-RypS", "/Predator.Badlands.2025.1080p.CAM.v2.HEVC.AC3-2.0.EngHardSubbed-RypS.mkv")]
    [InlineData("70+ Favorite Christmas Movies", "/82 - 85, 87 - 90/84 - Ernest.Saves.Christmas.1988.DVDRip.XviD.AC3-777.avi")]
    [InlineData("Spider Man Trilogy (2002-2007)", "/Spiderman 2 (2004)/Spider.Man.2.2004.720p.BrRip.264.YIFY.mp4")]
    [InlineData("Scary Movie 1, 2, 3, 4, 5 - Complete Horror Collection 2000-2013 Eng Subs 1080p [H264-mp4]", "/Scary Movie Collection Comedy Horror 2000-2013/01 Scary Movie - Comedy Horror 2000 Eng Subs 1080p [H264-mp4].mp4")]
    [InlineData("Eyes.Wide.Shut.1999.2160p.Ai-Upscaled.10Bit.HEVC.DTS-HD.MA.5.1-RIFE.4.17-60fps.DirtyHippie", "/Eyes.Wide.Shut.1999.2160p.Ai-Upscaled.10Bit.HEVC.DTS-HD.MA.5.1-RIFE.4.17-60fps.DirtyHippie.mkv")]
    [InlineData("Interstellar.2014.2160p.DV.HDR10.Ai-Enhanced.H265.DTS-HD.5.1-RIFE.4.15-60fps-DirtyHippie", "/Interstellar.2014.2160p.DV.HDR10.Ai-Enhanced.H265.DTS-HD.5.1-RIFE.4.15-60fps-DirtyHippie.mkv")]
    [InlineData("Alien Movie Collection - 9 Films DC SE Unrated 1979-2017 Eng Subs 720p [H264-mp4]", "/Alien 9 Movie Extended Collection/04 Alien Resurrection Special Extended - Sci-Fi 1997 Eng Subs 720p [H264-mp4].mp4")]
    [InlineData("Avengers End Game  2019 1080p  WEB-Rip X264 AC3 - 5-1 KINGDOM-RG", "/Avengers End Game  2019 1080p  WEB-Rip X264 AC3 - 5-1 KINGDOM-RG.mkv")]
    [InlineData("28.Days.Later.28.Weeks.Later.Duology.2002-2007.Blu-Ray.1080p.AVC.REMUX.DTS-HDMA.5.1", "/28.Days.Later.2002.Blu-Ray.1080p.AVC.ReMuX.DTS-HDMA.5.1-R2D2/28.Days.Later.2002.Blu-Ray.1080p.AVC.ReMuX.DTS-HDMA.5.1-R2D2.mkv")]
    [InlineData("Marvel Cinematic Film Collection (2008-2019) 23 Movie Set x264 720p Esub BluRay Dual Audio English Hindi GOPI SAHI", "/17 Thor Ragnarok 2017  IMAX Edition GOPI SAHI.mkv")]
    [InlineData("Weapons.2025.bluray.hdr.2160p.av1-7.1.opus-Dust", "/Weapons.2025.bluray.hdr.2160p.av1-7.1.opus-Dust.mkv")]
    [InlineData("The Hunger Games Complete Movie Collection - Sci-Fi 2012-2015 Eng Rus Multi-Subs 720p [H264-mp4]", "/The Hunger Games Complete Movie Collection 2012-2015/01 The Hunger Games - Sci-Fi 2012 Eng Rus Multi-Subs 720p [H264-mp4].mp4")]
    [InlineData("The.Shawshank.Redemption.1994.2160p.UHD.Blu-Ray.DV.HEVC.HDR.DTS-HD.MA.5.1.DTS.XLL.5.1x265-E", "/The.Shawshank.Redemption.1994.2160p.UHD.Blu-Ray.DV.HEVC.HDR.DTS-HD.MA.5.1.DTS.XLL.5.1x265-E.mkv")]
    [InlineData("Jeepers.Creepers.2001.REMASTERED.1080p.Bluray.REMUX.AVC.DTS-HD.MA.5.1-4K4U", "/Jeepers.Creepers.2001.REMASTERED.1080p.Bluray.REMUX.AVC.DTS-HD.MA.5.1-4K4U.mkv")]
    [InlineData("Crocodile.Dundee.II.1988.2160p.UHD.Blu-ray.REMUX.DV.HDR.HEVC.FLAC.2.0-12GaugeShotgun.mkv", "/Crocodile.Dundee.II.1988.2160p.UHD.Blu-ray.REMUX.DV.HDR.HEVC.FLAC.2.0-12GaugeShotgun.mkv")]
    [InlineData("Life.of.Crime.1984-2020.2021.1080p.WEBRip.x264-RARBG.mp4", "/Life.of.Crime.1984-2020.2021.1080p.WEBRip.x264-RARBG.mp4")]
    public void DoesNotReadATagOrYearRangeAsAnEpisode(string torrent, string file)
    {
        Assert.Null(_names.ParseEpisode(torrent, file));
    }

    /// <summary>
    /// Episodes the same change must keep, all real names: an <c>NxNN</c> marker right after a year, a
    /// four-digit season, a season after <c>Formula.1.</c>, <c>Blakes.7.1x01</c> and <c>Hawaii.5-0.7x01</c>
    /// whose titles end in a digit, single-digit <c>5X3</c> and <c>T5x3</c>,
    /// <c>Season 4 E14</c>, and <c>SxxEyy</c> files that carry <c>5.1x265</c> or a range in the pack name.
    /// </summary>
    [Theory]
    [InlineData("A.Series.of.Unfortunate.Events.2017.Season.1.S01.WEBRip.1080p.x265.5.1Ch.HAAC-KITE-METeam", "/A.Series.of.Unfortunate.Events.2017.1x01.WEBRip.1080p.x265-KITE-METeam.mkv", 1, 1)]
    [InlineData("MythBusters 1080p", "/MythBusters 2006/MythBusters.2006x19.Mega.Movie.Myths.1080p.Rus.Eng.mkv", 2006, 19)]
    [InlineData("Formula.1.2024x27.Round.05.ChineseGP.Qualifying.International.MULTi.1080p.SS.mkv", "/Formula.1.2024x27.Round.05.ChineseGP.Qualifying.International.MULTi.1080p.SS.mkv", 2024, 27)]
    [InlineData("Blakes.7.(1978).Complete-Z0DiAC99", "/Season 1/Blakes.7.1x01.The.Way.Back.avi", 1, 1)]
    [InlineData("Hawaii.5-0.7x01.HDTV.XviD.[www.DivxTotaL.com].avi", "/Hawaii.5-0.7x01.HDTV.XviD.[www.DivxTotaL.com].avi", 7, 1)]
    [InlineData("Stargate.SG.1.1x09.Dual.720p-lat", "/Stargate.SG.1.1x09.Dual.720p-lat.mkv", 1, 9)]
    [InlineData("th3 0rv1ll3.2x02.m720p.ES.mkv", "/th3 0rv1ll3.2x02.m720p.ES.mkv", 2, 2)]
    [InlineData("Machos alfa 5X3 HDTV XviD Castellano", "/Machos alfa 5X3 HDTV XviD Castellano/Machos alfa 5X3 HDTV XviD Castellano.avi", 5, 3)]
    [InlineData("The Boys T5x3 [AMZN WEB-DL 1080p HEVC x265 10Bits.mkv", "/The Boys T5x3 [AMZN WEB-DL 1080p HEVC x265 10Bits.mkv", 5, 3)]
    [InlineData("EXchange Season 4 E14 [H264.1080p.WEB-DL.AAC.2.0][YUKINOMATI].mkv", "/EXchange Season 4 E14 [H264.1080p.WEB-DL.AAC.2.0][YUKINOMATI].mkv", 4, 14)]
    [InlineData("Asur (2023) Season 2 S02 (1080p JIO WEBRip x265 HEVC 10bit DD 5.1 IONICBOY)", "/Asur.S02E01.1080p.10bit.WEBRip.DD.5.1x265.HEVC-IONICBOY.mkv", 2, 1)]
    [InlineData("Arya S01-S03(2020-23).1080p.WEB-DL.Multi DDP.x264.Esub-KIN", "/S01/Aarya.S01E09.Dharm.Sankat.1080pWEB-DL.DDP5.1x264-KIN.mkv", 1, 9)]
    [InlineData("Game.of.Thrones.S04.2160p.BluRay.REMUX.HEVC.DTS-HD.MA.TrueHD.7.1.Atmos-FGT", "/Game.of.Thrones.S04E03.Breaker.of.Chains.2160p.BluRay.REMUX.HEVC.DTS-HD.MA.TrueHD.7.1.Atmos-FGT.mkv", 4, 3)]
    [InlineData("101.Dalmatian.Street.S01E03E04.MULTI.1080p.DSNP.WEB.DL.AAC2.0.x264-AndreMor", "/101.Dalmatian.Street.S01E03E04.Power.to.the.Puppies.MULTI.1080p.DSNP.WEB.DL.AAC2.0.x264-AndreMor.mkv", 1, 3)]
    public void StillReadsEpisodesAroundTheseTags(string torrent, string file, int season, int episode)
    {
        var parsed = _names.ParseEpisode(torrent, file);

        Assert.NotNull(parsed);
        Assert.Equal(season, parsed!.SeasonNumber);
        Assert.Equal(episode, parsed.EpisodeNumber);
    }

    [Theory]
    [InlineData("/Some.Release.mkv", true)]
    [InlineData("/Some.Release.mp4", true)]
    [InlineData("/Some.Release.MKV", true)]
    [InlineData("/Some.Release.nfo", false)]
    [InlineData("/Some.Release.rar", false)]
    public void RecognisesVideoFiles(string path, bool expected)
    {
        Assert.Equal(expected, ReleaseNames.IsVideo(path));
    }

    [Fact]
    public void TurnsSeparatorsBackIntoSpaces()
    {
        Assert.Equal("The Matrix Reloaded 2003", ReleaseNames.Humanise("The.Matrix_Reloaded.2003"));
    }
}
