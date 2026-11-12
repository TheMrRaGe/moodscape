using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NightPages.EditorTools
{
    /// <summary>
    /// Builds the Windows game from the command line, with no editor window:
    ///   Unity.exe -batchmode -quit -projectPath &lt;night-pages&gt; -executeMethod NightPages.EditorTools.NightPagesBuild.Run -npexe Build/NightPages.exe
    /// It also sets up what a game built entirely from code needs: an empty start-up scene, fog kept in the
    /// build (no scene uses it, so Unity would otherwise strip it), the unlit shader the paintings use, and a
    /// glowing material so emission isn't stripped either. tools/unity-agent runs this for you.
    /// </summary>
    public static class NightPagesBuild
    {
        const string Scene = "Assets/NightPages/Boot.unity";
        const string Keep = "Assets/NightPages/Resources/NPKeep";

        static string Arg(string k, string d)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, k);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : d;
        }

        public static void Run()
        {
            string exe = Arg("-npexe", "Build/NightPages.exe");
            Prepare();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log("[NightPages] BUILD " + s.result + ": " + s.totalErrors + " errors, " + s.totalWarnings + " warnings, " +
                      (s.totalSize / 1048576f).ToString("0") + " MB, " + s.totalTime.TotalSeconds.ToString("0") + " s -> " + exe);
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        // The post-processing package's shaders and textures, referenced from Resources so the build includes them.
        static void KeepPostProcessing()
        {
            var res = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.PostProcessing.PostProcessResources>(
                "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
            if (res == null) { Debug.LogWarning("[NightPages] BUILD note: com.unity.postprocessing isn't installed; no camera effects"); return; }
            Directory.CreateDirectory(Keep);
            string path = Keep + "/PPResources.asset";
            var holder = AssetDatabase.LoadAssetAtPath<PPHolder>(path);
            if (holder == null)
            {
                holder = ScriptableObject.CreateInstance<PPHolder>();
                AssetDatabase.CreateAsset(holder, path);
            }
            holder.resources = res;
            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Night Pages/Prepare project for building")]
        public static void Prepare()
        {
            if (!File.Exists(Scene))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, Scene);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };

            PlayerSettings.productName = "Night Pages";
            PlayerSettings.companyName = "Heath Dale";
            PlayerSettings.runInBackground = true;          // keeps going while you work in another window
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            // Linear lighting: the photo textures and the filmic post-processing are made for it.
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            KeepPostProcessing();

            // Fog and the unlit shader: kept in the build even though no scene asset uses them.
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs.Length > 0)
            {
                var so = new SerializedObject(gs[0]);
                Set(so, "m_FogStripping", 1);
                SetBool(so, "m_FogKeepLinear", true);
                SetBool(so, "m_FogKeepExp", true);
                SetBool(so, "m_FogKeepExp2", true);
                var always = so.FindProperty("m_AlwaysIncludedShaders");
                // Unlit (the paintings' fallbacks) and the 3D text shader (words in the dust, the dream's dial).
                foreach (var shaderName in new[] { "Unlit/Texture", "GUI/Text Shader" })
                {
                    var sh = Shader.Find(shaderName);
                    if (always == null || sh == null) continue;
                    bool has = false;
                    for (int i = 0; i < always.arraySize; i++) if (always.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                    if (!has)
                    {
                        always.InsertArrayElementAtIndex(always.arraySize);
                        always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = sh;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Materials the game makes at run time switch emission on; keep that shader variant in the build.
            Directory.CreateDirectory(Keep);
            string glowPath = Keep + "/Glow.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(glowPath) == null)
            {
                var glow = new Material(Shader.Find("Standard"));
                glow.EnableKeyword("_EMISSION");
                glow.SetColor("_EmissionColor", Color.white);
                AssetDatabase.CreateAsset(glow, glowPath);
            }
            string bumpPath = Keep + "/Bumped.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(bumpPath) == null)
            {
                var bumped = new Material(Shader.Find("Standard"));
                bumped.EnableKeyword("_NORMALMAP");
                AssetDatabase.CreateAsset(bumped, bumpPath);
                var both = new Material(Shader.Find("Standard"));
                both.EnableKeyword("_NORMALMAP");
                both.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(both, Keep + "/BumpedGlow.mat");
            }
            string cutPath = Keep + "/Cutout.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(cutPath) == null)
            {
                var cut = new Material(Shader.Find("Standard"));
                cut.SetFloat("_Mode", 1f);
                cut.EnableKeyword("_ALPHATEST_ON");
                cut.SetOverrideTag("RenderType", "TransparentCutout");
                cut.renderQueue = 2450;
                AssetDatabase.CreateAsset(cut, cutPath);
            }
            // Models get their normal and metal/smoothness maps at run time (Models.Surface): keep those variants.
            foreach (var combo in new[] { new[] { "_METALLICGLOSSMAP" }, new[] { "_NORMALMAP", "_METALLICGLOSSMAP" }, new[] { "_NORMALMAP", "_METALLICGLOSSMAP", "_EMISSION" } })
            {
                string path = Keep + "/Surface_" + string.Join("_", combo).Replace("_", "") + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                var m = new Material(Shader.Find("Standard"));
                foreach (var k in combo) m.EnableKeyword(k);
                AssetDatabase.CreateAsset(m, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[NightPages] project prepared for building");
        }

        static void Set(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
    }
}
