using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Moves the per-user data folder from the pre-rename location
/// (<c>%LOCALAPPDATA%\Screenbox</c>) to the current one (<c>%LOCALAPPDATA%\FFlux</c>).
/// </summary>
/// <remarks>
/// The app was renamed to FFlux, but the data folder kept the old name so existing
/// installations would keep their settings and media library. This migration is the
/// one-shot handover that finally moves the data across.
/// <para>
/// The move is deliberately conservative: it never overwrites a file that already
/// exists in the destination, it leaves the legacy folder in place as a fallback, and
/// it swallows every failure so a locked database can never keep the app from starting.
/// </para>
/// </remarks>
public static class AppDataMigration
{
    /// <summary>The data folder name used before the FFlux rename.</summary>
    public const string LegacyFolderName = "Screenbox";

    /// <summary>The data folder name used after the FFlux rename.</summary>
    public const string CurrentFolderName = "FFlux";

    /// <summary>Database file name used before the FFlux rename.</summary>
    public const string LegacyDatabaseFileName = "screenbox.db";

    /// <summary>Database file name used after the FFlux rename.</summary>
    public const string CurrentDatabaseFileName = "fflux.db";

    /// <summary>
    /// Root of the per-user registry area where an unpackaged app's
    /// <c>ApplicationData.LocalSettings</c> is stored.
    /// </summary>
    public const string RegistryRoot = @"Software\Classes\Local Settings\Software";

    /// <summary>Registry key holding the settings written before the FFlux rename.</summary>
    public const string LegacyRegistrySubKey = RegistryRoot + @"\Screenbox\Screenbox";

    /// <summary>Registry key holding the settings written after the FFlux rename.</summary>
    public const string CurrentRegistrySubKey = RegistryRoot + @"\FFlux\FFlux";

    private static readonly object Gate = new();
    private static bool _completed;

    /// <summary>
    /// Gets the result of the last <see cref="Run"/> call, or <see langword="null"/> when the
    /// migration has not run yet.
    /// </summary>
    public static MigrationResult? LastResult { get; private set; }

    /// <summary>
    /// Migrates the legacy data folder into the current one. Safe to call repeatedly:
    /// the work happens at most once per process, and files that already exist in the
    /// destination are left untouched.
    /// </summary>
    /// <returns>The outcome of the migration.</returns>
    public static MigrationResult Run()
    {
        lock (Gate)
        {
            if (_completed)
            {
                return LastResult ?? MigrationResult.NotNeeded("already ran in this process");
            }

            _completed = true;

            MigrationResult files = Migrate();
            MigrationResult registry = MigrateRegistrySettings(LegacyRegistrySubKey, CurrentRegistrySubKey);
            LastResult = Combine(files, registry);
            return LastResult;
        }
    }

    private static MigrationResult Combine(MigrationResult files, MigrationResult registry)
    {
        bool failed = files.Failed || registry.Failed;
        bool migrated = files.Migrated || registry.Migrated;
        bool skipped = !migrated && !failed;
        return new MigrationResult(migrated, skipped, failed, $"files: {files.Message}; registry: {registry.Message}");
    }

    private static MigrationResult Migrate()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return MigrationResult.NotNeeded("no local app data folder");
            }

            string legacyRoot = Path.Combine(localAppData, LegacyFolderName);
            string currentRoot = Path.Combine(localAppData, CurrentFolderName);

            return Migrate(legacyRoot, currentRoot);
        }
        catch (Exception ex)
        {
            return MigrationResult.Error(ex);
        }
    }

    /// <summary>
    /// Moves everything from <paramref name="legacyRoot"/> into <paramref name="currentRoot"/>.
    /// Files that already exist in the destination are kept as they are.
    /// </summary>
    /// <param name="legacyRoot">The folder to move data out of.</param>
    /// <param name="currentRoot">The folder to move data into.</param>
    /// <returns>The outcome of the migration.</returns>
    public static MigrationResult Migrate(string legacyRoot, string currentRoot)
    {
        try
        {
            if (string.IsNullOrEmpty(legacyRoot) || string.IsNullOrEmpty(currentRoot))
            {
                return MigrationResult.NotNeeded("no folders to migrate");
            }

            if (!Directory.Exists(legacyRoot))
            {
                return MigrationResult.NotNeeded("no legacy folder");
            }

            Directory.CreateDirectory(currentRoot);

            int movedFiles = 0;
            int movedFolders = 0;
            int skippedFiles = 0;

            foreach (string file in Directory.EnumerateFiles(legacyRoot))
            {
                string name = Path.GetFileName(file);
                string targetName = name switch
                {
                    LegacyDatabaseFileName => CurrentDatabaseFileName,
                    LegacyDatabaseFileName + "-shm" => CurrentDatabaseFileName + "-shm",
                    LegacyDatabaseFileName + "-wal" => CurrentDatabaseFileName + "-wal",
                    _ => name,
                };

                string destination = Path.Combine(currentRoot, targetName);
                if (File.Exists(destination))
                {
                    skippedFiles++;
                    continue;
                }

                File.Move(file, destination);
                movedFiles++;
            }

            foreach (string folder in Directory.EnumerateDirectories(legacyRoot))
            {
                string destination = Path.Combine(currentRoot, Path.GetFileName(folder));
                if (Directory.Exists(destination))
                {
                    // Merge instead of skipping, so a partially migrated folder still
                    // receives the files it is missing.
                    movedFolders += MergeDirectory(folder, destination, ref movedFiles, ref skippedFiles);
                    continue;
                }

                Directory.Move(folder, destination);
                movedFolders++;
            }

            // The legacy folder is intentionally left in place. It is the user's data and
            // deleting it is not this method's call to make.
            return MigrationResult.Completed(movedFiles, movedFolders, skippedFiles);
        }
        catch (Exception ex)
        {
            return MigrationResult.Error(ex);
        }
    }

    /// <summary>
    /// Copies the settings an unpackaged app keeps in the registry from one key to another.
    /// The destination is left untouched when it already holds values, so a half-migrated
    /// install never loses the newer settings.
    /// </summary>
    /// <param name="sourceSubKey">Registry key to read the settings from.</param>
    /// <param name="destinationSubKey">Registry key to write the settings to.</param>
    /// <returns>The outcome of the migration.</returns>
    public static MigrationResult MigrateRegistrySettings(string sourceSubKey, string destinationSubKey)
    {
        try
        {
            using RegistryKey? source = Registry.CurrentUser.OpenSubKey(sourceSubKey);
            if (source is null)
            {
                return MigrationResult.NotNeeded("no legacy registry key");
            }

            if (source.GetValueNames().Length == 0 && source.GetSubKeyNames().Length == 0)
            {
                return MigrationResult.NotNeeded("legacy registry key is empty");
            }

            using (RegistryKey? existing = Registry.CurrentUser.OpenSubKey(destinationSubKey))
            {
                if (existing is not null &&
                    (existing.GetValueNames().Length > 0 || existing.GetSubKeyNames().Length > 0))
                {
                    return MigrationResult.NotNeeded("destination registry key already has values");
                }
            }

            int copied = CopyRegistryKey(source, destinationSubKey);
            return MigrationResult.Completed(copied, 0, 0);
        }
        catch (Exception ex)
        {
            return MigrationResult.Error(ex);
        }
    }

    private static int CopyRegistryKey(RegistryKey source, string destinationSubKey)
    {
        using RegistryKey destination = Registry.CurrentUser.CreateSubKey(destinationSubKey, true)
            ?? throw new InvalidOperationException($"Could not create registry key {destinationSubKey}");

        int copied = 0;

        foreach (string name in source.GetValueNames())
        {
            object? value = source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            destination.SetValue(name, value, source.GetValueKind(name));
            copied++;
        }

        foreach (string name in source.GetSubKeyNames())
        {
            using RegistryKey? child = source.OpenSubKey(name);
            if (child is null) continue;

            copied += CopyRegistryKey(child, destinationSubKey + "\\" + name);
        }

        return copied;
    }

    private static int MergeDirectory(string source, string destination, ref int movedFiles, ref int skippedFiles)
    {
        int merged = 0;

        foreach (string file in Directory.EnumerateFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target))
            {
                skippedFiles++;
                continue;
            }

            File.Copy(file, target);
            movedFiles++;
        }

        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(folder));
            if (Directory.Exists(target))
            {
                merged += MergeDirectory(folder, target, ref movedFiles, ref skippedFiles);
                continue;
            }

            Directory.Move(folder, target);
            merged++;
        }

        return merged;
    }

    /// <summary>
    /// Describes what happened during a migration attempt.
    /// </summary>
    public sealed record MigrationResult(bool Migrated, bool Skipped, bool Failed, string Message)
    {
        /// <summary>Creates a result describing a completed migration.</summary>
        public static MigrationResult Completed(int files, int folders, int skipped) =>
            new(true, false, false, $"moved {files} file(s) and {folders} folder(s), kept {skipped} existing");

        /// <summary>Creates a result describing a migration that had nothing to do.</summary>
        public static MigrationResult NotNeeded(string reason) => new(false, true, false, reason);

        /// <summary>Creates a result describing a failed migration.</summary>
        public static MigrationResult Error(Exception exception) => new(false, false, true, exception.Message);
    }
}
