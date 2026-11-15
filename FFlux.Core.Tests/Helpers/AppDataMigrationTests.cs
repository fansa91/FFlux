using System;
using System.IO;
using Microsoft.Win32;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class AppDataMigrationTests
{
    private static string CreateRoot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fflux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Test]
    public async Task FolderNames_ShouldReflectTheRename()
    {
        await Assert.That(AppDataMigration.LegacyFolderName).IsEqualTo("Screenbox");
        await Assert.That(AppDataMigration.CurrentFolderName).IsEqualTo("FFlux");
        await Assert.That(AppDataMigration.LegacyDatabaseFileName).IsEqualTo("screenbox.db");
        await Assert.That(AppDataMigration.CurrentDatabaseFileName).IsEqualTo("fflux.db");
    }

    [Test]
    public async Task Migrate_WhenLegacyFolderMissing_ShouldSkip()
    {
        string root = CreateRoot();
        string legacy = Path.Combine(root, "Screenbox");
        string current = Path.Combine(root, "FFlux");

        AppDataMigration.MigrationResult result = AppDataMigration.Migrate(legacy, current);

        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Failed).IsFalse();
        await Assert.That(result.Migrated).IsFalse();
    }

    [Test]
    public async Task Migrate_ShouldMoveFilesAndRenameTheDatabase()
    {
        string root = CreateRoot();
        string legacy = Path.Combine(root, "Screenbox");
        string current = Path.Combine(root, "FFlux");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "screenbox.db"), "database");
        File.WriteAllText(Path.Combine(legacy, "screenbox.db-wal"), "wal");
        File.WriteAllText(Path.Combine(legacy, "language.txt"), "de-DE");

        AppDataMigration.MigrationResult result = AppDataMigration.Migrate(legacy, current);

        await Assert.That(result.Migrated).IsTrue();
        await Assert.That(result.Failed).IsFalse();
        await Assert.That(File.Exists(Path.Combine(current, "fflux.db"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(current, "fflux.db"))).IsEqualTo("database");
        await Assert.That(File.Exists(Path.Combine(current, "fflux.db-wal"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(current, "language.txt"))).IsEqualTo("de-DE");
        await Assert.That(File.Exists(Path.Combine(legacy, "screenbox.db"))).IsFalse();
    }

    [Test]
    public async Task Migrate_ShouldNotOverwriteExistingFiles()
    {
        string root = CreateRoot();
        string legacy = Path.Combine(root, "Screenbox");
        string current = Path.Combine(root, "FFlux");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "language.txt"), "old");
        File.WriteAllText(Path.Combine(current, "language.txt"), "new");

        AppDataMigration.MigrationResult result = AppDataMigration.Migrate(legacy, current);

        await Assert.That(result.Migrated).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(current, "language.txt"))).IsEqualTo("new");
        await Assert.That(File.Exists(Path.Combine(legacy, "language.txt"))).IsTrue();
    }

    [Test]
    public async Task Migrate_ShouldMergeFoldersInsteadOfSkippingThem()
    {
        string root = CreateRoot();
        string legacy = Path.Combine(root, "Screenbox");
        string current = Path.Combine(root, "FFlux");
        Directory.CreateDirectory(Path.Combine(legacy, "Visualizers", "Nested"));
        Directory.CreateDirectory(Path.Combine(current, "Visualizers"));
        File.WriteAllText(Path.Combine(legacy, "Visualizers", "one.png"), "one");
        File.WriteAllText(Path.Combine(legacy, "Visualizers", "Nested", "two.png"), "two");

        AppDataMigration.MigrationResult result = AppDataMigration.Migrate(legacy, current);

        await Assert.That(result.Migrated).IsTrue();
        await Assert.That(File.Exists(Path.Combine(current, "Visualizers", "one.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(current, "Visualizers", "Nested", "two.png"))).IsTrue();
    }

    [Test]
    public async Task Migrate_WhenLegacyFolderMissing_ShouldNotCreateTheCurrentFolder()
    {
        string root = CreateRoot();
        string legacy = Path.Combine(root, "Screenbox");
        string current = Path.Combine(root, "FFlux");

        AppDataMigration.Migrate(legacy, current);

        await Assert.That(Directory.Exists(current)).IsFalse();
    }

    [Test]
    public async Task MigrateRegistrySettings_ShouldCopyValuesAndSubKeys()
    {
        string root = CreateRegistryRoot();
        string source = root + @"\source";
        string destination = root + @"\destination";

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(source))
            {
                key.SetValue("Theme", "Dark", RegistryValueKind.String);
                key.SetValue("Count", 42, RegistryValueKind.DWord);
                using RegistryKey child = key.CreateSubKey("Nested");
                child.SetValue("Inner", "value", RegistryValueKind.String);
            }

            AppDataMigration.MigrationResult result = AppDataMigration.MigrateRegistrySettings(source, destination);

            await Assert.That(result.Migrated).IsTrue();
            using RegistryKey migrated = Registry.CurrentUser.OpenSubKey(destination);
            await Assert.That(migrated).IsNotNull();
            await Assert.That((string)migrated!.GetValue("Theme")!).IsEqualTo("Dark");
            await Assert.That((int)migrated.GetValue("Count")!).IsEqualTo(42);
            using RegistryKey nested = migrated.OpenSubKey("Nested");
            await Assert.That(nested).IsNotNull();
            await Assert.That((string)nested!.GetValue("Inner")!).IsEqualTo("value");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, false);
        }
    }

    [Test]
    public async Task MigrateRegistrySettings_ShouldNotOverwriteExistingValues()
    {
        string root = CreateRegistryRoot();
        string source = root + @"\source";
        string destination = root + @"\destination";

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(source))
            {
                key.SetValue("Theme", "Dark", RegistryValueKind.String);
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(destination))
            {
                key.SetValue("Theme", "Light", RegistryValueKind.String);
            }

            AppDataMigration.MigrationResult result = AppDataMigration.MigrateRegistrySettings(source, destination);

            await Assert.That(result.Skipped).IsTrue();
            using RegistryKey existing = Registry.CurrentUser.OpenSubKey(destination);
            await Assert.That((string)existing!.GetValue("Theme")!).IsEqualTo("Light");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, false);
        }
    }

    [Test]
    public async Task MigrateRegistrySettings_WhenSourceMissing_ShouldSkip()
    {
        string root = CreateRegistryRoot();

        AppDataMigration.MigrationResult result = AppDataMigration.MigrateRegistrySettings(
            root + @"\missing",
            root + @"\destination");

        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Failed).IsFalse();
    }

    private static string CreateRegistryRoot()
    {
        string root = $@"Software\fflux-tests\{Guid.NewGuid():N}";
        Registry.CurrentUser.CreateSubKey(root);
        return root;
    }
}
