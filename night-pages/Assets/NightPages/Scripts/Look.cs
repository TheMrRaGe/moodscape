using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace NightPages
{
    /// <summary>
    /// How the game looks through the lens: filmic tonemapping, ambient occlusion in the corners, bloom on lamps,
    /// eyes that adjust when you step from daylight into a dim house, grain, vignette. It also reacts: fear
    /// tightens the vignette and drains the colour; a scare hits the image (the picture splits at the edges,
    /// the light dips) and lets go over a second or two.
    /// </summary>
    public class Look : MonoBehaviour
    {
        public static Look I;
        const int VolumeLayer = 30;

        PostProcessLayer layer;
        PostProcessVolume volume;
        AmbientOcclusion ao;
        Bloom bloom;
        ColorGrading grade;
        Vignette vignette;
        Grain grain;
        ChromaticAberration ca;
        AutoExposure exposure;

        float kick;                 // 0..1, the last scare, fading
        float fear;                 // 0..1, how frightened the game thinks you are
        int mode;                   // 0 = house by day, 1 = evening, 2 = outside, 3 = dream, 4 = cellar

        public static void ResetStatics() { I = null; }

        /// <summary>Set up the camera effects and the quality settings that go with them.</summary>
        public static Look Attach(Camera cam)
        {
            Quality();
            var holder = Resources.Load<PPHolder>("NPKeep/PPResources");
            if (holder == null || holder.resources == null)
            {
                Debug.LogWarning("[NightPages] post-processing resources missing (rebuild with the build script); plain camera");
                return null;
            }
            cam.allowHDR = true;
            cam.allowMSAA = false;              // SMAA in the post stack instead
            cam.depthTextureMode |= DepthTextureMode.Depth;
            var look = cam.gameObject.AddComponent<Look>();
            I = look;
            look.layer = cam.gameObject.AddComponent<PostProcessLayer>();
            look.layer.Init(holder.resources);
            look.layer.volumeLayer = 1 << VolumeLayer;
            look.layer.volumeTrigger = cam.transform;
            look.layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            look.layer.subpixelMorphologicalAntialiasing.quality = SubpixelMorphologicalAntialiasing.Quality.High;
            look.layer.stopNaNPropagation = true;
            look.Build();
            return look;
        }

        static void Quality()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 60f;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.pixelLightCount = 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = 2f;
            QualitySettings.softParticles = true;
        }

        void Build()
        {
            ao = ScriptableObject.CreateInstance<AmbientOcclusion>();
            ao.enabled.Override(true);
            ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
            ao.intensity.Override(1.1f);
            ao.thicknessModifier.Override(1.6f);
            ao.color.Override(new Color(0.02f, 0.015f, 0.01f));

            bloom = ScriptableObject.CreateInstance<Bloom>();
            bloom.enabled.Override(true);
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(1.05f);
            bloom.softKnee.Override(0.6f);
            bloom.diffusion.Override(7.5f);
            bloom.color.Override(new Color(1f, 0.9f, 0.78f));

            grade = ScriptableObject.CreateInstance<ColorGrading>();
            grade.enabled.Override(true);
            grade.gradingMode.Override(GradingMode.HighDefinitionRange);
            grade.tonemapper.Override(Tonemapper.ACES);
            grade.postExposure.Override(0.6f);
            grade.contrast.Override(12f);
            grade.saturation.Override(-12f);
            grade.temperature.Override(6f);
            grade.lift.Override(new Vector4(1f, 1f, 1.04f, -0.02f));        // blacks a little cold and crushed
            grade.gamma.Override(new Vector4(1f, 0.99f, 0.97f, 0f));
            grade.gain.Override(new Vector4(1.02f, 1f, 0.96f, 0f));

            vignette = ScriptableObject.CreateInstance<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.42f);
            vignette.roundness.Override(0.85f);
            vignette.color.Override(new Color(0.02f, 0.01f, 0.01f));

            grain = ScriptableObject.CreateInstance<Grain>();
            grain.enabled.Override(true);
            grain.intensity.Override(0.32f);
            grain.size.Override(1.1f);
            grain.lumContrib.Override(0.85f);
            grain.colored.Override(false);

            ca = ScriptableObject.CreateInstance<ChromaticAberration>();
            ca.enabled.Override(true);
            ca.intensity.Override(0.06f);

            exposure = ScriptableObject.CreateInstance<AutoExposure>();
            exposure.enabled.Override(true);
            exposure.filtering.Override(new Vector2(55f, 92f));
            exposure.minLuminance.Override(-1.1f);      // dark rooms stay dark: the eye only adjusts so far
            exposure.maxLuminance.Override(1.8f);
            exposure.keyValue.Override(0.9f);
            exposure.eyeAdaptation.Override(EyeAdaptation.Progressive);
            exposure.speedUp.Override(1.6f);      // into the light: quick
            exposure.speedDown.Override(0.55f);   // into the dark: slow, so a dark room stays dark for a moment

            volume = PostProcessManager.instance.QuickVolume(VolumeLayer, 100f, ao, bloom, grade, vignette, grain, ca, exposure);
            volume.gameObject.name = "Look";
            DontDestroyOnLoad(volume.gameObject);
        }

        /// <summary>Snap the eye adaptation (after a teleport, a cut, a capture): no slow fade from the last place.</summary>
        public void Cut() { if (layer != null) layer.ResetHistory(); }

        /// <summary>A scare lands: 0..1.</summary>
        public static void Kick(float strength) { if (I != null) I.kick = Mathf.Max(I.kick, Mathf.Clamp01(strength * Settings.S.shake)); }

        /// <summary>How afraid the player is right now, 0..1 (from the fear engine and the body readings).</summary>
        public static void Fear(float f) { if (I != null) I.fear = Mathf.Clamp01(f); }

        public static void Mode(int m) { if (I != null) I.mode = m; }

        void Update()
        {
            if (volume == null) return;
            var g = GameDirector.I;
            if (g != null && g.player != null && g.house != null)
            {
                Vector3 p = g.player.transform.position;
                mode = g.phase == Phase.Dream ? 3 : g.house.InCellar(p) ? 4 : g.house.Outside(p) ? 2 : g.phase == Phase.Evening ? 1 : 0;
                fear = Mathf.Max(fear * 0.98f, 1f - g.grip);
            }
            float dt = Time.unscaledDeltaTime;
            kick = Mathf.MoveTowards(kick, 0f, dt * 0.7f);
            float k = kick * kick;
            float t = Time.unscaledTime;

            // The place sets the base grade; fear and scares push on top of it.
            float temp = 6f, sat = -12f, exp = 0.6f, vig = 0.36f, aoI = 1.1f, gr = 0.32f;
            switch (mode)
            {
                case 1: temp = 14f; sat = -18f; exp = 0.75f; vig = 0.42f; gr = 0.38f; break;                // lamplight
                case 2: temp = -10f; sat = -24f; exp = 0.35f; vig = 0.3f; aoI = 0.8f; gr = 0.3f; break;       // overcast woods
                case 3: temp = -18f; sat = -35f; exp = 0.5f; vig = 0.5f; aoI = 1.4f; gr = 0.5f; break;        // dream
                case 4: temp = 4f; sat = -40f; exp = 0.4f; vig = 0.55f; aoI = 1.5f; gr = 0.55f; break;        // cellar
            }
            grade.temperature.value = Mathf.Lerp(grade.temperature.value, temp, dt * 1.5f);
            grade.saturation.value = Mathf.Lerp(grade.saturation.value, sat - fear * 25f - k * 30f, dt * 2f);
            grade.postExposure.value = Mathf.Lerp(grade.postExposure.value, exp - k * 1.2f, dt * 6f);
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, vig + fear * 0.12f + k * 0.2f + Mathf.Sin(t * 2.4f) * fear * 0.03f, dt * 4f);
            ao.intensity.value = aoI;
            grain.intensity.value = gr + fear * 0.25f + k * 0.4f;
            ca.intensity.value = 0.06f + fear * 0.12f + k * 0.85f;
            bloom.intensity.value = 1.4f + k * 2f;
        }

        void OnDestroy()
        {
            if (volume != null) RuntimeUtilities.DestroyVolume(volume, true, true);
            if (I == this) I = null;
        }
    }
}
