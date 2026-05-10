namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static string ResolveDatabasePath(string workspaceRoot, string indexDirectoryRelative)
    {
        // Back-compat note:
        // - Read operations can fall back to legacy location.
        // - Write operations (reindex) always go to the requested location.
        // This helper keeps old callsites working by using the "read" resolution.
        return ResolveDatabasePathForRead(workspaceRoot, indexDirectoryRelative);
    }

    internal static string ResolveDatabasePathForRead(string workspaceRoot, string indexDirectoryRelative)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var requestedDir = Path.Combine(root, indexDirectoryRelative.TrimStart(Path.DirectorySeparatorChar, '/'));
        var requestedDb = Path.Combine(requestedDir, $"codebase-index-v{FormatVersion}.sqlite");
        if (File.Exists(requestedDb))
            return requestedDb;

        var legacyDb = GetLegacyDbPath(root);
        if (File.Exists(legacyDb))
            return legacyDb;

        return requestedDb;
    }

    internal static string ResolveDatabasePathForWrite(string workspaceRoot, string indexDirectoryRelative)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var requestedDir = Path.Combine(root, indexDirectoryRelative.TrimStart(Path.DirectorySeparatorChar, '/'));
        Directory.CreateDirectory(requestedDir);

        // Best-effort migrate settings.toml if legacy exists and new doesn't.
        TryMigrateSettingsToml(root, requestedDir);

        return Path.Combine(requestedDir, $"codebase-index-v{FormatVersion}.sqlite");
    }

    private static string GetLegacyDbPath(string workspaceRootNormalized)
    {
        var legacyDir = Path.Combine(workspaceRootNormalized, ".cascade-ide", "hybrid-codebase-index");
        return Path.Combine(legacyDir, $"codebase-index-v{FormatVersion}.sqlite");
    }

    private static void TryMigrateSettingsToml(string workspaceRootNormalized, string requestedDir)
    {
        try
        {
            var newSettings = Path.Combine(requestedDir, "settings.toml");
            if (File.Exists(newSettings))
                return;

            var legacySettings = Path.Combine(workspaceRootNormalized, ".cascade-ide", "hybrid-codebase-index", "settings.toml");
            if (!File.Exists(legacySettings))
                return;

            File.Copy(legacySettings, newSettings);
        }
        catch
        {
            // best-effort
        }
    }
}

