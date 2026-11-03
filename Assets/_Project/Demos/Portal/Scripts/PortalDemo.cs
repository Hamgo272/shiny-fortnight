using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Portal
{
    // Open a doorway in your room and walk through it into another world. Look at the floor and
    // pinch: a portal stands there, facing you, anchored to the floor, with the world visible only
    // through it (see PortalWorld). Walk through the frame and you're in the world, with your room
    // showing through the door behind you; walk back through to return. Runs in passthrough, which
    // shows the room: so everything, the world included, is seen flat, the same picture in both eyes.
    // Walking through needs the phone off its cable. Move portal takes it down to put it somewhere
    // else. The panel chooses the world beyond the door, the Moon (the default) or the night world,
    // which takes a standing portal down to open the other; and, for the Moon, whether your hand
    // shows as the suit glove or your real hand, and Smooth soil undoes what you've pressed into it.
    // Logs [Portal].
    public class PortalDemo : Demo
    {
        [SerializeField] GameObject m_PortalPrefab;

        CoreServices m_Core;
        PortalWorld m_Portal;
        RectTransform m_Panel;
        TMP_Text m_Hint;
        AudioSource m_Audio;
        AudioClip m_Whoosh;
        PortalWorld.WorldKind m_Kind = PortalWorld.WorldKind.Moon;
        MoonHand.Look m_Look = MoonHand.Look.Glove;
        M3Button m_MoonButton;
        M3Button m_NightButton;
        M3Button m_GloveButton;
        M3Button m_RealButton;
        M3Button m_SmoothButton;
        FloatingWindows.Window m_PanelEntry;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            core.Placer.ObjectPrefab = m_PortalPrefab;
            core.Placer.Placed += OnPlaced;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Whoosh = Whoosh();
            BuildPanel();
            UpdateHint();
            Debug.Log("[Portal] Ready: look at the floor and pinch to open a portal");
        }

        public override void OnDemoExit(CoreServices core)
        {
            core.Placer.Placed -= OnPlaced;
            FloatingWindows.Unregister(m_PanelEntry);
            if (m_Portal != null)
                m_Portal.Crossed -= OnCrossed;
        }

        void OnPlaced(GameObject placed)
        {
            if (!placed.TryGetComponent<PortalWorld>(out var portal))
                return;
            // Standing on the floor: its origin is the bottom of the doorway.
            placed.transform.localPosition = Vector3.zero;
            // Before its first frame, when it builds its world.
            portal.Kind = m_Kind;
            m_Portal = portal;
            m_Portal.Crossed += OnCrossed;
            m_Core.Placer.ObjectPrefab = null;
            var distance = Vector3.Distance(m_Core.Head.position, placed.transform.position);
            Debug.Log($"[Portal] Opened {distance:F1} m away");
            // Softly, as it rises out of the floor.
            m_Audio.PlayOneShot(m_Whoosh, 0.4f);
            UpdateHint();
            UpdateButtons();
        }

        void OnCrossed(bool inside)
        {
            m_Audio.PlayOneShot(m_Whoosh, 0.7f);
            UpdateHint();
        }

        // Chooses the world beyond the door. A standing portal goes down, to be opened again.
        void Choose(PortalWorld.WorldKind kind)
        {
            if (kind == m_Kind)
                return;
            m_Kind = kind;
            Debug.Log($"[Portal] Chose the {(kind == PortalWorld.WorldKind.Moon ? "Moon" : "night world")}");
            if (m_Portal != null)
                Move();
            UpdateButtons();
            UpdateHint();
        }

        void ChooseLook(MoonHand.Look look)
        {
            m_Look = look;
            UpdateButtons();
        }

        void SmoothSoil()
        {
            if (m_Portal != null && m_Portal.Moon != null)
                m_Portal.Moon.SmoothSoil();
        }

        void UpdateButtons()
        {
            var moon = m_Kind == PortalWorld.WorldKind.Moon;
            m_MoonButton.Selected = moon;
            m_NightButton.Selected = !moon;
            m_GloveButton.Selected = m_Look == MoonHand.Look.Glove;
            m_RealButton.Selected = m_Look == MoonHand.Look.Real;
            m_GloveButton.Interactable = moon;
            m_RealButton.Interactable = moon;
            m_SmoothButton.Interactable = moon && m_Portal != null;
        }

        void Update()
        {
            // The Moon world appears on the portal's first frame, after it was placed.
            if (m_Portal != null && m_Portal.Moon != null && m_Portal.Moon.HandLook != m_Look)
                m_Portal.Moon.HandLook = m_Look;
        }

        // Takes the portal down so it can be put somewhere else: it sinks into the floor first.
        void Move()
        {
            if (m_Portal == null)
                return;
            var portal = m_Portal;
            portal.Crossed -= OnCrossed;
            m_Portal = null;
            m_Audio.PlayOneShot(m_Whoosh, 0.3f);
            Debug.Log("[Portal] Closing it to move it");
            portal.Close(() =>
            {
                if (m_Core == null)
                    return;
                m_Core.Placer.RemoveAll();
                m_Core.Placer.ObjectPrefab = m_PortalPrefab;
                Debug.Log("[Portal] Taken down to move it");
                UpdateHint();
                UpdateButtons();
            });
            UpdateHint();
            UpdateButtons();
        }

        void UpdateHint()
        {
            var moon = m_Kind == PortalWorld.WorldKind.Moon;
            if (m_Portal == null)
                m_Hint.text = $"Look at the floor and pinch to open a portal {(moon ? "to the Moon" : "there")}.\nNo highlight on the floor? Look down at it for a moment.";
            else if (!m_Portal.IsInside)
                m_Hint.text = "Walk through the doorway.\nUnplug the phone first if it's on a cable.";
            else if (m_Portal.Moon != null)
                m_Hint.text = "You're on the Moon. Crouch and touch the soil by the door.\nYour room is behind you: walk back through to return.";
            else
                m_Hint.text = "You're through. Look back: your room is behind the door.\nWalk back through to return.";
        }

        // A Material panel ahead and a little above, clear of the floor where the portal goes: what to
        // do next, the world (Moon or Night), the hand on the Moon, Move portal and Smooth soil. The
        // lobby is on the settings panel.
        void BuildPanel()
        {
            const float distance = 1.5f;
            m_Panel = WorldUi.Panel("Portal Panel", transform, new Vector2(520f, 300f), distance);
            m_Hint = M3.Text("Hint", m_Panel, "", M3Theme.Type.BodyLarge, new Vector2(0f, 96f), new Vector2(472f, 72f));
            m_MoonButton = M3.Button("Moon", m_Panel, "Moon", M3Button.Variant.Tonal, new Vector2(-64f, 30f),
                () => Choose(PortalWorld.WorldKind.Moon), width: 120f);
            m_NightButton = M3.Button("Night", m_Panel, "Night", M3Button.Variant.Tonal, new Vector2(64f, 30f),
                () => Choose(PortalWorld.WorldKind.Night), width: 120f);
            m_GloveButton = M3.Button("Glove", m_Panel, "Suit glove", M3Button.Variant.Tonal, new Vector2(-84f, -30f),
                () => ChooseLook(MoonHand.Look.Glove), width: 160f);
            m_RealButton = M3.Button("Real Hand", m_Panel, "Real hand", M3Button.Variant.Tonal, new Vector2(84f, -30f),
                () => ChooseLook(MoonHand.Look.Real), width: 160f);
            M3.Button("Move portal", m_Panel, "Move portal", M3Button.Variant.Tonal, new Vector2(-92f, -96f), Move, "open_with", 176f);
            m_SmoothButton = M3.Button("Smooth soil", m_Panel, "Smooth soil", M3Button.Variant.Text, new Vector2(92f, -96f),
                SmoothSoil, width: 176f);
            UpdateButtons();
            WorldUi.PlaceInFront(m_Panel, m_Core.Head, distance, 8f, -25f);
            m_PanelEntry = FloatingWindows.Register("Portal Panel", m_Panel, M3Theme.DpToMeters(520f, distance), true, distance);
        }

        // A soft rush of noise that swells and fades, made in code.
        static AudioClip Whoosh()
        {
            const int rate = 44100;
            const float seconds = 0.7f;
            var samples = new float[(int)(rate * seconds)];
            var random = new System.Random(3);
            var low = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)samples.Length;
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                // A low-pass whose cutoff sweeps up then down, for the sense of something passing.
                var cutoff = Mathf.Lerp(0.02f, 0.25f, Mathf.Sin(t * Mathf.PI));
                low += (noise - low) * cutoff;
                samples[i] = low * Mathf.Sin(t * Mathf.PI) * 1.6f;
            }
            var clip = AudioClip.Create("Whoosh", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
