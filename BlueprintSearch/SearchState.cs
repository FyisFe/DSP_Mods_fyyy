using System;
using System.Collections.Generic;
using System.IO;

namespace BlueprintSearch;

/// <summary>
/// Per-session state for the blueprint search feature. Single instance (one browser window).
/// </summary>
internal static class SearchState
{
    internal struct PathEntry
    {
        /// <summary>Relative path from rootPath, lower-cased, forward-slash separators. Match target.</summary>
        public string relLower;
        /// <summary>Relative path from rootPath, original case, forward-slash separators. Display + fullPath source.</summary>
        public string relOriginal;
    }

    internal static string query = "";
    internal static string[] tokens = Array.Empty<string>();
    internal static readonly List<PathEntry> cachedEntries = new();
    internal static bool cacheDirty = true;
    internal static float lastChangeTime;
    internal static bool pendingRefresh;

    internal static bool Active => tokens.Length > 0;

    /// <summary>
    /// Enumerate every .txt blueprint under rootPath. Per-subtree try/catch so one bad folder
    /// does not fail the whole cache. Main thread; typical libraries finish in &lt;50ms.
    /// </summary>
    internal static void RebuildCache(string rootPath, int rootPathLen,
        BepInEx.Logging.ManualLogSource logger)
    {
        cachedEntries.Clear();
        if (!Directory.Exists(rootPath))
        {
            cacheDirty = false;
            return;
        }
        EnumerateDirectory(rootPath, rootPathLen, logger);
        cacheDirty = false;
    }

    private static void EnumerateDirectory(string dirFull, int rootPathLen,
        BepInEx.Logging.ManualLogSource logger)
    {
        string[] files;
        string[] subDirs;
        try
        {
            files = Directory.GetFiles(dirFull, "*.txt", SearchOption.TopDirectoryOnly);
            subDirs = Directory.GetDirectories(dirFull, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            logger.LogWarning($"BlueprintSearch: skipping {dirFull}: {e.GetType().Name}: {e.Message}");
            return;
        }
        foreach (string f in files)
        {
            // f is an absolute path under rootPath. Slice off the rootPath prefix.
            if (f.Length <= rootPathLen) continue;
            string rel = f.Substring(rootPathLen).Replace('\\', '/');
            cachedEntries.Add(new PathEntry
            {
                relLower = rel.ToLowerInvariant(),
                relOriginal = rel,
            });
        }
        foreach (string sub in subDirs)
        {
            EnumerateDirectory(sub, rootPathLen, logger);
        }
    }

    internal static void ClearQuery()
    {
        query = "";
        tokens = Array.Empty<string>();
        pendingRefresh = false;
    }

    internal static void Reset()
    {
        ClearQuery();
        cachedEntries.Clear();
        cacheDirty = true;
    }
}
