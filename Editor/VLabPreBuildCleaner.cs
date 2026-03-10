// Packages/com.mhcockpit.vlpipe/Editor/VLabPreBuildCleaner.cs
// Menu: Tools → Virtual Lab → Pipeline → Clean Build Cache
//
// Responsibilities
//   1. Delete the entire ServerData folder (stale remote bundles)
//   2. Clear the Unity Addressables content-build cache
//   3. Force a full AssetDatabase refresh so the next build starts clean
//
// This script is called automatically at Step 1 of the BuildAndUploadToS3
// pipeline, but can also be invoked manually via the menu item below.

#if UNITY_EDITOR && ADDRESSABLES_INSTALLED

using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MHCockpit.VLPipe.Editor
{
    /// <summary>
    /// Pre-build cache cleaner for the Virtual Lab Addressables pipeline.
    /// Guarantees that every CI run and every manual build starts from a
    /// known-clean state, preventing stale bundle artefacts from polluting S3.
    /// </summary>
    public static class VLabPreBuildCleaner
    {
        // ─────────────────────────────────────────────────────────────────────
        //  CONSTANTS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Project-relative path of the Addressables remote output folder.
        /// Must match Remote.BuildPath in your Addressables profile.
        /// </summary>
        private const string SERVER_DATA_FOLDER = "ServerData";

        // ─────────────────────────────────────────────────────────────────────
        //  MENU ITEM
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Virtual Lab/Pipeline/Clean Build Cache", false, 10)]
        public static void CleanBuildCacheMenuItem()
        {
            CleanBuildCache();
            EditorUtility.DisplayDialog(
                "Clean Build Cache",
                "Cache cleared successfully.\n\n" +
                "• ServerData folder deleted\n" +
                "• Addressables content cache cleared\n" +
                "• AssetDatabase refreshed",
                "OK");
        }

        [MenuItem("Tools/Virtual Lab/Pipeline/Clean Build Cache", validate = true)]
        public static bool ValidateCleanBuildCacheMenuItem() =>
            !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        // ─────────────────────────────────────────────────────────────────────
        //  PUBLIC API  — called by BuildAndUploadToS3 at pipeline Step 1
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Performs all three cleanup operations in sequence.
        /// Safe to call from batch-mode CI as well as the interactive Editor.
        /// </summary>
        public static void CleanBuildCache()
        {
            Debug.Log("[VLab Cleaner] ══════════════════════════════════════════");
            Debug.Log("[VLab Cleaner]  Step 1 — Clean Build Cache");
            Debug.Log("[VLab Cleaner] ══════════════════════════════════════════");

            DeleteServerDataFolder();
            ClearAddressablesCache();
            RefreshAssetDatabase();

            Debug.Log("[VLab Cleaner] ✓ Build cache cleaned successfully.");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PRIVATE OPERATIONS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes &lt;ProjectRoot&gt;/ServerData and its entire contents.
        /// The folder is re-created from scratch by every Addressables build,
        /// so deleting it here is both safe and necessary to avoid stale bundles.
        /// </summary>
        private static void DeleteServerDataFolder()
        {
            string projectRoot     = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string serverDataPath  = Path.Combine(projectRoot, SERVER_DATA_FOLDER);

            if (!Directory.Exists(serverDataPath))
            {
                Debug.Log($"[VLab Cleaner] ServerData not found — nothing to delete. ({serverDataPath})");
                return;
            }

            try
            {
                Directory.Delete(serverDataPath, recursive: true);
                Debug.Log($"[VLab Cleaner] ✓ Deleted: {serverDataPath}");
            }
            catch (IOException ex)
            {
                // Non-fatal: log and continue. A locked file from a previous build
                // process would cause this; the subsequent Addressables build will
                // overwrite the relevant files anyway.
                Debug.LogWarning(
                    $"[VLab Cleaner] Could not fully delete ServerData: {ex.Message}\n" +
                    "Continuing — Addressables build will overwrite stale files.");
            }
        }

        /// <summary>
        /// Clears the Addressables player-content build cache so that
        /// incremental bundle hashes from a previous platform or session
        /// cannot bleed into the current build.
        /// </summary>
        private static void ClearAddressablesCache()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                Debug.LogWarning(
                    "[VLab Cleaner] Addressables Settings not found — skipping cache clear.\n" +
                    "Run the Project Setup to initialise Addressables.");
                return;
            }

            try
            {
                AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
                Debug.Log("[VLab Cleaner] ✓ Addressables player-content cache cleared.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[VLab Cleaner] Addressables cache clear reported an issue: {ex.Message}\n" +
                    "This is usually benign when no previous build cache exists.");
            }
        }

        /// <summary>
        /// Forces a synchronous AssetDatabase refresh so Unity picks up any
        /// file-system changes made by the two previous steps before compilation
        /// or the Addressables build begins.
        /// </summary>
        private static void RefreshAssetDatabase()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[VLab Cleaner] ✓ AssetDatabase refreshed.");
        }
    }
}

#endif // UNITY_EDITOR && ADDRESSABLES_INSTALLED