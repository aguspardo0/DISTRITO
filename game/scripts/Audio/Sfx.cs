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
