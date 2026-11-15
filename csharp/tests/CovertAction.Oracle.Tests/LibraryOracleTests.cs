using CovertAction.Core.Graphics;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Library routines that the cryptography part does not have, against the recorded runs of a program that has them
/// (the menus part, FINAL). A routine proven here stands for its copies in the other programs, which are the same
/// instructions (docs/source-map/shared-functions.csv). Skipped without vectors.
/// </summary>
public sealed class LibraryOracleTests(ITestOutputHelper output)
{
    /// <summary>Where FINAL keeps the world's 16 cities in its data segment.</summary>
    // orig: FINAL DS:8294 ct (src/c/include/final.h)
    private const int FinalCities = 0x8294;

    [SkippableFact]
    public void KeyDistanceIsTheOriginals()
    {
        var tally = OracleRun.ForEachCase("FINAL", "key_distance", c =>
            Assert.True(GameLibrary.KeyDistance(c.Args[0], c.Args[1]) == c.Ax, $"{c.Id}: key_distance({c.Args[0]}, {c.Args[1]}) is {c.Ax} in the original"));
        output.WriteLine($"TALLY key_distance: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
    }

    [SkippableFact]
    public void CityDistanceIsTheOriginals()
    {
        var tally = OracleRun.ForEachCase("FINAL", "city_distance", c =>
        {
            var cities = new City[CampaignTables.WorldSize];
            for (var i = 0; i < cities.Length; i++)
            {
                cities[i] = new City();
                CampaignLayouts.City.Load(c.DgroupIn.AsSpan(FinalCities + i * CampaignLayouts.CitySize, CampaignLayouts.CitySize), cities[i]);
            }
            Assert.True(GameLibrary.CityDistance(cities, c.Args[0], c.Args[1]) == c.Ax, $"{c.Id}: city_distance({c.Args[0]}, {c.Args[1]}) is {c.Ax} in the original");
        });
        output.WriteLine($"TALLY city_distance: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
    }

    [SkippableTheory]
    [InlineData("FINAL", "drive_ready")]
    [InlineData("GAME", "disk_ready")]
    public void DriveReadyIsTheOriginals(string module, string name)
    {
        var tally = OracleRun.ForEachCase(module, name, c =>
        {
            var world = new RecordingBoundaries(c.Calls);
            var ready = LibraryDisks.DriveReady(world, c.Args[0]);
            Assert.True((ready ? 1 : 0) == c.Ax, $"{c.Id}: drive_ready({c.Args[0]}) is {c.Ax} in the original");
            world.AssertAllCallsMade();
        });
        output.WriteLine($"TALLY {module} {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {name} {line}");
        }
    }

    // ---- the picture loaders on the programs that carry them under other names ----

    /// <summary>A program's copy of a picture routine: its name there, and where the program keeps the number of its open archive.</summary>
    /// <param name="Module">The program.</param>
    /// <param name="Name">The routine's name in that program.</param>
    /// <param name="ArchiveHandle">The data segment address of the open archive's number (FINAL's catalog_handle, GAME's data_file_handle).</param>
    private sealed record PictureRoutine(string Module, string Name, int ArchiveHandle);

    /// <summary>Where each program keeps the number of its open archive.</summary>
    // orig: FINAL DS:4c84 catalog_handle, GAME DS:449a data_file_handle, TAC DS:38dc archive_handle, BUG DS:3034 archive_handle (the twins of CHASE DS:1b34 archive_handle)
    private static readonly Dictionary<string, int> ArchiveHandles = new() { ["FINAL"] = 0x4c84, ["GAME"] = 0x449a, ["TAC"] = 0x38dc, ["BUG"] = 0x3034, ["CHASE"] = 0x1b34 };

    private static string TextAt(byte[] dgroup, int address)
    {
        var end = Array.IndexOf(dgroup, (byte)0, address);
        return System.Text.Encoding.Latin1.GetString(dgroup, address, (end < 0 ? dgroup.Length : end) - address);
    }

    /// <summary>Replays the cases of one copy of a picture routine through <see cref="LibraryPictures"/> on the recorded file and drawing calls.</summary>
    private void ReplayPictures(PictureRoutine routine, Func<LibraryPictures, OracleCase, int?> call)
    {
        var tally = OracleRun.ForEachCase(routine.Module, routine.Name, c =>
        {
            var world = new RecordingBoundaries(c.Calls);
            var pictures = new LibraryPictures(new Boundaries(world, world, world, world, world, world))
            {
                ArchiveHandle = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(c.DgroupIn.AsSpan(routine.ArchiveHandle)),
            };
            int? result;
            try
            {
                result = call(pictures, c);
            }
            catch (CovertAction.Msc.ProgramExitException exit) when (c.Exit is not null)
            {
                // the file could not be opened: the original names it and ends the program, and so does the C#
                Assert.True(exit.Code == c.Exit, $"the C# ends the program with the code {exit.Code}, the original with {c.Exit}");
                world.ProgramEnded();
                return;
            }
            Assert.True(c.Exit is null, "the original ended the program, the C# went on");
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == c.Ax, $"{c.Id}: the C# returns {value}, the original {c.Ax}");
            }
            world.AssertAllCallsMade();
        });
        output.WriteLine($"TALLY {routine.Module} {routine.Name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {routine.Module} {routine.Name} {line}");
        }
    }

    /// <summary>The copies of open_resource, close_resource, load_picture and load_picture_swapped in FINAL, GAME, TAC and BUG, by the name each program gives them.</summary>
    public static IEnumerable<object[]> PictureRoutines =>
    [
        ["FINAL", "open_picture_file", "open"], ["GAME", "open_resource", "open"], ["TAC", "open_resource", "open"], ["BUG", "open_resource", "open"],
        ["FINAL", "close_picture_file", "close"], ["TAC", "close_resource", "close"], ["BUG", "close_resource", "close"],
        ["FINAL", "draw_picture_to_page", "load"], ["GAME", "load_raw_picture", "load"], ["TAC", "load_pic", "load"], ["BUG", "load_pic", "load"],
        ["FINAL", "draw_picture_file", "load swapped"], ["GAME", "load_raw_picture_swapped", "load swapped"],
        ["TAC", "load_pic_name_first", "load swapped"], ["BUG", "load_pic_name_first", "load swapped"],
        // no program calls it: its recorded runs all end where the file cannot be opened
        ["GAME", "load_picture_surface", "block"], ["BUG", "load_picture_surface", "block"], ["CHASE", "load_picture_surface", "block"],
    ];

    [SkippableTheory]
    [MemberData(nameof(PictureRoutines))]
    public void ThePictureLoadersAreTheOriginals(string module, string name, string kind)
    {
        var routine = new PictureRoutine(module, name, ArchiveHandles[module]);
        ReplayPictures(routine, (pictures, c) =>
        {
            switch (kind)
            {
                case "open":
                    return pictures.OpenResource(TextAt(c.DgroupIn, (ushort)c.Args[0]), c.Args[1]);
                case "close":
                    pictures.CloseResource(c.Args[0]);
                    return null;
                case "load":
                    pictures.LoadPicture(c.Args[0], TextAt(c.DgroupIn, (ushort)c.Args[1]));
                    return null;
                case "block":
                    // the first two arguments are the far address of the caller's buffer; the answer is the block's count
                    pictures.LoadPictureSurface(TextAt(c.DgroupIn, (ushort)c.Args[2]));
                    return null;
                default:
                    pictures.LoadPictureSwapped(TextAt(c.DgroupIn, (ushort)c.Args[0]), c.Args[1]);
                    return null;
            }
        });
    }

    // ---- the sheets of faces, cut into each program's table of images ----

    /// <summary>
    /// Replays the cases of one program's load_faces_pic on that program's state: the files read, the rows put on the
    /// page and every cut must be the original's, and the table of images afterwards the original's table.
    /// </summary>
    /// <param name="womenSheet">The data segment address of the program's text "facesf.pic".</param>
    /// <param name="menSheet">The data segment address of the program's text "faces.pic".</param>
    /// <param name="name">The program's name of the routine: load_faces_pic, or load_pictures (TAC: load_face_sprites), which calls it and cuts sixteen small pictures more.</param>
    private void ReplayFaces<TState>(string module, string name, StateLayout<TState> layout, Func<TState, Image?[]> table, Func<TState, ImageRegister> images, int womenSheet, int menSheet)
        where TState : class, ILibraryState, new()
    {
        var tally = OracleRun.ForEachCase(module, name, c =>
        {
            if (layout.OutsideModel(c.DgroupIn) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons.Take(4)));
            }
            var state = new TState();
            layout.Load(c.DgroupIn, state);
            var world = new RecordingBoundaries(c.Calls, o => layout.AddressOf(state, o), images(state));
            var pictures = new LibraryPictures(new Boundaries(world, world, world, world, world, world))
            {
                ArchiveHandle = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(c.DgroupIn.AsSpan(ArchiveHandles[module])),
            };
            if (name == "load_faces_pic")
            {
                pictures.LoadFacesPic(table(state), TextAt(c.DgroupIn, womenSheet), TextAt(c.DgroupIn, menSheet));
            }
            else
            {
                pictures.LoadPictures(state, table(state), TextAt(c.DgroupIn, womenSheet), TextAt(c.DgroupIn, menSheet));
            }
            Assert.True(c.Exit is null, "the original ended the program, the C# went on");
            world.AssertAllCallsMade();
            // The picture reader's and decoder's working memory is not reproduced (D-12) and no field of the state:
            // every modelled variable is compared, a change outside them is not held against the C#.
            OracleRun.AssertImage(layout, state, c.DgroupIn, c.DgroupOut, c.Ignore, unmodelledChangesFail: false);
        });
        output.WriteLine($"TALLY {module} {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {name} {line}");
        }
    }

    // orig: FINAL DS:3fc6 "facesf.pic", DS:3fd1 "faces.pic"; TAC DS:2f0c, DS:2f17; BUG DS:240a, DS:2415 (the two literals of each copy of load_faces_pic)
    [SkippableTheory]
    [InlineData("load_faces_pic")]
    [InlineData("load_pictures")]
    public void FinalsFacesAreCutAsTheOriginalCutsThem(string name) =>
        ReplayFaces("FINAL", name, FinalDgroup.Layout, s => s.SpriteTable, s => s.Images, 0x3fc6, 0x3fd1);

    [SkippableTheory]
    [InlineData("load_faces_pic")]
    [InlineData("load_face_sprites")]
    public void TacsFacesAreCutAsTheOriginalCutsThem(string name) =>
        ReplayFaces("TAC", name, TacDgroup.Layout, s => s.IconHandles, s => s.Images, 0x2f0c, 0x2f17);

    [SkippableTheory]
    [InlineData("load_faces_pic")]
    [InlineData("load_pictures")]
    public void BugsFacesAreCutAsTheOriginalCutsThem(string name) =>
        ReplayFaces("BUG", name, BugDgroup.Layout, s => s.Sprite, s => s.Images, 0x240a, 0x2415);
}
