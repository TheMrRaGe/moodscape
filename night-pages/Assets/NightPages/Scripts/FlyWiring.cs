using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Loads the real glomerulus-to-Kenyon-cell wiring of the fruit fly's mushroom body for the fear engine's
    /// instinct (Resources/NPFly/mushroom_body.json, extracted by tools/fly/extract_mb.py from the MaleCNS
    /// connectome v1.0, Janelia FlyEM + Google Research, CC-BY 4.0). Without the file it uses the FlyHash
    /// control, which learns just as well.
    /// </summary>
    public static class FlyWiring
    {
        [System.Serializable]
        class FlyFile
        {
            public string source;
            public string[] glom;
            public string[] kcType;
            public int[] g2kG, g2kK;
            public float[] g2kW;
        }

        public static FearCore.Wiring Load()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("NPFly/mushroom_body");
                if (asset != null)
                {
                    var f = JsonUtility.FromJson<FlyFile>(asset.text);
                    if (f != null && f.glom != null && f.kcType != null && f.g2kG != null && f.g2kG.Length == f.g2kK.Length && f.g2kG.Length == f.g2kW.Length)
                        return new FearCore.Wiring
                        {
                            gloms = f.glom.Length, kcs = f.kcType.Length, glom = f.g2kG, kc = f.g2kK, weight = f.g2kW,
                            source = "fruit fly connectome, " + f.glom.Length + " glomeruli, " + f.kcType.Length + " Kenyon cells"
                        };
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the fly wiring (" + e.Message + "); using the random control"); }
            return FearCore.Wiring.Random();
        }
    }
}
