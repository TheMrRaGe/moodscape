using UnityEngine;

namespace NightPages
{
    /// <summary>Anything the player can use with E.</summary>
    public class Interactable : MonoBehaviour
    {
        public string prompt = "";
        public System.Func<string> promptFn;
        public System.Action use;
        public System.Func<bool> can;

        public bool Available { get { return can == null || can(); } }
        public string Prompt { get { return promptFn != null ? promptFn() : prompt; } }
    }
}
