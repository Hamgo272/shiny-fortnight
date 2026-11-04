using System.Collections.Generic;
using CardboardXR.UI;
using UnityEngine;

namespace CardboardXR.Demos
{
    // The settings panel's open apps: a short list under the panel of what's running, each with its
    // icon, its name and an X that closes it, as Android XR lists its open windows. One demo runs at a
    // time, so today that's the demo you're in, or "No apps open" in the lobby; closing it goes back to
    // the lobby, as its own Leave does. The list rebuilds whenever a demo loads, so it grows as more
    // can run at once. Its Open button on the toolbar shows and hides it. Part of the settings panel,
    // so it follows the panel and closes with it. Lives on the always-active Demos object. Logs [Demos].
    public class OpenApps : MonoBehaviour
    {
        // In dp: a row, the gap between rows, and the padding round the list.
        const float k_RowHeight = 64f;
        const float k_Gap = 4f;
        const float k_Padding = 12f;
        const float k_Width = 360f;
        // Below the panel's own X, which sits under the panel.
        const float k_Below = 20f + M3Theme.k_Target + 16f;

        [SerializeField] DemoManager m_Demos;
        [Tooltip("The settings panel, which the list hangs under.")]
        [SerializeField] RectTransform m_Panel;
        [Tooltip("The toolbar's Open button, shown selected while the list shows.")]
        [SerializeField] M3Button m_Button;

        RectTransform m_Root;
        readonly List<GameObject> m_Rows = new();

        public bool IsShown => m_Root != null && M3Fade.IsShown(m_Root.gameObject);

        void OnEnable()
        {
            if (m_Demos != null)
                m_Demos.Loaded += OnLoaded;
        }

        void OnDisable()
        {
            if (m_Demos != null)
                m_Demos.Loaded -= OnLoaded;
        }

        void OnLoaded(DemoCatalog.Entry entry)
        {
            if (m_Root != null)
                Rebuild();
        }

        // The toolbar's Open button.
        public void Toggle()
        {
            if (m_Root == null)
                Build();
            var show = !IsShown;
            if (show)
                Rebuild();
            M3Fade.SetShown(m_Root.gameObject, show);
            if (m_Button != null)
                m_Button.Selected = show;
            Debug.Log($"[Demos] Open apps {(show ? "shown" : "hidden")}");
        }

        void Build()
        {
            m_Root = M3.Surface("Open Apps", m_Panel, Vector2.zero, new Vector2(k_Width, k_RowHeight + 2f * k_Padding),
                M3Theme.SurfaceContainerHigh, M3Theme.k_ShapePanel, raycastTarget: true).rectTransform;
            m_Root.gameObject.AddComponent<M3Fade>();
            m_Root.gameObject.SetActive(false);
        }

        void Rebuild()
        {
            foreach (var row in m_Rows)
                Destroy(row);
            m_Rows.Clear();

            var open = new List<DemoCatalog.Entry>();
            if (m_Demos.Current != null)
                open.Add(m_Demos.Current);

            var rows = Mathf.Max(1, open.Count);
            var height = rows * k_RowHeight + (rows - 1) * k_Gap + 2f * k_Padding;
            m_Root.sizeDelta = new Vector2(k_Width, height);
            m_Root.anchoredPosition = new Vector2(0f, -(m_Panel.rect.height / 2f + k_Below + height / 2f));

            if (open.Count == 0)
            {
                m_Rows.Add(M3.Text("Nothing Open", m_Root, "No apps open", M3Theme.Type.BodyLarge, Vector2.zero,
                    new Vector2(k_Width - 2f * k_Padding, k_RowHeight), M3Theme.OnSurfaceVariant).gameObject);
                return;
            }
            for (var i = 0; i < open.Count; i++)
            {
                var entry = open[i];
                var y = height / 2f - k_Padding - k_RowHeight / 2f - i * (k_RowHeight + k_Gap);
                var row = M3.Group($"Open {entry.Id}", m_Root, new Vector2(0f, y), new Vector2(k_Width - 2f * k_Padding, k_RowHeight));
                var tile = M3.Surface("Tile", row, new Vector2(-k_Width / 2f + k_Padding + 28f, 0f), new Vector2(44f, 44f), entry.Tint, 12f);
                M3.Icon("Icon", tile.rectTransform, entry.Icon, 26f, Vector2.zero, Color.white);
                M3.Text("Name", row, entry.Title, M3Theme.Type.BodyLarge, new Vector2(-10f, 0f), new Vector2(k_Width - 200f, k_RowHeight));
                M3.IconButton("Close", row, "close", M3Button.Variant.Tonal, new Vector2(k_Width / 2f - k_Padding - 28f, 0f),
                    () => Close(entry), 48f);
                m_Rows.Add(row.gameObject);
            }
        }

        void Close(DemoCatalog.Entry entry)
        {
            if (m_Demos.Current != entry)
                return;
            Debug.Log($"[Demos] Closed {entry.Id} from the open apps");
            m_Demos.ExitToLobby();
        }
    }
}
