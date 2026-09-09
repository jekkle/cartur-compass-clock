using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace CarturCompassAndClock
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturcompassandclock";
        public const string PluginName = "Cartur's Compass and Clock";
        public const string PluginVersion = "1.0.0";

        public static ConfigEntry<float> PinRange;
        public static ConfigEntry<float> FieldOfView;
        public static ConfigEntry<int> FrameWidth;
        public static ConfigEntry<float> FrameOffsetY;
        public static ConfigEntry<bool> ShowPinNames;

        // Cartur's Map Pins repoints a chest pin to a custom "looted" icon once the container is
        // empty; that icon index is its only record of "this chest is cleared out". The compass
        // cannot see emptiness itself, so it reads that mod's own config entries and rebuilds the
        // PinType the same way that mod does (custom types start at 100). Soft: absent mod, custom
        // icons switched off, or moved config keys all leave this at -1 and nothing is hidden.
        private const string MapPinsGuid = "com.jekkle.valheim.carturmappins";
        private const int MapPinsFirstCustomType = 100;
        private ConfigEntry<bool> _mapPinsCustomIcons;
        private ConfigEntry<int> _mapPinsLootedIcon;
        private bool _mapPinsResolved;

        // A pin you are standing on has no meaningful bearing: sub-metre wobble swings atan2
        // through a full circle, so its marker whips across the compass every frame - at max icon
        // scale, since it is also the closest - and collides with every other marker near the
        // centre. Inside this radius the pin is just "here", so it is not drawn at all.
        private const float MinBearingDistance = 8f;

        // Both distance cutoffs are hard edges, and a pin parked on one would have its marker
        // destroyed and rebuilt every single frame. Markers are only released once the pin is
        // this much past the edge it came in through.
        private const float RangeSlack = 1.1f;

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

        // Horizontal fade at the mask's edges, so markers dissolve out of the window instead of
        // being sliced off mid-icon.
        private const int EdgeFadePx = 22;

        // A minor tick every this many degrees, skipping the eight bearings that carry a letter.
        // Without them the window is blank between letters and turning gives no sense of motion.
        private const int MinorTickStep = 15;

        private GameObject _canvasGo;
        private RectTransform _viewport;
        private RectTransform _markerRoot;
        private readonly Dictionary<Minimap.PinData, GameObject> _markerPool = new Dictionary<Minimap.PinData, GameObject>();
        private readonly List<Minimap.PinData> _toRemove = new List<Minimap.PinData>();
        // Letters and ticks, with the world bearing each one sits at.
        private readonly List<GameObject> _bearingMarks = new List<GameObject>();
        private readonly List<float> _bearingAngles = new List<float>();
        // Parallel lists, rebuilt each frame, holding this frame's markers farthest-first.
        private readonly List<Transform> _sortedMarkers = new List<Transform>();
        private readonly List<float> _sortedDistances = new List<float>();
        private Font _font;
        private float _contentWidthPx;
        private Text _clockText;
        private Text _focusLabel;
        private bool _rebuildQueued;

        private void Awake()
        {
            PinRange = Config.Bind("General", "PinRange", 300f, "Only show map pins within this many meters.");
            FieldOfView = Config.Bind("General", "FieldOfView", 90f, "Total degrees of heading visible across the compass window.");
            FrameWidth = Config.Bind("Layout", "FrameWidth", 552, "Frame width, in reference-resolution pixels (1920x1080 basis - scales with screen size). Height follows the frame image's aspect ratio.");
            FrameOffsetY = Config.Bind("Layout", "FrameOffsetY", 36f, "Distance from the top of the screen down to the frame's top edge, in reference-resolution pixels (1920x1080 basis). The clock sits directly above the frame.");
            ShowPinNames = Config.Bind("General", "ShowPinNames", true, "Show the name and distance of the pin nearest the center of the compass, under the frame.");

            // A plugin that throws out of Awake takes the whole chainloader down with it, and
            // every other mod in the profile with it. Nothing here is worth that: if the UI
            // cannot be built, say so in the log, switch this component off so Update never
            // runs, and let the game start without a compass.
            try
            {
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                BuildUi();
            }
            catch (System.Exception e)
            {
                enabled = false;
                Logger.LogError($"Could not build the compass, disabling it: {e}");
                return;
            }

            // Layout is baked into the hierarchy at build time, so a changed setting only shows up
            // if the whole thing is rebuilt. Deferred to Update - this fires off the config watcher.
            Config.SettingChanged += (sender, args) => _rebuildQueued = true;
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        // Raw RGBA32 (8-byte width/height header + bottom-up pixel data) - see the comment in
        // the .csproj for why this isn't a PNG decoded via Texture2D.LoadImage.
        private Sprite LoadFrameSprite()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CarturCompassAndClock.compass_frame.rgba"))
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
            _canvasGo = new GameObject("CarturCompassCanvas");
            DontDestroyOnLoad(_canvasGo);
            Canvas canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            CanvasScaler scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Split the difference between width and height: matching width alone (the default)
            // scales this off the horizontal axis only, so on an ultrawide the compass balloons.
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            float frameW = FrameWidth.Value;
            float frameH = frameW / FrameNativeAspect;

            GameObject root = new GameObject("CompassRoot");
            root.transform.SetParent(_canvasGo.transform, false);
            RectTransform rootRt = root.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.sizeDelta = new Vector2(frameW, frameH);
            // Fixed screen position, deliberately not tied to the hotbar or any other mod's UI:
            // the CanvasScaler above means these units are relative to a 1920x1080 basis, so the
            // compass lands in the same relative spot on any resolution, with or without the
            // mods this was originally tuned against.
            rootRt.anchoredPosition = new Vector2(0f, -FrameOffsetY.Value);

            // Frame art sits behind Content - its window is baked-in opaque black (not a cutout),
            // so the compass content below draws on top of that black backdrop rather than
            // showing through a transparent hole.
            GameObject frameGo = new GameObject("FrameArt");
            frameGo.transform.SetParent(root.transform, false);
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
            // frame image - so ticks and pins land on the frame's own black backdrop.
            GameObject contentGo = new GameObject("Content");
            contentGo.transform.SetParent(root.transform, false);
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
            RectMask2D mask = viewportGo.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(EdgeFadePx, 0);

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

            _bearingMarks.Clear();
            _bearingAngles.Clear();
            Color northColor = new Color(1f, 0.9f, 0.6f);
            Color interColor = new Color(1f, 1f, 1f, 0.8f);
            CreateBearingLabel("N", 0f, 18, northColor);
            CreateBearingLabel("NE", 45f, 12, interColor);
            CreateBearingLabel("E", 90f, 18, Color.white);
            CreateBearingLabel("SE", 135f, 12, interColor);
            CreateBearingLabel("S", 180f, 18, Color.white);
            CreateBearingLabel("SW", 225f, 12, interColor);
            CreateBearingLabel("W", 270f, 18, Color.white);
            CreateBearingLabel("NW", 315f, 12, interColor);
            for (int bearing = 0; bearing < 360; bearing += MinorTickStep)
            {
                if (bearing % 45 == 0)
                    continue;   // a letter already sits here
                CreateBearingTick(bearing);
            }

            // Markers get their own container, added last so they draw over the letters and ticks,
            // and so the per-frame depth sort can reorder siblings without shuffling those.
            GameObject markerRootGo = new GameObject("Markers");
            markerRootGo.transform.SetParent(_viewport, false);
            _markerRoot = markerRootGo.AddComponent<RectTransform>();
            _markerRoot.anchorMin = Vector2.zero;
            _markerRoot.anchorMax = Vector2.one;
            _markerRoot.offsetMin = Vector2.zero;
            _markerRoot.offsetMax = Vector2.zero;

            // Clock, centered just above the frame's top edge.
            GameObject clockGo = new GameObject("Clock");
            clockGo.transform.SetParent(_canvasGo.transform, false);
            RectTransform clockRt = clockGo.AddComponent<RectTransform>();
            clockRt.anchorMin = new Vector2(0.5f, 1f);
            clockRt.anchorMax = new Vector2(0.5f, 1f);
            clockRt.pivot = new Vector2(0.5f, 1f);
            clockRt.sizeDelta = new Vector2(300f, ClockHeight);
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

            // One name, for the pin nearest the center tick - the one you are actually looking at.
            // It sits below the frame rather than under its own marker: the window is only about
            // 24 reference pixels tall, so text hung off a marker falls outside the mask and gets
            // clipped away entirely.
            GameObject focusGo = new GameObject("FocusLabel");
            focusGo.transform.SetParent(_canvasGo.transform, false);
            RectTransform focusRt = focusGo.AddComponent<RectTransform>();
            focusRt.anchorMin = new Vector2(0.5f, 1f);
            focusRt.anchorMax = new Vector2(0.5f, 1f);
            focusRt.pivot = new Vector2(0.5f, 1f);
            focusRt.sizeDelta = new Vector2(500f, 20f);
            focusRt.anchoredPosition = new Vector2(0f, -(FrameOffsetY.Value + frameH) - 2f);
            _focusLabel = focusGo.AddComponent<Text>();
            _focusLabel.font = _font;
            _focusLabel.fontSize = 14;
            _focusLabel.alignment = TextAnchor.UpperCenter;
            _focusLabel.color = new Color(1f, 0.95f, 0.85f);
            _focusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            Shadow focusShadow = focusGo.AddComponent<Shadow>();
            focusShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            focusShadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void CreateBearingLabel(string text, float bearing, int fontSize, Color color)
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
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.raycastTarget = false;
            _bearingMarks.Add(go);
            _bearingAngles.Add(bearing);
        }

        /// Sits on the floor of the window, out from under the pin icons, which are centered.
        private void CreateBearingTick(float bearing)
        {
            GameObject go = new GameObject($"Tick_{bearing:F0}");
            go.transform.SetParent(_viewport, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(2f, 6f);
            Image img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.95f, 0.8f, 0.35f);
            img.raycastTarget = false;
            _bearingMarks.Add(go);
            _bearingAngles.Add(bearing);
        }

        private GameObject CreateMarker()
        {
            GameObject go = new GameObject("PinMarker");
            go.transform.SetParent(_markerRoot, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(20f, 20f);

            // Icon is its own child so it can scale with distance independently of the marker
            // root, whose anchored position is what the bearing math writes to.
            GameObject iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            RectTransform iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            Image iconImg = iconGo.AddComponent<Image>();
            iconImg.raycastTarget = false;

            return go;
        }

        private void Update()
        {
            if (_rebuildQueued)
            {
                _rebuildQueued = false;
                Rebuild();
            }

            Player player = Player.m_localPlayer;
            GameCamera cam = GameCamera.instance;
            bool active = player != null && cam != null && Minimap.instance != null
                          && !Hud.IsUserHidden()                                 // HUD toggled off
                          && Minimap.instance.m_mode != Minimap.MapMode.Large     // big map open
                          && !InventoryGui.IsVisible()
                          && !Menu.IsVisible();
            _canvasGo.SetActive(active);
            if (!active)
                return;

            if (EnvMan.instance != null)
            {
                // GetDayFraction: 0.0/1.0 = midnight, 0.5 = noon - fraction * 24 is the hour directly.
                float dayFraction = EnvMan.instance.GetDayFraction();
                int totalMinutes = (int)(dayFraction * 24f * 60f) % 1440;
                _clockText.text = $"Day {EnvMan.instance.GetDay()} - {totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
            }

            float heading = cam.transform.eulerAngles.y;
            float halfFov = FieldOfView.Value * 0.5f;
            float halfWidth = _contentWidthPx * 0.5f;

            for (int i = 0; i < _bearingMarks.Count; i++)
                PositionOnCompass(_bearingMarks[i], _bearingAngles[i], heading, halfFov, halfWidth);

            UpdatePins(player, heading, halfFov, halfWidth);
        }

        /// Destroying the canvas takes the pooled markers with it, so the pool has to be dropped
        /// too or it hands out already-destroyed objects.
        private void Rebuild()
        {
            Destroy(_canvasGo);
            _markerPool.Clear();
            _sortedMarkers.Clear();
            _sortedDistances.Clear();
            BuildUi();
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

        /// Both the stale-marker sweep and the draw loop have to agree on this, or a pin that
        /// becomes hidden keeps a live marker frozen at its last position. The sweep passes a
        /// slack above 1 so a pin sitting on either distance edge is not rebuilt every frame.
        private bool ShouldShow(Minimap.PinData pin, Vector3 playerPos, float range, int lootedType, float slack)
        {
            if (pin.m_type == Minimap.PinType.None)
                return false;
            if (pin.m_checked)
                return false;                       // ticked off on the map - done with
            if (lootedType >= 0 && (int)pin.m_type == lootedType)
                return false;                       // emptied chest

            Vector3 offset = pin.m_pos - playerPos;
            float far = range * slack;
            if (offset.sqrMagnitude > far * far)
                return false;
            // Bearing is an XZ angle, so the "you are standing on it" test has to be XZ too -
            // a pin straight down a dungeon shaft is close in 3D but still has a real bearing.
            float near = MinBearingDistance / slack;
            return offset.x * offset.x + offset.z * offset.z > near * near;
        }

        /// -1 when there is nothing to hide. Read through the cached ConfigEntry rather than a
        /// cached value, so changing the icon in the config manager takes effect live.
        private int LootedChestType()
        {
            if (!_mapPinsResolved)
            {
                _mapPinsResolved = true;
                if (Chainloader.PluginInfos.TryGetValue(MapPinsGuid, out PluginInfo info) && info.Instance != null)
                {
                    info.Instance.Config.TryGetEntry("CustomIcons", "Enabled", out _mapPinsCustomIcons);
                    info.Instance.Config.TryGetEntry("Chest", "LootedIconIndex", out _mapPinsLootedIcon);
                    if (_mapPinsLootedIcon == null)
                        Logger.LogWarning("CarturMapPins is loaded but Chest/LootedIconIndex was not found - looted chests will still show.");
                }
            }

            if (_mapPinsLootedIcon == null || _mapPinsLootedIcon.Value < 0)
                return -1;
            if (_mapPinsCustomIcons != null && !_mapPinsCustomIcons.Value)
                return -1;
            return MapPinsFirstCustomType + _mapPinsLootedIcon.Value;
        }

        private void UpdatePins(Player player, float heading, float halfFov, float halfWidth)
        {
            List<Minimap.PinData> pins = (List<Minimap.PinData>)PinsField.GetValue(Minimap.instance);
            Vector3 playerPos = player.transform.position;
            float range = PinRange.Value;
            int lootedType = LootedChestType();

            _toRemove.Clear();
            foreach (KeyValuePair<Minimap.PinData, GameObject> kv in _markerPool)
            {
                if (!pins.Contains(kv.Key) || !ShouldShow(kv.Key, playerPos, range, lootedType, RangeSlack))
                    _toRemove.Add(kv.Key);
            }
            foreach (Minimap.PinData stale in _toRemove)
            {
                Destroy(_markerPool[stale]);
                _markerPool.Remove(stale);
            }

            _sortedMarkers.Clear();
            _sortedDistances.Clear();
            string focusName = null;
            float focusDistance = 0f;
            float focusOffAxis = float.MaxValue;

            foreach (Minimap.PinData pin in pins)
            {
                if (!ShouldShow(pin, playerPos, range, lootedType, 1f))
                    continue;

                Vector3 offset = pin.m_pos - playerPos;
                float distance = offset.magnitude;

                if (!_markerPool.TryGetValue(pin, out GameObject marker))
                {
                    marker = CreateMarker();
                    _markerPool[pin] = marker;
                }

                // Farthest first, so the nearest marker - the biggest, and the one worth reading -
                // ends up last in the sibling list and draws on top of the rest. Markers that fail
                // the FOV test below are ordered too, to keep sibling order stable frame to frame.
                int at = _sortedDistances.Count;
                while (at > 0 && _sortedDistances[at - 1] < distance)
                    at--;
                _sortedDistances.Insert(at, distance);
                _sortedMarkers.Insert(at, marker.transform);

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

                // Base size (20x20) is the far end (distance == range); scale up to 2x and fade
                // back up to full opacity as the pin approaches, so depth reads at a glance.
                float closeness = 1f - Mathf.Clamp01(distance / range);
                Transform iconTransform = marker.transform.Find("Icon");
                iconTransform.localScale = Vector3.one * Mathf.Lerp(1f, 2f, closeness);

                Image icon = iconTransform.GetComponent<Image>();
                if (icon.sprite != pin.m_icon)
                    icon.sprite = pin.m_icon;
                icon.enabled = pin.m_icon != null;
                icon.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, closeness));

                float offAxis = Mathf.Abs(relative);
                if (offAxis < focusOffAxis && !string.IsNullOrEmpty(pin.m_name))
                {
                    focusOffAxis = offAxis;
                    focusName = pin.m_name;
                    focusDistance = distance;
                }
            }

            for (int i = 0; i < _sortedMarkers.Count; i++)
            {
                // Reordering dirties the canvas, so only touch the ones that actually moved.
                if (_sortedMarkers[i].GetSiblingIndex() != i)
                    _sortedMarkers[i].SetSiblingIndex(i);
            }

            bool showName = ShowPinNames.Value && focusName != null;
            _focusLabel.enabled = showName;
            if (showName)
                _focusLabel.text = $"{focusName} ({Mathf.RoundToInt(focusDistance)}m)";
        }
    }
}
