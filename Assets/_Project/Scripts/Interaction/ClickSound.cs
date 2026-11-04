using CardboardXR.Stereo;
using UnityEngine;

namespace CardboardXR.Interaction
{
    // A soft tick the moment a gaze press registers (GazePointer), so a pinch is heard as well as
    // seen, even before what it pressed answers. Made in code: 25 ms of a fast-decaying 2.2 kHz tone
    // with a little noise for the edge, played quietly, unspatialized. The "clicks" launch option
    // at 0 turns it off. Made on first use and kept across scenes.
    public static class ClickSound
    {
        const int k_Rate = 44100;
        const float k_Seconds = 0.025f;
        const float k_Frequency = 2200f;
        const float k_Volume = 0.22f;

        static AudioSource s_Source;
        static bool s_Off;
        static bool s_Checked;

        public static void Play()
        {
            // Edit-mode tests press the gaze pointer too, where there's nothing to hear and
            // DontDestroyOnLoad isn't allowed.
            if (!Application.isPlaying)
                return;
            if (!s_Checked)
            {
                s_Checked = true;
                s_Off = LaunchOptions.GetInt("clicks", 1) == 0;
            }
            if (s_Off)
                return;
            if (s_Source == null)
                s_Source = Create();
            s_Source.PlayOneShot(s_Source.clip, k_Volume);
        }

        static AudioSource Create()
        {
            var holder = new GameObject("Click Sound");
            Object.DontDestroyOnLoad(holder);
            var source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.clip = Tick();
            return source;
        }

        // The tick's samples: a sine under an exponential decay, with a pinch of seeded noise.
        public static float[] Samples(int rate = k_Rate)
        {
            var count = Mathf.CeilToInt(rate * k_Seconds);
            var samples = new float[count];
            var random = new System.Random(7);
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / rate;
                var envelope = Mathf.Exp(-t * 260f);
                var tone = Mathf.Sin(2f * Mathf.PI * k_Frequency * t);
                var noise = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 900f);
                samples[i] = Mathf.Clamp(envelope * (0.8f * tone + 0.35f * noise), -1f, 1f);
            }
            return samples;
        }

        static AudioClip Tick()
        {
            var samples = Samples();
            var clip = AudioClip.Create("Tick", samples.Length, 1, k_Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
