using System.Collections.Generic;
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
        public static ConfigEntry<int> BarWidth;
        public static ConfigEntry<int> BarHeight;
        public static ConfigEntry<bool> ShowPinNames;

        // Minimap.m_pins is private - grab it once via reflection instead of patching anything.
        private static readonly FieldInfo PinsField =
            typeof(Minimap).GetField("m_pins", BindingFlags.NonPublic | BindingFlags.Instance);

        private RectTransform _viewport;
        private readonly Dictionary<Minimap.PinData, GameObject> _markerPool = new Dictionary<Minimap.PinData, GameObject>();
        private readonly List<Minimap.PinData> _toRemove = new List<Minimap.PinData>();
        private GameObject _cardinalN, _cardinalE, _cardinalS, _cardinalW;
        private GameObject _root;
        private Font _font;

        private void Awake()
        {
            PinRange = Config.Bind("General", "PinRange", 300f, "Only show map pins within this many meters.");
            FieldOfView = Config.Bind("General", "FieldOfView", 90f, "Total degrees of heading visible across the compass bar.");
            BarWidth = Config.Bind("Layout", "BarWidth", 600, "Compass bar width in pixels.");
            BarHeight = Config.Bind("Layout", "BarHeight", 36, "Compass bar height in pixels.");
            ShowPinNames = Config.Bind("General", "ShowPinNames", true, "Show pin name text under each icon.");

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
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

            _root = new GameObject("CompassRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            RectTransform rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.sizeDelta = new Vector2(BarWidth.Value, BarHeight.Value);
            rootRt.anchoredPosition = new Vector2(0f, -20f);

            Image bg = _root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            GameObject viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(_root.transform, false);
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

            _cardinalN = CreateLabel("N", Color.white);
            _cardinalE = CreateLabel("E", Color.white);
            _cardinalS = CreateLabel("S", Color.white);
            _cardinalW = CreateLabel("W", Color.white);
        }

        private GameObject CreateLabel(string text, Color color)
        {
            GameObject go = new GameObject("Cardinal_" + text);
            go.transform.SetParent(_viewport, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(30f, BarHeight.Value);
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
            go.AddComponent<Image>();

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
            if (!active)
                return;

            float heading = cam.transform.eulerAngles.y;
            float halfFov = FieldOfView.Value * 0.5f;
            float halfWidth = BarWidth.Value * 0.5f;

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

                Image icon = marker.GetComponent<Image>();
                if (icon.sprite != pin.m_icon)
                    icon.sprite = pin.m_icon;
                icon.enabled = pin.m_icon != null;

                Text label = marker.transform.Find("Label").GetComponent<Text>();
                if (ShowPinNames.Value && !string.IsNullOrEmpty(pin.m_name))
                {
                    int dist = Mathf.RoundToInt(Mathf.Sqrt(sqrDist));
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
