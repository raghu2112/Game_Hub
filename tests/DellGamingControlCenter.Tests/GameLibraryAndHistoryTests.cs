namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Models;
using Xunit;

public class TelemetryHistoryTests
{
    [Fact]
    public void Push_BeyondCapacity_DropsOldest()
    {
        var h = new TelemetryHistory(3);
        h.Push(10); h.Push(20); h.Push(30); h.Push(40);

        var buf = new double?[3];
        var n = h.CopyTo(buf);

        Assert.Equal(3, n);
        Assert.Equal(new double?[] { 20, 30, 40 }, buf);
    }

    [Fact]
    public void Push_NullValue_IsPreservedAsGap_NotZero()
    {
        var h = new TelemetryHistory(4);
        h.Push(10); h.Push(null); h.Push(30);

        var buf = new double?[4];
        var n = h.CopyTo(buf);

        Assert.Equal(3, n);
        Assert.Null(buf[1]);
        Assert.Equal(30, buf[2]);
    }

    [Fact]
    public void PartialFill_ReturnsOnlyWrittenSamples()
    {
        var h = new TelemetryHistory(10);
        h.Push(1); h.Push(2);

        var buf = new double?[10];
        Assert.Equal(2, h.CopyTo(buf));
        Assert.Equal(1, buf[0]);
        Assert.Equal(2, buf[1]);
    }

    [Fact]
    public void Latest_SkipsNullGaps()
    {
        var h = new TelemetryHistory(5);
        h.Push(5); h.Push(null); h.Push(null);
        Assert.Equal(5, h.Latest());

        h.Push(9);
        Assert.Equal(9, h.Latest());
    }

    [Fact]
    public void Latest_Empty_ReturnsNull()
    {
        var h = new TelemetryHistory(5);
        Assert.Null(h.Latest());
    }

    [Fact]
    public void Updated_EventFires_PerPush()
    {
        var h = new TelemetryHistory(3);
        int fired = 0;
        h.Updated += () => fired++;
        h.Push(1); h.Push(2);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Push_NeverExceedsCapacity_BufferStaysBounded()
    {
        var h = new TelemetryHistory(8);
        var buf = new double?[8];
        for (int i = 0; i < 1000; i++)
        {
            h.Push(i);
        }
        Assert.Equal(8, h.CopyTo(buf));
        Assert.Equal(999, buf[7]); // newest
        Assert.Equal(992, buf[0]); // oldest surviving
    }
}

public class GameLibraryValidatorTests
{
    private static readonly string ExistingExe = Path.Combine(AppContext.BaseDirectory, "existing_game.exe");

    public GameLibraryValidatorTests()
    {
        if (!File.Exists(ExistingExe))
        {
            File.WriteAllText(ExistingExe, "not really an exe, but exists for path tests");
        }
    }

    [Fact]
    public void Save_EmptyName_IsRejected()
    {
        var err = GameLibraryValidator.ValidateForSave("", @"C:\Games\Game\game.exe", null, Array.Empty<GameProfile>());
        Assert.Equal("Game name is required.", err);
    }

    [Fact]
    public void Save_EmptyPath_IsRejected()
    {
        var err = GameLibraryValidator.ValidateForSave("Game", "", null, Array.Empty<GameProfile>());
        Assert.Equal("Select a game executable (use Browse).", err);
    }

    [Fact]
    public void Save_RelativePath_IsRejected()
    {
        var err = GameLibraryValidator.ValidateForSave("Game", "game.exe", null, Array.Empty<GameProfile>());
        Assert.Contains("absolute", err);
    }

    [Fact]
    public void Save_NonExeFile_IsRejected()
    {
        var err = GameLibraryValidator.ValidateForSave("Game", @"C:\Games\game.dll", null, Array.Empty<GameProfile>());
        Assert.Contains(".exe", err);
    }

    [Fact]
    public void Save_MissingFile_OnAdd_IsRejected()
    {
        var err = GameLibraryValidator.ValidateForSave("NFS", @"D:\Games\NFS\missing.exe", null, Array.Empty<GameProfile>());
        Assert.Contains("not found", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_MissingFile_OnEdit_IsAllowed_CardShowsExecutableMissing()
    {
        // Editing a profile whose exe moved must not be blocked — the card honestly
        // shows EXECUTABLE MISSING. Only fresh adds require a real file.
        var err = GameLibraryValidator.ValidateForSave("NFS", @"D:\Games\NFS\missing.exe", "abc123", Array.Empty<GameProfile>());
        Assert.Null(err);
    }

    [Fact]
    public void Save_DuplicateExecutable_IsRejected_WithExistingName()
    {
        var profiles = new List<GameProfile>
        {
            new() { GameId = "g1", GameName = "Need for Speed", ExecutablePath = ExistingExe }
        };
        var err = GameLibraryValidator.ValidateForSave("Speed II", ExistingExe, null, profiles);
        Assert.Contains("Need for Speed", err);
    }

    [Fact]
    public void Save_SameProfileBeingEdited_IsNotADuplicate()
    {
        var profiles = new List<GameProfile>
        {
            new() { GameId = "g1", GameName = "Need for Speed", ExecutablePath = ExistingExe }
        };
        var err = GameLibraryValidator.ValidateForSave("Need for Speed (Renamed)", ExistingExe, "g1", profiles);
        Assert.Null(err);
    }

    [Fact]
    public void Save_ValidAdd_ReturnsNull()
    {
        var err = GameLibraryValidator.ValidateForSave("game", ExistingExe, null, Array.Empty<GameProfile>());
        Assert.Null(err);
    }

    [Fact]
    public void Save_DuplicateIsCaseInsensitive_OnDriveLetterAndName()
    {
        var profiles = new List<GameProfile>
        {
            new() { GameId = "g1", GameName = "game", ExecutablePath = ExistingExe.ToUpperInvariant() }
        };
        var err = GameLibraryValidator.ValidateForSave("Other Name", ExistingExe.ToLowerInvariant(), null, profiles);
        Assert.Contains("already in the library", err);
    }

    [Fact]
    public void DeriveDefaultName_FallsBackToFileName()
    {
        var name = GameLibraryValidator.DeriveDefaultName(ExistingExe);
        Assert.Equal("existing_game", name);
    }
}
