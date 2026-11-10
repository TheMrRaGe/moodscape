using System; using System.Collections.Generic; using NightPages;
static class P {
  // Two simulated players. The first is unsettled by three specific options. The second is afraid of a whole
  // kind of fear (animals), and only some of those options are ever shown to it in the first nights: the engine
  // should work out the kind, and rank animal options it has barely tried above the rest.
  static int Run(string name, Func<FearOption, bool> scares, int seed, bool checkKind) {
    System.IO.Directory.CreateDirectory("fear-sim-data");
    if (System.IO.File.Exists("fear-sim-data/nightpages-fear.json")) System.IO.File.Delete("fear-sim-data/nightpages-fear.json");
    var rng = new Random(seed); float t = 0f; int eases = 0; string learnedKind = null;
    Console.WriteLine("== " + name);
    for (int day = 1; day <= 7; day++) {
      var e = new FearEngine();
      int kept = 0, undone = 0;
      e.log = s => { if (s.StartsWith("KEPT")) kept++; if (s.StartsWith("undid")) undone++; if (s.StartsWith("EASING")) eases++; if (s.StartsWith("LEARNING")) learnedKind = s; };
      e.BeginDream(day, t);
      float level = 0f, end = t + 46f + 6f * day + 30f, onScreen = 0; int frames = 0;
      while (t < end) {
        t += 1f / 30f;
        int present = 0;
        for (int s = 0; s < FearEngine.Slots.Length; s++) if (scares(FearEngine.Slots[s].options[e.current[s]])) present++;
        float target = 0.4f * present - 0.05f + (float)(rng.NextDouble() - 0.5) * 0.5f;
        level += (target - level) * 0.12f;
        e.Tick(t, Math.Max(-1f, Math.Min(1f, level)), true, true);
        onScreen += present; frames++;
      }
      e.EndDream();
      Console.WriteLine($"  night {day}: kept {kept}, undid {undone}, frightening things on screen (avg) {onScreen / frames:0.00}");
    }
    var f = new FearEngine();
    float fs = 0, os = 0; int fn = 0, on = 0;
    for (int s = 1; s < FearEngine.Slots.Length; s++) for (int i = 1; i < FearEngine.Slots[s].options.Length; i++) {
      var o = FearEngine.Slots[s].options[i]; float m = f.Mean(s, i);
      if (scares(o)) { fs += m; fn++; } else { os += m; on++; }
    }
    Console.WriteLine($"  learned score: frightening options {fs / fn:+0.00;-0.00} vs the rest {os / on:+0.00;-0.00}");
    Console.WriteLine($"  times it eased off because the player stayed very frightened: {eases}");
    var ranking = f.Families();
    Console.Write("  kinds of fear, best first:");
    for (int i = 0; i < 4; i++) Console.Write($" {ranking[i].Key.name} {ranking[i].Value.mean:+0.00;-0.00;+0.00} |");
    Console.WriteLine();
    bool pass = fs / fn > os / on + 0.1f;
    if (checkKind) pass &= f.TopFamily() == FearTypes.Animal;
    Console.WriteLine("  -> " + (pass ? "PASS" : "FAIL"));
    return pass ? 0 : 1;
  }
  static int Main() {
    string[] three = { "It only stands", "Deep red", "Breathing" };
    int fails = 0;
    fails += Run("player afraid of three specific things", o => Array.IndexOf(three, o.label) >= 0, 3, false);
    fails += Run("player afraid of animals", o => o.families != null && Array.IndexOf(o.families, FearTypes.Animal) >= 0, 5, true);
    Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
    return fails;
  }
}
