using Godot;

namespace Distrito.Game.Audio;

/// <summary>
/// Efectos de sonido generados por código (2026-11-08): no hay archivos de audio, así que se sintetizan al arrancar
/// (ruido filtrado, senos y decaimientos) y quedan en memoria. Pasos, tiros, piñas, la urna del café, la taza, el
/// motor de los autos, el murmullo de la calle y bocinas. Sirven hasta que haya sonidos grabados de verdad.
/// </summary>
public static class Sfx
{
    public const int Rate = 22050;

    private static readonly Random Rng = new(1952);
    private static readonly Dictionary<string, AudioStreamWav> Cache = new();

    public static AudioStreamWav Step(int variant) => Get($"paso{variant % 4}", () => MakeStep(variant));
    public static AudioStreamWav Shot => Get("tiro", MakeShot);
    public static AudioStreamWav Punch => Get("pina", MakePunch);
    public static AudioStreamWav Pour => Get("urna", MakePour);
    public static AudioStreamWav Clink => Get("taza", MakeClink);
    public static AudioStreamWav Engine => Get("motor", MakeEngine);
    public static AudioStreamWav Street => Get("calle", MakeStreet);
    public static AudioStreamWav Horn => Get("bocina", MakeHorn);
    public static AudioStreamWav Click => Get("clic", MakeClick);
    public static AudioStreamWav Thunder => Get("trueno", MakeThunder);
    public static AudioStreamWav Spark => Get("chispa", MakeSpark);
    public static AudioStreamWav Accordion => Get("acordeon", MakeAccordion);
    public static AudioStreamWav Siren => Get("sirena", MakeSiren);
    public static AudioStreamWav ShipHorn => Get("sirena-barco", MakeShipHorn);
    public static AudioStreamWav Bell => Get("campana", MakeBell);
    public static AudioStreamWav Screech => Get("chirrido", MakeScreech);
    public static AudioStreamWav Rain => Get("lluvia", MakeRain);
    public static AudioStreamWav Whistle => Get("silbato", MakeWhistle);
    public static AudioStreamWav Steam => Get("vapor", MakeSteam);

    /// <summary>Un sonido de las escenas por su nombre (los de config/escenas.json y CityScenes).</summary>
    public static AudioStreamWav? ByName(string name) => name switch
    {
        "trueno" => Thunder, "chispa" => Spark, "acordeon" => Accordion, "sirena" => Siren, "sirena-barco" => ShipHorn,
        "campana" => Bell, "chirrido" => Screech, "tiro" => Shot, "bocina" => Horn, "motor" => Engine,
        "silbato" => Whistle, "vapor" => Steam, _ => null,
    };

    private static AudioStreamWav Get(string key, Func<AudioStreamWav> make)
    {
        if (!Cache.TryGetValue(key, out var s))
            Cache[key] = s = make();
        return s;
    }

    // ---------- Síntesis ----------

    private static float Noise() => (float)(Rng.NextDouble() * 2 - 1);

    /// <summary>Filtro pasabajos de un polo (a más alto <paramref name="a"/>, más agudo).</summary>
    private sealed class LowPass(float a)
    {
        private float _y;
        public float A = a;
        public float Next(float x) => _y += A * (x - _y);
    }

    private static AudioStreamWav ToWav(float[] samples, bool loop = false, float gain = 1f)
    {
        var peak = samples.Max(MathF.Abs);
        var scale = peak > 0 ? gain * 0.9f / peak : 0;
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Math.Clamp(samples[i] * scale * short.MaxValue, short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xff);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }
        return wav;
    }

    /// <summary>Hace que un sonido en bucle no tenga salto: mezcla el final con el principio.</summary>
    private static float[] Seamless(float[] s, int fade)
    {
        var n = s.Length - fade;
        var result = new float[n];
        Array.Copy(s, result, n);
        for (var i = 0; i < fade; i++)
        {
            var t = i / (float)fade;
            result[i] = s[i] * t + s[n + i] * (1 - t);
        }
        return result;
    }

    private static AudioStreamWav MakeStep(int variant)
    {
        var n = (int)(Rate * 0.11);
        var s = new float[n];
        var lp = new LowPass(0.18f + 0.04f * variant);
        var thump = 95 + 12 * variant;
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            s[i] = lp.Next(Noise()) * MathF.Exp(-t * 45) + 0.6f * MathF.Sin(MathF.Tau * thump * t) * MathF.Exp(-t * 60);
        }
        return ToWav(s, gain: 0.55f);
    }

    private static AudioStreamWav MakeShot()
    {
        var n = (int)(Rate * 0.9);
        var s = new float[n];
        var lp = new LowPass(0.9f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            lp.A = Math.Max(0.05f, 0.9f - t * 3); // el estampido se apaga hacia lo grave
            var crack = t < 0.004f ? Noise() * 1.6f : 0;
            s[i] = lp.Next(Noise()) * MathF.Exp(-t * 9) + crack + 0.5f * MathF.Sin(MathF.Tau * 60 * t) * MathF.Exp(-t * 14);
        }
        // Eco de la calle: dos rebotes en las paredes.
        foreach (var (delay, amount) in new[] { (0.07f, 0.35f), (0.16f, 0.2f) })
        {
            var d = (int)(delay * Rate);
            for (var i = n - 1; i >= d; i--)
                s[i] += s[i - d] * amount;
        }
        return ToWav(s);
    }

    private static AudioStreamWav MakePunch()
    {
        var n = (int)(Rate * 0.18);
        var s = new float[n];
        var lp = new LowPass(0.12f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            s[i] = lp.Next(Noise()) * MathF.Exp(-t * 30) + 0.8f * MathF.Sin(MathF.Tau * 85 * t) * MathF.Exp(-t * 35);
        }
        return ToWav(s, gain: 0.8f);
    }

    private static AudioStreamWav MakePour()
    {
        var n = (int)(Rate * 1.3);
        var s = new float[n];
        var lp = new LowPass(0.25f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var gurgle = 0.6f + 0.4f * MathF.Sin(MathF.Tau * 6.5f * t + 2 * MathF.Sin(MathF.Tau * 1.7f * t));
            s[i] = lp.Next(Noise()) * gurgle;
        }
        return ToWav(Seamless(s, Rate / 5), loop: true, gain: 0.5f);
    }

    private static AudioStreamWav MakeClink()
    {
        var n = (int)(Rate * 0.45);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            s[i] = (MathF.Sin(MathF.Tau * 2150 * t) + 0.6f * MathF.Sin(MathF.Tau * 3300 * t) + 0.3f * MathF.Sin(MathF.Tau * 4750 * t)) * MathF.Exp(-t * 14);
        }
        return ToWav(s, gain: 0.45f);
    }

    /// <summary>Clic de la interfaz: un golpecito seco, como la tecla de una máquina de escribir.</summary>
    private static AudioStreamWav MakeClick()
    {
        var n = (int)(Rate * 0.06);
        var s = new float[n];
        var rng = new Random(7);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var noise = (float)(rng.NextDouble() * 2 - 1) * MathF.Exp(-t * 180);
            var body = MathF.Sin(MathF.Tau * 1250 * t) * MathF.Exp(-t * 90) + 0.5f * MathF.Sin(MathF.Tau * 2900 * t) * MathF.Exp(-t * 140);
            s[i] = 0.55f * noise + 0.6f * body;
        }
        return ToWav(s, gain: 0.35f);
    }

    /// <summary>Trueno: un estallido y un retumbo grave que se va.</summary>
    private static AudioStreamWav MakeThunder()
    {
        var n = Rate * 4;
        var s = new float[n];
        var low = new LowPass(0.02f);
        var lower = new LowPass(0.006f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var crack = t < 0.25f ? Noise() * MathF.Exp(-t * 12) : 0;
            var rumble = lower.Next(low.Next(Noise())) * 14 * MathF.Exp(-t * 0.9f) * (1 + 0.5f * MathF.Sin(t * 7));
            s[i] = crack * 0.6f + rumble;
        }
        return ToWav(s, gain: 0.9f);
    }

    /// <summary>Chispazo de un transformador (el apagón).</summary>
    private static AudioStreamWav MakeSpark()
    {
        var n = (int)(Rate * 0.7);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var burst = Rng.NextDouble() < 0.15 ? Noise() : 0;
            s[i] = (burst + 0.3f * MathF.Sin(MathF.Tau * 120 * t) * MathF.Exp(-t * 6)) * MathF.Exp(-t * 4);
        }
        return ToWav(s, gain: 0.6f);
    }

    /// <summary>Acordeón: una melodía corta de vals (16 s) con dos lengüetas apenas desafinadas y vibrato.</summary>
    private static AudioStreamWav MakeAccordion()
    {
        int[] melody = [0, 4, 7, 12, 11, 7, 9, 5, 4, 0, 2, 4, 5, 7, 4, 0, 7, 9, 11, 12, 11, 9, 7, 4];
        int[] bass = [0, 0, 5, 5, 7, 7, 0, 0];
        const float beat = 0.66f;
        var n = (int)(Rate * melody.Length * beat);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var k = (int)(t / beat);
            var local = t - k * beat;
            var env = MathF.Min(1, local * 30) * (0.75f + 0.25f * MathF.Exp(-local * 3)) * MathF.Min(1, (beat - local) * 25);
            var f = 349.23f * MathF.Pow(2, melody[k % melody.Length] / 12f) * (1 + 0.004f * MathF.Sin(MathF.Tau * 5.5f * t));
            float Reed(float freq) => 2 * (freq * t % 1) - 1;
            var tone = (Reed(f) + Reed(f * 1.004f)) * 0.5f;
            var fb = 87.31f * MathF.Pow(2, bass[(k / 3) % bass.Length] / 12f);
            var chord = (k % 3 == 0 ? Reed(fb) : (Reed(fb * 1.5f) + Reed(fb * 2.52f)) * 0.5f) * (local < 0.3f ? 1 : 0.2f);
            s[i] = (tone * env * 0.55f + chord * 0.35f) * 0.8f;
        }
        var lp = new LowPass(0.25f);
        for (var i = 0; i < n; i++)
            s[i] = lp.Next(s[i]);
        return ToWav(s, gain: 0.45f);
    }

    /// <summary>Sirena de patrullero de los 50: el aullido que sube y baja.</summary>
    private static AudioStreamWav MakeSiren()
    {
        var n = Rate * 6;
        var s = new float[n];
        var phase = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var f = 650 + 550 * (0.5f - 0.5f * MathF.Cos(MathF.Tau * t / 3f));
            phase += MathF.Tau * f / Rate;
            s[i] = (MathF.Sin(phase) + 0.3f * MathF.Sin(2 * phase)) * MathF.Min(1, t * 4) * MathF.Min(1, (6 - t) * 2);
        }
        return ToWav(s, gain: 0.5f);
    }

    /// <summary>Bocina de barco: grave y larga.</summary>
    /// <summary>Silbato de locomotora: acorde de tres notas con aire (vapor) y un temblor, dos toques.</summary>
    private static AudioStreamWav MakeWhistle()
    {
        var n = (int)(Rate * 2.6);
        var s = new float[n];
        var air = new LowPass(0.35f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            // Dos toques: uno corto y uno largo.
            var env = t < 0.55f ? MathF.Min(1, t * 12) * MathF.Min(1, (0.55f - t) * 12) : t > 0.75f ? MathF.Min(1, (t - 0.75f) * 8) * MathF.Min(1, (2.6f - t) * 3) : 0;
            var wobble = 1 + 0.006f * MathF.Sin(MathF.Tau * 5.5f * t);
            var tone = MathF.Sin(MathF.Tau * 523 * wobble * t) + 0.8f * MathF.Sin(MathF.Tau * 659 * wobble * t) + 0.7f * MathF.Sin(MathF.Tau * 784 * wobble * t);
            s[i] = (tone * 0.33f + air.Next(Noise()) * 0.35f) * env;
        }
        return ToWav(s, gain: 0.7f);
    }

    /// <summary>Vapor que escapa: ruido que arranca fuerte y se apaga despacio.</summary>
    private static AudioStreamWav MakeSteam()
    {
        var n = (int)(Rate * 2.2);
        var s = new float[n];
        var hiss = new LowPass(0.6f);
        var low = new LowPass(0.05f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var env = MathF.Min(1, t * 20) * MathF.Exp(-t * 1.4f);
            var x = Noise();
            s[i] = (hiss.Next(x) - low.Next(x)) * env;
        }
        return ToWav(s, gain: 0.8f);
    }

    private static AudioStreamWav MakeShipHorn()
    {
        var n = (int)(Rate * 3.5);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var env = MathF.Min(1, t * 3) * MathF.Min(1, (3.5f - t) * 2);
            s[i] = (MathF.Sin(MathF.Tau * 92 * t) + 0.6f * MathF.Sin(MathF.Tau * 184 * t) + 0.25f * MathF.Sin(MathF.Tau * 276 * t)) * env;
        }
        return ToWav(s, gain: 0.85f);
    }

    /// <summary>Campana de iglesia (el cortejo).</summary>
    private static AudioStreamWav MakeBell()
    {
        var n = Rate * 5;
        var s = new float[n];
        float[] partials = [1f, 2.0f, 2.4f, 3.0f, 4.2f];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var v = 0f;
            for (var p = 0; p < partials.Length; p++)
                v += MathF.Sin(MathF.Tau * 220 * partials[p] * t) * MathF.Exp(-t * (0.6f + p * 0.5f)) / (p + 1);
            s[i] = v;
        }
        return ToWav(s, gain: 0.6f);
    }

    /// <summary>Chirrido de gomas (arranca la persecución).</summary>
    private static AudioStreamWav MakeScreech()
    {
        var n = (int)(Rate * 1.3);
        var s = new float[n];
        var band = new LowPass(0.35f);
        var hp = new LowPass(0.08f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var x = band.Next(Noise());
            var y = x - hp.Next(x);
            s[i] = (y * 3 + 0.4f * MathF.Sin(MathF.Tau * (1800 + 200 * MathF.Sin(t * 30)) * t)) * MathF.Min(1, t * 10) * MathF.Exp(-t * 1.2f);
        }
        return ToWav(s, gain: 0.5f);
    }

    /// <summary>Lluvia en bucle (ruido filtrado con gotas).</summary>
    private static AudioStreamWav MakeRain()
    {
        var n = Rate * 4;
        var s = new float[n + Rate / 2];
        var lp = new LowPass(0.3f);
        for (var i = 0; i < s.Length; i++)
        {
            var drop = Rng.NextDouble() < 0.004 ? Noise() * 3 : 0;
            s[i] = lp.Next(Noise()) * 0.6f + drop * 0.3f;
        }
        return ToWav(Seamless(s, Rate / 2), loop: true, gain: 0.5f);
    }

    private static AudioStreamWav MakeEngine()
    {
        var n = (int)(Rate * 0.8);
        var s = new float[n];
        var lp = new LowPass(0.08f);
        const float f = 50f; // ralentí: se le cambia el tono con la velocidad
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var phase = f * t % 1f;
            var saw = 2 * phase - 1;
            var rumble = lp.Next(Noise());
            var cyl = 0.5f + 0.5f * MathF.Sin(MathF.Tau * f * 2 * t); // los cilindros
            s[i] = (0.6f * saw + 0.35f * MathF.Sin(MathF.Tau * f * 2 * t)) * cyl + 1.2f * rumble;
        }
        return ToWav(Seamless(s, Rate / 10), loop: true, gain: 0.6f);
    }

    private static AudioStreamWav MakeStreet()
    {
        var n = Rate * 6;
        var s = new float[n];
        var low = new LowPass(0.015f);
        var mid = new LowPass(0.08f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            // Tránsito a lo lejos (grave) y gente hablando (un murmullo que sube y baja).
            var talk = mid.Next(Noise()) * (0.5f + 0.5f * MathF.Sin(MathF.Tau * 0.23f * t) * MathF.Sin(MathF.Tau * 0.61f * t));
            s[i] = 2.2f * low.Next(Noise()) + 0.5f * talk;
        }
        return ToWav(Seamless(s, Rate), loop: true, gain: 0.5f);
    }

    private static AudioStreamWav MakeHorn()
    {
        var n = (int)(Rate * 0.55);
        var s = new float[n];
        var lp = new LowPass(0.2f);
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)Rate;
            var env = Math.Min(1, t * 40) * (t > 0.45f ? MathF.Exp(-(t - 0.45f) * 40) : 1);
            var sq = MathF.Sign(MathF.Sin(MathF.Tau * 330 * t)) + 0.8f * MathF.Sign(MathF.Sin(MathF.Tau * 415 * t)); // dos notas, como las bocinas de los 50
            s[i] = lp.Next(sq) * env;
        }
        return ToWav(s, gain: 0.5f);
    }
}
