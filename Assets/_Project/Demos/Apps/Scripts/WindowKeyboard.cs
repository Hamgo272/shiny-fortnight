using System;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    // A small keyboard for the browser window, looked at and pinched key by key like any panel. What's
    // typed collects in the line at the top, and nothing reaches the app until one of two keys:
    //   Go opens it in the browser: as an address if it looks like one, else as a Google search
    //   (ToUrl). That goes through an intent, so it works whatever the page has focused.
    //   Type sends it to whatever field is focused on the page (tap the field on the window first),
    //   and Enter after it, for a site's own search box or a form.
    // Android's own keyboard is kept off the window's display (WindowService), since it would come up
    // on the phone's screen, over the stereo view. Lowercase only, which addresses and searches don't
    // mind. Logs [Apps].
    public class WindowKeyboard : MonoBehaviour
    {
        const float k_Key = 56f;
        const float k_Gap = 6f;
        const float k_RowGap = 8f;
        const float k_Padding = 20f;
        const int k_Columns = 10;
        public const float k_WidthDp = k_Columns * k_Key + (k_Columns - 1) * k_Gap + 2f * k_Padding;
        public const float k_HeightDp = 6f * k_Key + 5f * k_RowGap + 2f * k_Padding;

        static readonly string[] k_Rows =
        {
            "1234567890",
            "qwertyuiop",
            "asdfghjkl-",
            "zxcvbnm./:",
        };

        RectTransform m_Root;
        TMP_Text m_Line;
        string m_Text = "";

        public event Action<string> Go;
        public event Action<string> Type;
        public event Action Closed;

        public RectTransform Root => m_Root;
        public bool IsShown => m_Root != null && m_Root.gameObject.activeSelf;

        public static WindowKeyboard Build(Transform parent, float distance)
        {
            var root = WorldUi.Panel("Window Keyboard", parent, new Vector2(k_WidthDp, k_HeightDp), distance);
            var keyboard = root.gameObject.AddComponent<WindowKeyboard>();
            keyboard.m_Root = root;
            keyboard.BuildKeys();
            root.gameObject.SetActive(false);
            return keyboard;
        }

        // The middle of a key, in dp from the panel's middle, by row (0 the entry line) and column.
        static Vector2 KeyPosition(int row, float column)
        {
            var x = -k_WidthDp / 2f + k_Padding + k_Key / 2f + column * (k_Key + k_Gap);
            var y = k_HeightDp / 2f - k_Padding - k_Key / 2f - row * (k_Key + k_RowGap);
            return new Vector2(x, y);
        }

        void BuildKeys()
        {
            // The entry line, nine keys wide, and Backspace in the tenth place.
            var lineWidth = 9f * k_Key + 8f * k_Gap;
            var linePosition = KeyPosition(0, 4f);
            M3.Surface("Line", m_Root, linePosition, new Vector2(lineWidth, k_Key), M3Theme.SurfaceContainerHighest, M3Theme.k_ShapeFull);
            m_Line = M3.Text("Text", m_Root, "", M3Theme.Type.BodyLarge, linePosition, new Vector2(lineWidth - 40f, k_Key));
            m_Line.alignment = TextAlignmentOptions.MidlineLeft;
            m_Line.overflowMode = TextOverflowModes.Ellipsis;
            M3.IconButton("Backspace", m_Root, "backspace", M3Button.Variant.Tonal, KeyPosition(0, 9f), Backspace, k_Key);

            for (var row = 0; row < k_Rows.Length; row++)
            {
                var keys = k_Rows[row];
                for (var column = 0; column < keys.Length; column++)
                {
                    var key = keys[column].ToString();
                    M3.Button($"Key {key}", m_Root, key, M3Button.Variant.Tonal, KeyPosition(row + 1, column), () => Add(key),
                        width: k_Key);
                }
            }

            // Close, .com, Space, Type, Go: 1 + 2 + 3 + 2 + 2 keys wide.
            const int bottom = 5;
            M3.IconButton("Close Keyboard", m_Root, "keyboard_hide", M3Button.Variant.Text, KeyPosition(bottom, 0f), Close, k_Key);
            M3.Button("Dot Com", m_Root, ".com", M3Button.Variant.Tonal, KeyPosition(bottom, 1.5f), () => Add(".com"), width: Span(2));
            M3.Button("Space", m_Root, "space", M3Button.Variant.Tonal, KeyPosition(bottom, 4f), () => Add(" "), width: Span(3));
            M3.Button("Type", m_Root, "Type", M3Button.Variant.Outlined, KeyPosition(bottom, 6.5f), SendType, width: Span(2));
            M3.Button("Go", m_Root, "Go", M3Button.Variant.Filled, KeyPosition(bottom, 8.5f), SendGo, width: Span(2));
        }

        static float Span(int keys) => keys * k_Key + (keys - 1) * k_Gap;

        public void Show(Transform head, float distance, float pitch)
        {
            WorldUi.PlaceInFront(m_Root, head, distance, pitch);
            m_Root.gameObject.SetActive(true);
            Debug.Log("[Apps] Keyboard shown");
        }

        public void Close()
        {
            if (!IsShown)
                return;
            m_Root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void Add(string text)
        {
            m_Text += text;
            m_Line.text = m_Text;
        }

        void Backspace()
        {
            if (m_Text.Length == 0)
                return;
            m_Text = m_Text.Substring(0, m_Text.Length - 1);
            m_Line.text = m_Text;
        }

        void SendGo()
        {
            var url = ToUrl(m_Text);
            if (url == null)
                return;
            Debug.Log($"[Apps] Keyboard: open {url}");
            Go?.Invoke(url);
            Clear();
            Close();
        }

        void SendType()
        {
            if (m_Text.Length == 0)
                return;
            Debug.Log($"[Apps] Keyboard: type {m_Text.Length} characters into the page");
            Type?.Invoke(m_Text);
            Clear();
            Close();
        }

        void Clear()
        {
            m_Text = "";
            m_Line.text = "";
        }

        // What the browser should open for what was typed: an address as it is (https:// added if it
        // has no scheme) when it has no spaces and a dot inside, such as "wikipedia.org" or
        // "m.youtube.com/feed"; anything else as a Google search. Null for nothing typed.
        public static string ToUrl(string typed)
        {
            var text = typed?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return text;
            var dot = text.IndexOf('.');
            if (!text.Contains(" ") && dot > 0 && dot < text.Length - 1)
                return "https://" + text;
            return "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
        }
    }
}
