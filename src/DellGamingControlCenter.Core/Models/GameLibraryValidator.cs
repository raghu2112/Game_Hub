namespace DellGamingControlCenter.Core.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Pure, UI-independent validation for saving a game profile. Returns a
/// user-facing error message, or null when the save is valid. Used by the
/// Add/Edit Game dialog; unit-tested so the rules cannot regress silently.
/// </summary>
public static class GameLibraryValidator
{
    /// <param name="gameName">Display name entered by the user.</param>
    /// <param name="executablePath">Full path to the game executable.</param>
    /// <param name="currentGameId">GameId of the profile being edited (null when adding), so a profile is never a duplicate of itself.</param>
    /// <param name="existingProfiles">All profiles currently in the library.</param>
    public static string? ValidateForSave(string gameName, string executablePath, string? currentGameId, IEnumerable<GameProfile> existingProfiles)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return "Game name is required.";
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return "Select a game executable (use Browse).";
        }

        if (executablePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "The executable path contains invalid characters.";
        }

        // Full path check (catches relative paths that would break after restart).
        if (!Path.IsPathRooted(executablePath))
        {
            return "The executable path must be absolute (e.g. D:\\Games\\Game\\game.exe).";
        }

        if (Directory.Exists(executablePath))
        {
            return "The selected path is a folder, not an executable. Select the game's .exe file.";
        }

        if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Only Windows executables (.exe) can be added to the library.";
        }

        if (!File.Exists(executablePath))
        {
            // When editing, an exe that has genuinely moved is allowed to save (the user may
            // fix it later); the card will honestly show EXECUTABLE MISSING. But a fresh add
            // must point at a real file — that is the workflow being validated.
            if (currentGameId is null)
            {
                return "Executable not found. Confirm the file exists, then try again.";
            }
        }

        var duplicate = existingProfiles.FirstOrDefault(p =>
            p.GameId != currentGameId &&
            string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            return $"This executable is already in the library as '{duplicate.GameName}'.";
        }

        return null;
    }

    /// <summary>
    /// A sensible default display name: file description metadata when present,
    /// otherwise the executable file name without extension.
    /// </summary>
    public static string DeriveDefaultName(string executablePath)
    {
        try
        {
            var desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
        }
        catch
        {
            // Metadata unreadable — fall back to the file name.
        }
        return Path.GetFileNameWithoutExtension(executablePath);
    }
}
