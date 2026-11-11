using UnityEngine;
using UnityEngine.SceneManagement;

namespace NightPages
{
    /// <summary>
    /// Starts the game when you press Play, whatever scene is open. It makes a fresh empty scene and
    /// builds the whole prototype into it from code, so there are no scene or prefab files to set up.
    /// To switch this off (once you start authoring real scenes), put an object named
    /// "NoNightPagesBootstrap" in your scene.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (GameObject.Find("NoNightPagesBootstrap") != null) return;
            Sfx.ResetStatics();
            Build.ResetStatics();
            Monster.ResetStatics();
            WallHands.ResetStatics();
            Painted.ResetStatics();
            Habits.ResetStatics();
            Noise.ResetStatics();
            Door.ResetStatics();
            DreamMaze.ResetStatics();
            Look.ResetStatics();
            FaceReader.ResetStatics();
            Mirror.ResetStatics();
            MonsterSkin.ResetStatics();
            Outdoors.ResetStatics();
            Tour.ResetStatics();
            Reactions.ResetStatics();
            VRRig.ResetStatics();
            Work.ResetStatics(); Source.ResetStatics(); Spot.ResetStatics(); ClockFace.ResetStatics();
            House.Switched = null;
            var old = SceneManager.GetActiveScene();
            var fresh = SceneManager.CreateScene("NightPagesRuntime");
            SceneManager.SetActiveScene(fresh);
            if (old.IsValid() && old.isLoaded) SceneManager.UnloadSceneAsync(old);
            RenderSettings.skybox = null;
            new GameObject("NightPages").AddComponent<GameDirector>();
            Debug.Log("[NightPages] Prototype started. F1 shows what the game is thinking.");
        }
    }
}
