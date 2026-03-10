// Packages/com.mhcockpit.vlpipe/Editor/VLabBuildAndUpload.cs
// Menu: Tools → Virtual Lab → Pipeline → Build And Upload
//
// SECURITY: AWS credentials are NEVER stored in this file.
//           Local  → Tools → Virtual Lab → AWS Settings  (saves to EditorPrefs)
//           CI/CD  → GitHub Actions environment variables (AWS_ACCESS_KEY_ID etc.)

#if UNITY_EDITOR && ADDRESSABLES_INSTALLED

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MHCockpit.VLPipe.Editor
{
    /// <summary>
    /// Full CI/CD pipeline for Virtual Lab Addressables:
    ///   Step 1 — Clean Build Cache          (VLabPreBuildCleaner)
    ///   Step 2 — Ensure WebGL platform
    ///   Step 3 — Build Addressables
    ///   Step 4 — Upload all output files to S3
    ///
    /// AWS credentials are read from the following environment variables ONLY:
    ///   AWS_ACCESS_KEY_ID      — IAM access key
    ///   AWS_SECRET_ACCESS_KEY  — IAM secret key
    ///   AWS_REGION             — e.g. ap-south-1  (falls back to S3_DEFAULT_REGION)
    ///
    /// If any required credential variable is absent the pipeline throws immediately
    /// with a descriptive message rather than silently failing mid-upload.
    /// </summary>
    public static class VLabBuildAndUpload
    {
        // ═════════════════════════════════════════════════════════════════════
        //  S3 CONFIGURATION  — non-sensitive values only
        // ═════════════════════════════════════════════════════════════════════

        private const string S3_BUCKET           = "mhc-embibe-test";
        private const string S3_DEFAULT_REGION   = "ap-south-1";

        // ── Credential resolution ──────────────────────────────────────────
        //
        // Priority order:
        //   1. VLabSettings (EditorPrefs) — entered via Tools → Virtual Lab → AWS Settings
        //   2. Environment variables      — GitHub Actions CI / shell
        //   3. Throw with clear message   — tells user exactly where to go

        private static string AwsAccessKey
        {
            get
            {
                // 1. EditorPrefs — set via the AWS Settings window
                string prefs = VLabSettings.GetAccessKeyId();
                if (!string.IsNullOrWhiteSpace(prefs)) return prefs;

                // 2. Environment variable — GitHub Actions CI
                string env = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
                if (!string.IsNullOrWhiteSpace(env)) return env;

                // 3. Nothing found — tell the user exactly where to go
                throw new InvalidOperationException(
                    "[VLab S3] AWS credentials not found.\n" +
                    "Open  Tools → Virtual Lab → AWS Settings  and enter your credentials.");
            }
        }

        private static string AwsSecretKey
        {
            get
            {
                // 1. EditorPrefs
                string prefs = VLabSettings.GetSecretKey();
                if (!string.IsNullOrWhiteSpace(prefs)) return prefs;

                // 2. Environment variable
                string env = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
                if (!string.IsNullOrWhiteSpace(env)) return env;

                // 3. Nothing found
                throw new InvalidOperationException(
                    "[VLab S3] AWS credentials not found.\n" +
                    "Open  Tools → Virtual Lab → AWS Settings  and enter your credentials.");
            }
        }

        /// <summary>
        /// Region: EditorPrefs → environment variable → default (ap-south-1).
        /// </summary>
        private static string AwsRegion
        {
            get
            {
                string prefs = VLabSettings.GetRegion();
                if (!string.IsNullOrWhiteSpace(prefs)) return prefs;

                return Environment.GetEnvironmentVariable("AWS_REGION") ?? S3_DEFAULT_REGION;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  PATH CONSTANTS
        // ═════════════════════════════════════════════════════════════════════

        private const string BUILD_ROOT             = "ServerData";
        private const string MODULES_ROOT           = "Assets/Modules";
        private const string MODULE_CONFIG_FILENAME = "module_config.json";
        private const string MONOSCRIPTS_FRAGMENT   = "monoscripts";
        private const string BUILTIN_FRAGMENT       = "unitybuiltinassets";
        private const string JSON_CATALOG_DEFINE    = "ENABLE_JSON_CATALOG";

        private readonly struct UploadFile
        {
            public readonly string LocalPath;
            public readonly string RelativePath;

            public UploadFile(string localPath, string relativePath)
            {
                LocalPath    = localPath;
                RelativePath = relativePath;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  MENU ITEMS
        // ═════════════════════════════════════════════════════════════════════

        [MenuItem("Tools/Virtual Lab/Pipeline/Build And Upload To S3", false, 20)]
        public static void TriggerBuildAndUpload() => _ = RunPipelineAsync();

        [MenuItem("Tools/Virtual Lab/Pipeline/Build And Upload To S3", validate = true)]
        public static bool ValidateTrigger() =>
            !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        /// <summary>Opens the AWS Settings window directly.</summary>
        private static void Open() => VLabSettings.Open();

        // ═════════════════════════════════════════════════════════════════════
        //  PIPELINE ENTRY POINT
        // ═════════════════════════════════════════════════════════════════════

        private static async Task RunPipelineAsync()
        {
            Debug.Log("[VLab S3] ════════════════════════════════════════════");
            Debug.Log("[VLab S3]  Virtual Lab — Build & Upload to S3");
            Debug.Log("[VLab S3] ════════════════════════════════════════════");

            try
            {
                // ── Step 1: Clean Build Cache ──────────────────────────────
                ShowProgress("Step 1 — Cleaning build cache…", 0.01f);
                VLabPreBuildCleaner.CleanBuildCache();

                // ── Step 2: Read module metadata ───────────────────────────
                ShowProgress("Step 2 — Reading module config…", 0.04f);
                ModuleConfig config = ReadModuleConfig();
                if (config == null) { ClearProgress(); return; }

                // ── Step 3: Ensure active platform is WebGL ────────────────
                ShowProgress("Step 3 — Checking build platform…", 0.07f);
                if (!EnsureWebGLPlatform()) { ClearProgress(); return; }

                // ── Step 4: Build Addressables ─────────────────────────────
                ShowProgress("Step 4 — Building Addressables — please wait…", 0.12f);
                string buildOutputFolder = ExecuteAddressablesBuild();
                if (buildOutputFolder == null) { ClearProgress(); return; }

                // ── Step 5: Upload files to S3 ─────────────────────────────
                // Credential validation happens inside AwsAccessKey/AwsSecretKey
                // properties and will throw before any AWS call is made.
                string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
                string s3Prefix    = BuildS3Prefix(config, buildTarget);
                Debug.Log($"[VLab S3] Destination: s3://{S3_BUCKET}/{s3Prefix}");

                await UploadFolderAsync(buildOutputFolder, s3Prefix);
            }
            catch (InvalidOperationException credEx)
            {
                Debug.LogError(credEx.Message);
                EditorUtility.DisplayDialog(
                    "AWS Credentials Missing",
                    credEx.Message +
                    "\n\nGo to:  Tools → Virtual Lab → AWS Settings\n" +
                    "Enter your Access Key ID and Secret Access Key, then click Save.",
                    "Open Settings");
                Open();   // open the settings window automatically
            }
            catch (AmazonS3Exception s3Ex)
            {
                Debug.LogError($"[VLab S3] S3 error ({s3Ex.ErrorCode}): {s3Ex.Message}");
                EditorUtility.DisplayDialog("S3 Error",
                    $"Error Code : {s3Ex.ErrorCode}\nMessage    : {s3Ex.Message}", "OK");
            }
            catch (AmazonClientException clientEx)
            {
                Debug.LogError($"[VLab S3] AWS client error: {clientEx.Message}");
                EditorUtility.DisplayDialog("AWS Error", clientEx.Message, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VLab S3] Unexpected error: {ex}");
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
            finally
            {
                ClearProgress();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 2 — READ MODULE CONFIG
        // ═════════════════════════════════════════════════════════════════════

        [Serializable]
        private class ModuleConfig
        {
            public string board, grade, subject, topic, createdDate;
        }

        private static ModuleConfig ReadModuleConfig()
        {
            string modulesAbsPath = Path.Combine(Application.dataPath, "Modules");

            if (!Directory.Exists(modulesAbsPath))
            {
                Debug.LogError($"[VLab S3] '{MODULES_ROOT}' not found. " +
                               "Run Project Setup Wizard Step 2 first.");
                return null;
            }

            string[] configFiles = Directory.GetFiles(
                modulesAbsPath, MODULE_CONFIG_FILENAME, SearchOption.AllDirectories);

            if (configFiles.Length == 0)
            {
                Debug.LogError($"[VLab S3] No '{MODULE_CONFIG_FILENAME}' found. " +
                               "Run Project Setup Wizard Step 2 first.");
                return null;
            }

            if (configFiles.Length > 1)
                Debug.LogWarning($"[VLab S3] {configFiles.Length} module configs found — " +
                                 $"using: {configFiles[0]}");

            var config = JsonUtility.FromJson<ModuleConfig>(File.ReadAllText(configFiles[0]));

            if (config == null
                || string.IsNullOrEmpty(config.board)
                || string.IsNullOrEmpty(config.grade)
                || string.IsNullOrEmpty(config.subject)
                || string.IsNullOrEmpty(config.topic))
            {
                Debug.LogError("[VLab S3] module_config.json is corrupt. " +
                               "Re-run Setup Wizard Step 2.");
                return null;
            }

            Debug.Log($"[VLab S3] Module: {config.board} / {config.grade} / " +
                      $"{config.subject} / {config.topic}");
            return config;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 3 — ENSURE WEBGL PLATFORM
        // ═════════════════════════════════════════════════════════════════════

        private static bool EnsureWebGLPlatform()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                Debug.Log("[VLab S3] Platform: WebGL ✓");
                return true;
            }

            string current = EditorUserBuildSettings.activeBuildTarget.ToString();
            Debug.LogWarning($"[VLab S3] Active platform is '{current}', not WebGL.");

            bool confirm = EditorUtility.DisplayDialog(
                "Switch Platform to WebGL?",
                $"Current platform: {current}\n\n" +
                "Addressables must be built for WebGL.\n" +
                "Switch now? (This may take a minute while Unity reimports assets.)",
                "Switch to WebGL",
                "Cancel");

            if (!confirm)
            {
                Debug.LogWarning("[VLab S3] Platform switch cancelled — pipeline aborted.");
                return false;
            }

            ShowProgress("Switching platform to WebGL — reimporting assets…", 0.09f);
            Debug.Log("[VLab S3] Switching to WebGL…");

            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.WebGL, BuildTarget.WebGL);

            if (!ok)
            {
                Debug.LogError("[VLab S3] Platform switch to WebGL failed.");
                EditorUtility.DisplayDialog("Platform Switch Failed",
                    "Unity could not switch to WebGL. " +
                    "Check that the WebGL Build Support module is installed " +
                    "via Unity Hub → Installs → Add Modules.", "OK");
                return false;
            }

            Debug.Log("[VLab S3] Platform switched to WebGL. ✓");
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 4 — ADDRESSABLES BUILD
        // ═════════════════════════════════════════════════════════════════════

        private static string ExecuteAddressablesBuild()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[VLab S3] Addressables Settings not found. " +
                               "Run the Project Setup Wizard first.");
                return null;
            }

            // Enforce remote catalog JSON output every run.
            bool changed = false;
            if (!settings.BuildRemoteCatalog)
            {
                settings.BuildRemoteCatalog = true;
                changed = true;
            }
            if (!settings.EnableJsonCatalog)
            {
                settings.EnableJsonCatalog = true;
                changed = true;
            }
            bool defineChanged = EnsureJsonCatalogDefine();
            if (changed)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[VLab S3] Enabled BuildRemoteCatalog + EnableJsonCatalog before build.");
            }
            if (defineChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.LogWarning(
                    "[VLab S3] Added scripting define 'ENABLE_JSON_CATALOG' for the active target.\n" +
                    "Unity must recompile before JSON catalogs can be generated.\n" +
                    "Run Build And Upload again after compilation completes.");
                EditorUtility.DisplayDialog(
                    "Recompile Required",
                    "Added scripting define ENABLE_JSON_CATALOG.\n\n" +
                    "Wait for Unity to finish compiling, then run:\n" +
                    "Tools > Virtual Lab > Pipeline > Build And Upload To S3",
                    "OK");
                return null;
            }
            if (changed)
                AssetDatabase.Refresh();

            // Clean first so stale bundles from a previous platform are removed.
            ShowProgress("Cleaning previous Addressables output…", 0.14f);
            Debug.Log("[VLab S3] Cleaning previous build…");
            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);

            ShowProgress("Building Addressables — this may take several minutes…", 0.20f);
            Debug.Log("[VLab S3] Building Addressables…");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError($"[VLab S3] Addressables build failed:\n{result.Error}");
                EditorUtility.DisplayDialog("Build Failed",
                    $"Addressables build failed:\n\n{result.Error}", "OK");
                return null;
            }

            Debug.Log($"[VLab S3] Build completed in {result.Duration:F2}s. ✓");

            string projectRoot  = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildTarget  = EditorUserBuildSettings.activeBuildTarget.ToString();
            string outputFolder = Path.Combine(projectRoot, BUILD_ROOT, buildTarget);

            if (!Directory.Exists(outputFolder))
            {
                Debug.LogError($"[VLab S3] Output folder not found: {outputFolder}\n" +
                               "Verify Remote.BuildPath = 'ServerData/[BuildTarget]' " +
                               "in your Addressables profile (Setup Wizard Step 4).");
                return null;
            }

            string[] jsonCatalogFiles = Directory.GetFiles(
                outputFolder, "catalog*.json", SearchOption.AllDirectories);

            if (jsonCatalogFiles.Length > 0)
            {
                Debug.Log("[VLab S3] Catalog JSON detected:\n" +
                          $"  {jsonCatalogFiles[0]}");
            }
            else
            {
                string[] binCatalogFiles = Directory.GetFiles(
                    outputFolder, "catalog*.bin", SearchOption.AllDirectories);

                if (binCatalogFiles.Length > 0)
                {
                    Debug.LogError(
                        "[VLab S3] JSON catalog not found. Binary catalog was produced instead:\n" +
                        $"  {Path.GetFileName(binCatalogFiles[0])}\n\n" +
                        "This upload is aborted because the parent bootstrap loads JSON catalogs.\n" +
                        "Fix:\n" +
                        "  1. Run Tools > Virtual Lab > Project Setup > Step 5.\n" +
                        "  2. Ensure Addressables Catalog > Enable Json Catalog is ON.\n" +
                        "  3. Rebuild and upload.");
                    return null;
                }

                Debug.LogError(
                    $"[VLab S3] No catalog file found in build output: {outputFolder}\n" +
                    "Expected one of:\n" +
                    "  catalog*.json\n" +
                    "  catalog*.bin\n" +
                    "Check Addressables global catalog settings in Setup Wizard Step 5.");
                return null;
            }

            int count = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories).Length;
            Debug.Log($"[VLab S3] Output folder: {outputFolder}  ({count} file(s))");
            ShowProgress($"Build complete — {count} file(s) ready to upload.", 0.30f);
            return outputFolder;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 5 — S3 UPLOAD
        // ═════════════════════════════════════════════════════════════════════

        private static string BuildS3Prefix(ModuleConfig cfg, string buildTarget) =>
            $"Modules/{cfg.board}/{cfg.grade}/{cfg.subject}/{VLabProjectSetup.ToAddressableKey(cfg.topic)}/{buildTarget}/";

        private static async Task UploadFolderAsync(string localFolder, string s3Prefix)
        {
            List<UploadFile> uploadFiles = BuildUploadManifest(localFolder);

            if (uploadFiles.Count == 0)
            {
                Debug.LogWarning("[VLab S3] Build output folder is empty — nothing to upload.");
                return;
            }

            Debug.Log($"[VLab S3] Uploading {uploadFiles.Count} file(s)…");
            Debug.Log($"[VLab S3] Bucket : {S3_BUCKET}");
            Debug.Log($"[VLab S3] Region : {AwsRegion}");
            Debug.Log($"[VLab S3] Prefix : {s3Prefix}");
            Debug.Log("[VLab S3] ────────────────────────────────────────────");

            // Credentials are resolved here — throws if missing, before any
            // network call is ever attempted.
            var credentials = new BasicAWSCredentials(AwsAccessKey, AwsSecretKey);
            using var s3Client = new AmazonS3Client(
                credentials, RegionEndpoint.GetBySystemName(AwsRegion));
            using var transfer = new TransferUtility(s3Client);

            var succeeded  = new List<string>();
            var failedList = new List<string>();

            const float UPLOAD_START = 0.32f;
            const float UPLOAD_RANGE = 0.68f;
            float       slicePerFile = UPLOAD_RANGE / Math.Max(uploadFiles.Count, 1);

            for (int i = 0; i < uploadFiles.Count; i++)
            {
                UploadFile item       = uploadFiles[i];
                string     filePath   = item.LocalPath;
                string     fileName   = Path.GetFileName(filePath);
                string     s3Key      = s3Prefix + item.RelativePath;
                long       fileSize   = new FileInfo(filePath).Length;
                float      fileBase   = UPLOAD_START + i * slicePerFile;

                ShowProgress(
                    $"Uploading  [{i + 1} / {uploadFiles.Count}]  {fileName}",
                    fileBase,
                    $"s3://{S3_BUCKET}/{s3Key}  ({FormatBytes(fileSize)})");

                Debug.Log($"[VLab S3] [{i + 1}/{uploadFiles.Count}] {fileName}" +
                          $"  ({FormatBytes(fileSize)})  →  {s3Key}");

                try
                {
                    long lastReported = 0;
                    long reportEvery  = Math.Max(1, fileSize / 20);

                    var request = new TransferUtilityUploadRequest
                    {
                        BucketName  = S3_BUCKET,
                        FilePath    = filePath,
                        Key         = s3Key,
                        ContentType = ResolveContentType(filePath)
                    };

                    request.UploadProgressEvent += (_, args) =>
                    {
                        if (args.TransferredBytes - lastReported < reportEvery) return;
                        lastReported = args.TransferredBytes;

                        float withinFile = fileSize > 0
                            ? (float)args.TransferredBytes / fileSize : 1f;
                        float overall = fileBase + withinFile * slicePerFile;

                        ShowProgress(
                            $"Uploading  [{i + 1} / {uploadFiles.Count}]  " +
                            $"{fileName}  {args.PercentDone}%",
                            overall,
                            $"{FormatBytes(args.TransferredBytes)} / {FormatBytes(fileSize)}" +
                            $"   →   s3://{S3_BUCKET}/{s3Key}");
                    };

                    await transfer.UploadAsync(request);

                    succeeded.Add(fileName);
                    Debug.Log($"[VLab S3]   ✓  {fileName}  [{FormatBytes(fileSize)}]");
                }
                catch (AmazonS3Exception s3Ex)
                {
                    failedList.Add(fileName);
                    Debug.LogError($"[VLab S3]   ✗  {fileName} — " +
                                   $"S3 {s3Ex.ErrorCode}: {s3Ex.Message}");
                }
                catch (Exception ex)
                {
                    failedList.Add(fileName);
                    Debug.LogError($"[VLab S3]   ✗  {fileName} — {ex.Message}");
                }
            }

            Debug.Log("[VLab S3] ────────────────────────────────────────────");

            if (failedList.Count == 0)
            {
                ShowProgress("Upload complete! ✓", 1f,
                    $"{succeeded.Count} file(s) → s3://{S3_BUCKET}/{s3Prefix}");

                Debug.Log($"[VLab S3] ✓ Upload completed successfully.\n" +
                          $"  {succeeded.Count} file(s) → s3://{S3_BUCKET}/{s3Prefix}");

                await Task.Delay(1200);
                EditorUtility.DisplayDialog(
                    "Upload Complete ✓",
                    $"All {succeeded.Count} file(s) uploaded successfully.\n\n" +
                    $"Bucket : {S3_BUCKET}\n" +
                    $"Prefix : {s3Prefix}",
                    "OK");
            }
            else
            {
                ShowProgress($"Finished with {failedList.Count} error(s).", 1f,
                    $"Succeeded: {succeeded.Count}   Failed: {failedList.Count}");

                Debug.LogWarning($"[VLab S3] Upload finished with errors.\n" +
                                 $"  Succeeded : {succeeded.Count}\n" +
                                 $"  Failed    : {failedList.Count}\n" +
                                 $"  Files     : {string.Join(", ", failedList)}");

                await Task.Delay(1200);
                EditorUtility.DisplayDialog(
                    "Upload Finished with Errors",
                    $"Succeeded : {succeeded.Count}\n" +
                    $"Failed    : {failedList.Count}\n\n" +
                    $"Failed files:\n  {string.Join("\n  ", failedList)}\n\n" +
                    "See the Console window for full error details.",
                    "OK");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  UPLOAD MANIFEST
        // ═════════════════════════════════════════════════════════════════════

        private static List<UploadFile> BuildUploadManifest(string localFolder)
        {
            var manifest = new List<UploadFile>();

            if (!Directory.Exists(localFolder))
                return manifest;

            string normalisedRoot = localFolder.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (string filePath in Directory.GetFiles(
                         localFolder, "*", SearchOption.AllDirectories))
            {
                string relativePath = filePath
                    .Substring(normalisedRoot.Length + 1)
                    .Replace('\\', '/');

                manifest.Add(new UploadFile(filePath, relativePath));
            }

            AppendFallbackBuiltInBundles(manifest);
            return manifest;
        }

        private static void AppendFallbackBuiltInBundles(List<UploadFile> manifest)
        {
            bool hasMonoscripts = manifest.Any(f =>
                Path.GetFileName(f.LocalPath)
                    .IndexOf(MONOSCRIPTS_FRAGMENT, StringComparison.OrdinalIgnoreCase) >= 0);

            bool hasBuiltinAssets = manifest.Any(f =>
                Path.GetFileName(f.LocalPath)
                    .IndexOf(BUILTIN_FRAGMENT, StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasMonoscripts && hasBuiltinAssets)
                return;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
            string fallbackDir = Path.Combine(
                projectRoot, "Library", "com.unity.addressables", "aa", buildTarget);

            if (!Directory.Exists(fallbackDir))
            {
                Debug.LogWarning(
                    $"[VLab S3] Fallback bundle folder not found: {fallbackDir}\n" +
                    "If child bundles fail to load at runtime, run Setup Wizard Step 5 " +
                    "to force the Default Local Group to Remote paths, then rebuild.");
                return;
            }

            string[] candidates = Directory.GetFiles(
                fallbackDir, "*.bundle", SearchOption.AllDirectories);

            foreach (string bundlePath in candidates)
            {
                string fileName      = Path.GetFileName(bundlePath);
                bool   isMonoscripts = fileName.IndexOf(MONOSCRIPTS_FRAGMENT, StringComparison.OrdinalIgnoreCase) >= 0;
                bool   isBuiltin     = fileName.IndexOf(BUILTIN_FRAGMENT,     StringComparison.OrdinalIgnoreCase) >= 0;
                bool   needed        = (!hasMonoscripts && isMonoscripts) || (!hasBuiltinAssets && isBuiltin);

                if (!needed)
                    continue;

                bool alreadyPresent = manifest.Any(f =>
                    Path.GetFileName(f.LocalPath).Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (alreadyPresent)
                    continue;

                manifest.Add(new UploadFile(bundlePath, fileName));
                Debug.Log($"[VLab S3] Added fallback built-in bundle: {fileName}");
            }

            bool stillMissingMonoscripts = !manifest.Any(f =>
                Path.GetFileName(f.LocalPath)
                    .IndexOf(MONOSCRIPTS_FRAGMENT, StringComparison.OrdinalIgnoreCase) >= 0);

            bool stillMissingBuiltin = !manifest.Any(f =>
                Path.GetFileName(f.LocalPath)
                    .IndexOf(BUILTIN_FRAGMENT, StringComparison.OrdinalIgnoreCase) >= 0);

            if (stillMissingMonoscripts || stillMissingBuiltin)
            {
                Debug.LogWarning(
                    "[VLab S3] Built-in dependency bundles are still missing from upload manifest.\n" +
                    "This can cause remote scene load failures. Re-run Project Setup Step 5 and rebuild.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  EDITOR PROGRESS BAR
        // ═════════════════════════════════════════════════════════════════════

        private static void ShowProgress(string title, float progress, string info = "")
        {
            string body = string.IsNullOrEmpty(info) ? title : $"{title}\n{info}";
            EditorUtility.DisplayProgressBar(
                "Virtual Lab — Build & Upload to S3",
                body,
                Mathf.Clamp01(progress));
        }

        private static void ClearProgress() =>
            EditorUtility.ClearProgressBar();

        // ═════════════════════════════════════════════════════════════════════
        //  UTILITIES
        // ═════════════════════════════════════════════════════════════════════

        private static string ResolveContentType(string filePath) =>
            Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".json"     => "application/json",
                ".hash"     => "text/plain",
                ".bundle"   => "application/octet-stream",
                ".data"     => "application/octet-stream",
                ".js"       => "application/javascript",
                ".wasm"     => "application/wasm",
                ".unityweb" => "application/octet-stream",
                ".br"       => "application/x-brotli",
                ".gz"       => "application/gzip",
                ".xml"      => "application/xml",
                _           => "application/octet-stream"
            };

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)     return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }

        private static bool EnsureJsonCatalogDefine()
        {
#if UNITY_6000_0_OR_NEWER
            var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            if (HasDefine(current, JSON_CATALOG_DEFINE))
                return false;

            PlayerSettings.SetScriptingDefineSymbols(target,
                string.IsNullOrEmpty(current) ? JSON_CATALOG_DEFINE : current + ";" + JSON_CATALOG_DEFINE);
            return true;
#else
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            if (HasDefine(current, JSON_CATALOG_DEFINE))
                return false;

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group,
                string.IsNullOrEmpty(current) ? JSON_CATALOG_DEFINE : current + ";" + JSON_CATALOG_DEFINE);
            return true;
#endif
        }

        private static bool HasDefine(string defines, string targetDefine)
        {
            if (string.IsNullOrWhiteSpace(defines))
                return false;

            foreach (string part in defines.Split(';'))
                if (part.Trim().Equals(targetDefine, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}

#endif // UNITY_EDITOR && ADDRESSABLES_INSTALLED