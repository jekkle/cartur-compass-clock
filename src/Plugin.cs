using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace SkyrimCompass
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.skyrimcompass";
        public const string PluginName = "SkyrimCompass";
        public const string PluginVersion = "1.0.0";

        public static ConfigEntry<float> PinRange;
        public static ConfigEntry<float> FieldOfView;
        public static ConfigEntry<int> FrameWidth;
        public static ConfigEntry<float> FrameOffsetY;
        public static ConfigEntry<bool> ShowPinNames;

        // Minimap.m_pins is private - grab it once via reflection instead of patching anything.
        private static readonly FieldInfo PinsField =
            typeof(Minimap).GetField("m_pins", BindingFlags.NonPublic | BindingFlags.Instance);

        // Measured from Assets/compass_frame.png: the inner rune-window as a fraction of the
        // full frame image, center-out scan with a 5px-run noise guard (see repo history).
        private const float WinXMin = 0.1037f, WinXMax = 0.8946f;
        private const float WinYMin = 0.3103f, WinYMax = 0.6092f;
        private const float FrameNativeAspect = 2392f / 348f;

        // The clock sits directly above the frame's top edge, ClockToFrameGap apart.
        private const float ClockHeight = 40f;
        private const float ClockToFrameGap = 0f;

        private RectTransform _viewport;
        private readonly Dictionary<Minimap.PinData, GameObject> _markerPool = new Dictionary<Minimap.PinData, GameObject>();
        private readonly List<Minimap.PinData> _toRemove = new List<Minimap.PinData>();
        private GameObject _cardinalN, _cardinalE, _cardinalS, _cardinalW;
        private GameObject _root;
        private RectTransform _rootRt;
        private Font _font;
        private float _contentWidthPx;
        private Text _clockText;

        private void Awake()
        {
            PinRange = Config.Bind("General", "PinRange", 300f, "Only show map pins within this many meters.");
            FieldOfView = Config.Bind("General", "FieldOfView", 90f, "Total degrees of heading visible across the compass window.");
            FrameWidth = Config.Bind("Layout", "FrameWidth", 552, "Frame width, in reference-resolution pixels (1920x1080 basis - scales with screen size). Height follows the frame image's aspect ratio.");
            FrameOffsetY = Config.Bind("Layout", "FrameOffsetY", 36f, "Distance from the top of the screen down to the frame's top edge, in reference-resolution pixels (1920x1080 basis). The clock sits directly above the frame.");
            ShowPinNames = Config.Bind("General", "ShowPinNames", true, "Show pin name text under each icon.");

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        // Raw RGBA32 (8-byte width/height header + bottom-up pixel data) - see the comment in
        // the .csproj for why this isn't a PNG decoded via Texture2D.LoadImage.
        private Sprite LoadFrameSprite()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SkyrimCompass.compass_frame.rgba"))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                Color32[] pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte r = reader.ReadByte();
                    byte g = reader.ReadByte();
                    byte b = reader.ReadByte();
                    byte a = reader.ReadByte();
                    pixels[i] = new Color32(r, g, b, a);
                }

                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            }
        }

        private void BuildUi()
        {
            GameObject canvasGo = new GameObject("SkyrimCompassCanvas");
            DontDestroyOnLoad(canvasGo);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            float frameW = FrameWidth.Value;
            float frameH = frameW / FrameNativeAspect;

            _root = new GameObject("CompassRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            _rootRt = _root.AddComponent<RectTransform>();
            _rootRt.anchorMin = new Vector2(0.5f, 1f);
            _rootRt.anchorMax = new Vector2(0.5f, 1f);
            _rootRt.pivot = new Vector2(0.5f, 1f);
            _rootRt.sizeDelta = new Vector2(frameW, frameH);
            // Fixed screen position, deliberately not tied to the hotbar or any other mod's UI:
            // the CanvasScaler above means these units are relative to a 1920x1080 basis, so the
            // compass lands in the same relative spot on any resolution, with or without the
            // mods this was originally tuned against.
            _rootRt.anchoredPosition = new Vector2(0f, -FrameOffsetY.Value);

            // Frame art sits behind Content - its window is baked-in opaque black (not a cutout),
            // so the compass content below draws on top of that black backdrop rather than
            // showing through a transparent hole.
            GameObject frameGo = new GameObject("FrameArt");
            frameGo.transform.SetParent(_root.transform, false);
            RectTransform frameRt = frameGo.AddComponent<RectTransform>();
            frameRt.anchorMin = Vector2.zero;
            frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = Vector2.zero;
            frameRt.offsetMax = Vector2.zero;
            Image frameImg = frameGo.AddComponent<Image>();
            frameImg.sprite = LoadFrameSprite();
            frameImg.type = Image.Type.Simple;
            frameImg.raycastTarget = false;

            // Content sits strictly inside the frame's window - measured fractions of the full
            // frame image - so ticks/pins/labels land on the frame's own black backdrop.
            GameObject contentGo = new GameObject("Content");
            contentGo.transform.SetParent(_root.transform, false);
            RectTransform contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(WinXMin, 1f - WinYMax);
            contentRt.anchorMax = new Vector2(WinXMax, 1f - WinYMin);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            _contentWidthPx = (WinXMax - WinXMin) * frameW;

            GameObject viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(contentGo.transform, false);
            _viewport = viewportGo.AddComponent<RectTransform>();
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = Vector2.zero;
            _viewport.offsetMax = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();

            // Center tick - always points at where the player is actually facing.
            GameObject centerTick = new GameObject("CenterTick");
            centerTick.transform.SetParent(_viewport, false);
            RectTransform tickRt = centerTick.AddComponent<RectTransform>();
            tickRt.anchorMin = new Vector2(0.5f, 0f);
            tickRt.anchorMax = new Vector2(0.5f, 1f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.sizeDelta = new Vector2(2f, 0f);
            tickRt.anchoredPosition = Vector2.zero;
            Image tickImg = centerTick.AddComponent<Image>();
            tickImg.color = new Color(1f, 0.85f, 0.2f, 0.9f);

            _cardinalN = CreateLabel("N", new Color(1f, 0.9f, 0.6f));
            _cardinalE = CreateLabel("E", Color.white);
            _cardinalS = CreateLabel("S", Color.white);
            _cardinalW = CreateLabel("W", Color.white);

            // Clock, centered just above the frame's top edge.
            GameObject clockGo = new GameObject("Clock");
            clockGo.transform.SetParent(canvasGo.transform, false);
            RectTransform clockRt = clockGo.AddComponent<RectTransform>();
            clockRt.anchorMin = new Vector2(0.5f, 1f);
            clockRt.anchorMax = new Vector2(0.5f, 1f);
            clockRt.pivot = new Vector2(0.5f, 1f);
            clockRt.sizeDelta = new Vector2(200f, ClockHeight);
            // Glued to the frame's top edge: the frame's top is at -FrameOffsetY, and the clock
            // (top pivot) sits its own height above that.
            clockRt.anchoredPosition = new Vector2(0f, -FrameOffsetY.Value + ClockToFrameGap + ClockHeight);
            _clockText = clockGo.AddComponent<Text>();
            _clockText.font = _font;
            _clockText.fontSize = 26;
            _clockText.fontStyle = FontStyle.Bold;
            _clockText.alignment = TextAnchor.MiddleCenter;
            _clockText.color = new Color(1f, 0.9f, 0.65f);
            Shadow clockShadow = clockGo.AddComponent<Shadow>();
            clockShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            clockShadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private GameObject CreateLabel(string text, Color color)
        {
            GameObject go = new GameObject("Cardinal_" + text);
            go.transform.SetParent(_viewport, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(30f, 0f);
            Text t = go.AddComponent<Text>();
            t.text = text;
            t.font = _font;
            t.fontSize = 18;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            return go;
        }

        private GameObject CreateMarker()
        {
            GameObject go = new GameObject("PinMarker");
            go.transform.SetParent(_viewport, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(20f, 20f);

            // Icon is its own child so it can scale with distance independently of the label -
            // scaling the marker root would also stretch the label's size and offset.
            GameObject iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            RectTransform iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            iconGo.AddComponent<Image>();

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.5f, 0f);
            labelRt.anchorMax = new Vector2(0.5f, 0f);
            labelRt.pivot = new Vector2(0.5f, 1f);
            labelRt.anchoredPosition = new Vector2(0f, -2f);
            labelRt.sizeDelta = new Vector2(120f, 16f);
            Text labelText = labelGo.AddComponent<Text>();
            labelText.font = _font;
            labelText.fontSize = 12;
            labelText.alignment = TextAnchor.UpperCenter;
            labelText.color = Color.white;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;

            return go;
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            GameCamera cam = GameCamera.instance;
            bool active = player != null && cam != null && Minimap.instance != null;
            _root.SetActive(active);
            _clockText.gameObject.SetActive(active);
            if (!active)
                return;

            if (EnvMan.instance != null)
            {
                // GetDayFraction: 0.0/1.0 = midnight, 0.5 = noon - fraction * 24 is the hour directly.
                float dayFraction = EnvMan.instance.GetDayFraction();
                int totalMinutes = (int)(dayFraction * 24f * 60f) % 1440;
                _clockText.text = $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
            }

            float heading = cam.transform.eulerAngles.y;
            float halfFov = FieldOfView.Value * 0.5f;
            float halfWidth = _contentWidthPx * 0.5f;

            PositionOnCompass(_cardinalN, 0f, heading, halfFov, halfWidth);
            PositionOnCompass(_cardinalE, 90f, heading, halfFov, halfWidth);
            PositionOnCompass(_cardinalS, 180f, heading, halfFov, halfWidth);
            PositionOnCompass(_cardinalW, 270f, heading, halfFov, halfWidth);

            UpdatePins(player, heading, halfFov, halfWidth);
        }

        private void PositionOnCompass(GameObject go, float bearing, float heading, float halfFov, float halfWidth)
        {
            float relative = Mathf.DeltaAngle(heading, bearing);
            if (Mathf.Abs(relative) > halfFov)
            {
                go.SetActive(false);
                return;
            }
            go.SetActive(true);
            float x = relative / halfFov * halfWidth;
            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, 0f);
        }

        private void UpdatePins(Player player, float heading, float halfFov, float halfWidth)
        {
            List<Minimap.PinData> pins = (List<Minimap.PinData>)PinsField.GetValue(Minimap.instance);
            Vector3 playerPos = player.transform.position;
            float range = PinRange.Value;
            float sqrRange = range * range;

            _toRemove.Clear();
            foreach (KeyValuePair<Minimap.PinData, GameObject> kv in _markerPool)
            {
                if (!pins.Contains(kv.Key) || (kv.Key.m_pos - playerPos).sqrMagnitude > sqrRange)
                    _toRemove.Add(kv.Key);
            }
            foreach (Minimap.PinData stale in _toRemove)
            {
                Destroy(_markerPool[stale]);
                _markerPool.Remove(stale);
            }

            foreach (Minimap.PinData pin in pins)
            {
                if (pin.m_type == Minimap.PinType.None)
                    continue;

                Vector3 offset = pin.m_pos - playerPos;
                float sqrDist = offset.sqrMagnitude;
                if (sqrDist > sqrRange)
                    continue;

                if (!_markerPool.TryGetValue(pin, out GameObject marker))
                {
                    marker = CreateMarker();
                    _markerPool[pin] = marker;
                }

                float bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                float relative = Mathf.DeltaAngle(heading, bearing);
                if (Mathf.Abs(relative) > halfFov)
                {
                    marker.SetActive(false);
                    continue;
                }

                marker.SetActive(true);
                float x = relative / halfFov * halfWidth;
                marker.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, 0f);

                float distance = Mathf.Sqrt(sqrDist);
                // Current fixed size (20x20) is the far end (distance == range); scale up to
                // 2x as the pin approaches the player.
                float closeness = 1f - Mathf.Clamp01(distance / range);
                float iconScale = Mathf.Lerp(1f, 2f, closeness);
                Transform iconTransform = marker.transform.Find("Icon");
                iconTransform.localScale = Vector3.one * iconScale;

                Image icon = iconTransform.GetComponent<Image>();
                if (icon.sprite != pin.m_icon)
                    icon.sprite = pin.m_icon;
                icon.enabled = pin.m_icon != null;

                Text label = marker.transform.Find("Label").GetComponent<Text>();
                if (ShowPinNames.Value && !string.IsNullOrEmpty(pin.m_name))
                {
                    int dist = Mathf.RoundToInt(distance);
                    label.text = $"{pin.m_name} ({dist}m)";
                    label.enabled = true;
                }
                else
                {
                    label.enabled = false;
                }
            }
        }
    }
}
