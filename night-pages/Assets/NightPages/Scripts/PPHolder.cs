using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace NightPages
{
    /// <summary>
    /// Points at the post-processing package's shaders and textures, so a build includes them. The build script
    /// creates one in Resources/NPKeep; Look loads it to start the camera effects.
    /// </summary>
    public class PPHolder : ScriptableObject
    {
        public PostProcessResources resources;
    }
}
