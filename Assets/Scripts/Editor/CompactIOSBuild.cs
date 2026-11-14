using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MDPro3.Editor
{
    public static class CompactIOSBuild
    {
        [MenuItem("Build/Compact iOS/Export Xcode Project")]
        public static void Export()
        {
            const string native = "Assets/Plugins/iOS/libMDPro3Native.a";
            bool linkOnRunner = Environment.GetEnvironmentVariable("MDPRO3_LINK_NATIVE_ON_RUNNER") == "1";
            if (!linkOnRunner && !File.Exists(native)) throw new BuildFailedException("Build Native/libMDPro3Native.a on macOS and copy it to " + native);
            if (!File.Exists("Assets/StreamingAssets/CompactData/manifest.json"))
                throw new BuildFailedException("Run python Tools/Compact/prepare_resources.py before Unity export.");
            if (!linkOnRunner)
            {
                var importer = AssetImporter.GetAtPath(native) as PluginImporter;
                if (importer == null) { AssetDatabase.ImportAsset(native); importer = AssetImporter.GetAtPath(native) as PluginImporter; }
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                importer.SaveAndReimport();
            }
            // All three card loaders use the callback-free read-only provider on iOS.
            var legacySqlite = AssetImporter.GetAtPath("Assets/Plugins/Managed/Mono.Data.Sqlite.dll") as PluginImporter;
            if (legacySqlite != null) { legacySqlite.SetCompatibleWithPlatform(BuildTarget.iOS, false); legacySqlite.SaveAndReimport(); }

            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS).Split(';').ToList();
            if (!defines.Contains("MDPRO3_COMPACT")) defines.Add("MDPRO3_COMPACT");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, string.Join(";", defines));
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,
                Environment.GetEnvironmentVariable("MDPRO3_BUNDLE_ID") ?? "dev.mdpro3.compact");
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);
            PlayerSettings.productName = "MDPro3 Compact";
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { UnityEngine.Rendering.GraphicsDeviceType.Metal });
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            // Use Addressables' player staging path. The upstream desktop profile writes to Platforms/.
            settings.profileSettings.SetValue(settings.activeProfileId, "Local.BuildPath",
                "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]");
            settings.profileSettings.SetValue(settings.activeProfileId, "Local.LoadPath",
                "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]");
            foreach (var group in settings.groups.Where(g => g != null && (g.Name == "Icon" || g.Name == "Sound")))
            {
                var missing = group.entries.Where(e => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.guid))).ToArray();
                foreach (var entry in missing) settings.RemoveAssetEntry(entry.guid);
                Debug.Log($"Compact iOS Addressables {group.Name}: kept {group.entries.Count} entries, removed {missing.Length} unavailable entries.");
            }
            AddressableAssetSettings.BuildPlayerContent(out var content);
            if (!string.IsNullOrEmpty(content.Error)) throw new BuildFailedException(content.Error);
            var output = Environment.GetEnvironmentVariable("MDPRO3_XCODE_OUTPUT") ?? "Artifacts/iOS";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.iOS,
                extraScriptingDefines = new[] { "MDPRO3_COMPACT" },
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            var embeddedAddressables = Path.Combine(output, "Data", "Raw", "aa");
            if (!Directory.Exists(embeddedAddressables)
                || !Directory.EnumerateFiles(embeddedAddressables, "scene_scenes_scenemain.bundle", SearchOption.AllDirectories).Any())
                throw new BuildFailedException("The Xcode export is missing the local SceneMain Addressables bundle.");
            Debug.Log("Xcode project exported. Final .app storage gate is enforced after xcodebuild, not against generated C++ source size.");
        }

        [PostProcessBuild(1000)]
        public static void ConfigureXcode(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject(); project.ReadFromFile(projectPath);
            var framework = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework, "CoreFoundation.framework", false);
            var z = project.AddFile("usr/lib/libz.tbd", "Frameworks/libz.tbd", PBXSourceTree.Sdk);
            project.AddFileToBuild(framework, z);
            string relative = "Libraries/libMDPro3Native.a";
            if (Environment.GetEnvironmentVariable("MDPRO3_LINK_NATIVE_ON_RUNNER") != "1")
            {
                var libraries = Directory.GetFiles(path, "libMDPro3Native.a", SearchOption.AllDirectories);
                if (libraries.Length != 1) throw new BuildFailedException("Expected exactly one merged MDPro3 native archive.");
                relative = Path.GetRelativePath(path, libraries[0]).Replace('\\', '/');
            }
            project.AddBuildProperty(framework, "OTHER_LDFLAGS", "-force_load \"$(PROJECT_DIR)/" + relative + "\"");
            project.SetBuildProperty(framework, "ENABLE_BITCODE", "NO");
            project.WriteToFile(projectPath);
            var plist = new PlistDocument(); var plistPath = Path.Combine(path, "Info.plist");
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("UIFileSharingEnabled", true);
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            plist.WriteToFile(plistPath);
        }
    }
}
