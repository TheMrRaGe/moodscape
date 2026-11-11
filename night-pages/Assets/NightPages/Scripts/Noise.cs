using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Noise the player makes that something might hear: a door slammed or banged against the wall, a door
    /// shoved open at a run, something knocked over. Loudness 0..1 (a latch clicking is about 0.1, a slam 1).
    ///
    /// Whatever is listening subscribes: the thing in the dream maze turns toward a loud noise (and comes
    /// sooner if it's resting), the house answers a slam with knocks of its own, and the habits keep count
    /// of how careless you are with doors.
    /// </summary>
    public static class Noise
    {
        /// <summary>(where, how loud 0..1, what made it).</summary>
        public static event System.Action<Vector3, float, string> Made;

        public static float LastLoud { get; private set; }
        public static Vector3 LastAt { get; private set; }
        public static float LastAt_Time { get; private set; } = -100f;

        public static void ResetStatics() { Made = null; LastLoud = 0f; LastAt_Time = -100f; }

        public static void Make(Vector3 at, float loud, string what)
        {
            LastLoud = loud;
            LastAt = at;
            LastAt_Time = Time.time;
            var g = GameDirector.I;
            if (g != null && loud >= 0.35f) g.Log("noise: " + what + " (loudness " + loud.ToString("0.00") + ")");
            if (Made != null) Made(at, loud, what);
        }

        /// <summary>How far away something with good ears would hear a noise this loud.</summary>
        public static float Reach(float loud) { return Mathf.Lerp(3f, 30f, Mathf.Clamp01(loud)); }
    }
}
