using UnityEngine;

namespace CardboardXR.Interaction
{
    // Cardboard's gaze reticle: a dot straight ahead that grows into a ring over something clickable.
    // It sits at the distance of whatever the gaze hits, so both eyes converge on the target, and its
    // size is set in degrees, so it looks the same at any distance. The sizes and growth speed follow
    // the Google VR SDK's GvrReticlePointer. Put it on a unit quad using the CardboardXR/Reticle
    // shader, as a child of the GazePointer.
    //   Feedback the moment a press registers, before whatever it presses answers (an app on a window
    //   can take a few hundred milliseconds): while held, the reticle draws in to k_PressScale and
    //   turns solid; on release it springs back with a quick ring out past it (k_PingSeconds).
    [RequireComponent(typeof(MeshRenderer))]
    public class GazeReticle : MonoBehaviour
    {
        static readonly int k_Inner = Shader.PropertyToID("_Inner");
        static readonly int k_Outer = Shader.PropertyToID("_Outer");
        static readonly int k_Outline = Shader.PropertyToID("_Outline");
        static readonly int k_Color = Shader.PropertyToID("_Color");

        [Tooltip("Diameter of the dot, in degrees.")]
        [SerializeField] float m_DotDegrees = 0.5f;
        [Tooltip("How much the inner and outer diameters grow over something clickable, in degrees.")]
        [SerializeField] float m_GrowthDegrees = 1.5f;
        [SerializeField] float m_GrowthSpeed = 8f;
        [Tooltip("Width of the dark edge that keeps the reticle visible on light surfaces, in degrees.")]
        [SerializeField] float m_OutlineDegrees = 0.1f;
        [Tooltip("Closer than this, the eyes can't comfortably converge on the reticle.")]
        [SerializeField] float m_MinDistance = 0.3f;
        [SerializeField] Color m_Color = new(1f, 1f, 1f, 0.9f);
        [Tooltip("While the camera sees a hand, so a pinch will click.")]
        [SerializeField] Color m_HandColor = new(0.45f, 0.85f, 1f, 0.95f);

        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;
        float m_Distance = 10f;
        bool m_Interactive;
        // 0 is the dot, 1 the full ring.
        float m_Growth;
        const float k_PressScale = 0.6f;
        const float k_PingSeconds = 0.22f;
        const float k_PingDegrees = 1.6f;
        bool m_Held;
        float m_Press;
        float m_PingAt = float.NegativeInfinity;

        // From GazePointer: a press began (true) or ended (false).
        public void Press(bool held)
        {
            if (m_Held && !held)
                m_PingAt = Time.unscaledTime;
            m_Held = held;
        }

        // Whether a hand is in the camera's view, ready to pinch: the reticle turns light blue, since
        // otherwise nothing says whether a pinch will register.
        public bool HandSeen { get; set; }

        public void SetTarget(float distance, bool interactive)
        {
            m_Distance = Mathf.Max(distance, m_MinDistance);
            m_Interactive = interactive;
        }

        void Awake()
        {
            m_Renderer = GetComponent<MeshRenderer>();
            m_Block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            m_Growth = Mathf.Lerp(m_Growth, m_Interactive ? 1f : 0f, Mathf.Clamp01(Time.unscaledDeltaTime * m_GrowthSpeed));

            // Pressing pulls it in fast; letting go springs it back.
            m_Press = Mathf.MoveTowards(m_Press, m_Held ? 1f : 0f, Time.unscaledDeltaTime * (m_Held ? 25f : 10f));
            var ping = Mathf.Clamp01((Time.unscaledTime - m_PingAt) / k_PingSeconds);
            var pinging = ping < 1f;
            var scale = Mathf.Lerp(1f, k_PressScale, m_Press);
            // Radii per meter of distance. The ring fills in as it's pressed.
            var inner = RadiusForDiameter(m_GrowthDegrees * m_Growth * scale * (1f - m_Press));
            var outer = RadiusForDiameter((m_DotDegrees + m_GrowthDegrees * m_Growth) * scale +
                                          (pinging ? k_PingDegrees * Mathf.Sin(ping * Mathf.PI) : 0f));
            var outline = Mathf.Tan(m_OutlineDegrees * Mathf.Deg2Rad);
            var quadRadius = outer + outline;

            transform.localPosition = new Vector3(0f, 0f, m_Distance);
            var size = 2f * quadRadius * m_Distance;
            transform.localScale = new Vector3(size, size, 1f);
            m_Block.SetFloat(k_Inner, inner / quadRadius);
            m_Block.SetFloat(k_Outer, outer / quadRadius);
            m_Block.SetFloat(k_Outline, outline / quadRadius);
            var color = HandSeen ? m_HandColor : m_Color;
            color.a = Mathf.Lerp(color.a, 1f, m_Press);
            m_Block.SetColor(k_Color, color);
            m_Renderer.SetPropertyBlock(m_Block);
        }

        static float RadiusForDiameter(float degrees) => Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad);
    }
}
