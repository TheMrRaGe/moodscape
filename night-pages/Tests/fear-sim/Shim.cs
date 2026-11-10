using System;
namespace UnityEngine {
  public static class Mathf {
    public static float Sqrt(float x) => (float)Math.Sqrt(x);
    public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
  }
  public static class Application { public static string persistentDataPath = "fear-sim-data"; }
  public static class Debug { public static void LogWarning(string s) { Console.WriteLine("WARN " + s); } }
  public static class JsonUtility {
    public static string ToJson(object o, bool pretty) => System.Text.Json.JsonSerializer.Serialize(o, o.GetType(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    public static void FromJsonOverwrite(string s, object o) {
      var n = (NightPages.FearSave)System.Text.Json.JsonSerializer.Deserialize(s, typeof(NightPages.FearSave), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
      var t = (NightPages.FearSave)o; t.dreams = n.dreams; t.stats = n.stats;
    }
  }
}
