using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// A part of the monster's skeleton with a truer shape than Unity's primitives give (see Flesh): a limb segment
    /// that tapers (a forearm, thick by the elbow and narrow at the wrist; a spider's tibia), a muscle that is a real
    /// ellipsoid (a biceps, a calf), a flattened rounded box (a palm, a mantis's blade). Lengths and radii are in
    /// metres in the monster's own space. `blend` scales how softly it melts into its neighbours (well under 1 for a
    /// hard exoskeleton seam, over 1 for soft flesh). A `carve` part is cut away instead of added: a crease, a
    /// groove between plates, the hollow behind a knee.
    /// The GameObject still carries a plain primitive (a capsule, a sphere, a cube) sized to match, so it can be
    /// seen as it is when the flesh isn't melted.
    /// </summary>
    public class FleshShape : MonoBehaviour
    {
        public enum K { RoundCone, Ellipsoid, RoundBox }
        public K kind;
        public float r0, r1, halfLen;    // round cone: radius at -y and at +y, half the length between the centres
        public float round;              // round box: corner radius
        public float blend = 1f;
        public bool carve;
    }
}
