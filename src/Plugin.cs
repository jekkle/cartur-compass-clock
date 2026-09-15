using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturCompassAndClock
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturcompassandclock";
        public const string PluginName = "Cartur's Compass and Clock";
        public const string PluginVersion = "1.2.0";

        public static ConfigEntry<float> PinRange;
        public static ConfigEntry<float> FieldOfView;
        public static ConfigEntry<int> FrameWidth;
        public static ConfigEntry<float> FrameOffsetX;
        public static ConfigEntry<float> FrameOffsetY;
        public static ConfigEntry<bool> ShowPinNames;
        public static ConfigEntry<bool> TwelveHourClock;
        public static ConfigEntry<bool> EditMode;

        // Dragging writes to FrameWidth/FrameOffsetX/FrameOffsetY, and a changed setting rebuilds
        // the whole canvas - which re-decodes the frame art pixel by pixel in LoadFrameSprite. Once
        // per drag is fine; once per frame is not. So a drag moves the transforms directly and only
        // commits to config on release.
        private const float MinFrameWidth = 200f;
        private const float MaxFrameWidth = 1600f;

        // Cartur's Map Pins repoints a chest pin to a custom "looted" icon once the container is
        // empty; that icon index is its only record of "this chest is cleared out". The compass
        // cannot see emptiness itself, so it reads that mod's own config entry and rebuilds the
        // PinType the same way that mod does. Soft: absent mod, custom icons switched off, or a
        // key that has moved again all leave this at -1 and nothing is hidden.
        //
        // Map Pins 1.3.0 broke all three halves of that at once, which is why looted chests came
        // back onto the compass: the key was renamed LootedIconIndex -> LootedIcon, its type went
        // from int to a PinIcon enum, and the custom types moved - 100 is now where the old 1.2.2
        // sheet sits, with the current sheet starting above it. Each is read defensively now
        // rather than assumed.
        private const string MapPinsGuid = "com.jekkle.valheim.carturmappins";

        // Where the current sheet starts, if that mod cannot be asked. Only a fallback: the real
        // value is read off its own CustomIcons.CurrentBase, so another sheet does not break this
        // a second time.
        private const int MapPinsCurrentBaseFallback = 183;
        private const int MapPinsLegacyBase = 100;

        // Values this size in the config are a 1.2.2 icon name carried over by that mod, and mean
        // an index into its old sheet rather than the current one.
        private const int MapPinsLegacyMarker = 1000;

        private ConfigEntry<bool> _mapPinsCustomIcons;
        private ConfigEntryBase _mapPinsLootedIcon;
        private int _mapPinsCurrentBase = MapPinsCurrentBaseFallback;
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

        // Measured from Assets/compass_frame_slim.png: the inner window as a fraction of the full
        // frame image. Found by per-row and per-column "how much of this line is opaque black"
        // scans rather than a single centre-out ray - the bar's tick marks poke into the window
        // from the top edge, and a single ray would stop at the first tick it met. The top bound
        // is the row where >95% of the width is still black, i.e. below the tick tips, so content
        // never overlaps them.
        //
        // These four go with whichever art the csproj embeds. For the original wide frame
        // (compass_frame.rgba, 2392x348) they are 0.1037/0.8946, 0.3103/0.6092, aspect 2392/348.
        private const float WinXMin = 0.0615f, WinXMax = 0.9335f;
        private const float WinYMin = 0.2101f, WinYMax = 0.8406f;
        private const float FrameNativeAspect = 2000f / 138f;

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

        // The clock is the one piece of this UI that asks for a real typeface rather than the
        // built-in Arial, so it is TextMeshPro: Valheim's own fonts are all TMP_FontAssets, and
        // legacy UI.Text can only take a font the operating system has installed - which would
        // mean shipping a TTF and hoping, for everyone who installs this.
        //
        // Valheim-AveriaSerifLibre is the serif face the game itself uses, already loaded, so
        // there is nothing to ship. The assets live in resources.assets and their atlases in
        // StreamingAssets/tmp_fonts; the full set is AveriaSansLibre, AveriaSerifLibre, Norse,
        // Norsebold, Prstartk and Rune.
        // Valheim ships no words for the times of day. Its localization CSV - the one the game
        // itself reads, 13k rows across 37 languages - has $hud_mapday ("Day") and $msg_newday
        // ("Day $1") and nothing at all for dawn, morning, afternoon, dusk or night, so
        // Localization has nothing to hand back and these have to come from here.
        //
        // The ten languages most Valheim players run, plus English. Anything else falls through
        // to English, which is what every language got before this existed - so a missing entry
        // reads as it always did rather than as an empty clock.
        //
        // Non-Latin scripts render because Valheim's font assets carry their own TMP fallback
        // chains: the game has no font-swapping code at all (zero Font-typed fields across
        // assembly_valheim and assembly_guiutils), yet its own UI draws Chinese and Russian, so
        // the fallbacks are the only thing that can be doing it. The clock uses a game font and
        // gets the same treatment.
        //
        // Keys are Valheim's own language names, as stored in the "language" pref and as they
        // head the columns of that CSV.
        private const int Dawn = 0, Morning = 1, Afternoon = 2, Dusk = 3, Night = 4, Am = 5, Pm = 6;

        private static readonly Dictionary<string, string[]> Phrases = new Dictionary<string, string[]>
        {
            { "English",              new[] { "Dawn",         "Morning", "Afternoon",  "Dusk",           "Night", "AM",   "PM"   } },
            { "German",               new[] { "Morgengrauen", "Morgen",  "Nachmittag", "Abenddämmerung", "Nacht", "AM",   "PM"   } },
            { "Russian",              new[] { "Рассвет",      "Утро",    "День",       "Сумерки",        "Ночь",  "AM",   "PM"   } },
            { "French",               new[] { "Aube",         "Matin",   "Après-midi", "Crépuscule",     "Nuit",  "AM",   "PM"   } },
            { "Spanish",              new[] { "Amanecer",     "Mañana",  "Tarde",      "Anochecer",      "Noche", "a.m.", "p.m." } },
            { "Italian",              new[] { "Alba",         "Mattino", "Pomeriggio", "Tramonto",       "Notte", "AM",   "PM"   } },
            { "Polish",               new[] { "Świt",         "Poranek", "Popołudnie", "Zmierzch",       "Noc",   "AM",   "PM"   } },
            { "Portuguese_Brazilian", new[] { "Amanhecer",    "Manhã",   "Tarde",      "Anoitecer",      "Noite", "AM",   "PM"   } },
            { "Chinese",              new[] { "黎明",          "早晨",     "下午",        "黄昏",            "夜晚",   "上午",  "下午"  } },
            { "Japanese",             new[] { "夜明け",         "朝",      "午後",        "夕暮れ",           "夜",    "午前",  "午後"  } },
            { "Korean",               new[] { "새벽",          "아침",     "오후",        "황혼",            "밤",    "오전",  "오후"  } },
        };

        // Cleared by Localization.OnLanguageChange and rebuilt on the next frame that needs it -
        // GetSelectedLanguage is a PlayerPrefs read underneath, which is not a per-frame call.
        private string[] _phrases;

        private const string ClockFontName = "Valheim-AveriaSerifLibre";
        private TMP_FontAsset _clockFont;
        private float _contentWidthPx;
        private TextMeshProUGUI _clockText;
        private Text _focusLabel;
        private bool _rebuildQueued;

        // The frame, the clock and the focus label are three siblings under the canvas, each
        // positioned from FrameOffsetY on its own. A drag applies the same delta to all three
        // rather than reparenting them under the frame, which would mean rewriting how every one
        // of them is laid out.
        private readonly List<RectTransform> _movable = new List<RectTransform>();
        private RectTransform _frameRoot;
        private bool _dragging;

        private void Awake()
        {
            PinRange = Config.Bind("General", "PinRange", 300f, "Only show map pins within this many meters.");
            FieldOfView = Config.Bind("General", "FieldOfView", 90f, "Total degrees of heading visible across the compass window.");
            // Defaults are the layout arrived at by dragging it around in edit mode, rounded to
            // whole reference pixels - the fractions a drag leaves behind are well under a screen
            // pixel and only make the config file look like it was measured with a micrometer.
            FrameWidth = Config.Bind("Layout", "FrameWidth", 657, "Frame width, in reference-resolution pixels (1920x1080 basis - scales with screen size). Height follows the frame image's aspect ratio.");
            FrameOffsetX = Config.Bind("Layout", "FrameOffsetX", 0f, "Distance right of screen centre to the middle of the frame, in reference-resolution pixels (1920x1080 basis). Negative moves it left.");
            FrameOffsetY = Config.Bind("Layout", "FrameOffsetY", 54f, "Distance from the top of the screen down to the frame's top edge, in reference-resolution pixels (1920x1080 basis). The clock sits directly above the frame.");
            TwelveHourClock = Config.Bind("General", "TwelveHourClock", true,
                "Show the clock as 12-hour with AM/PM (1:05 PM). Off is 24-hour (13:05).");
            ShowPinNames = Config.Bind("General", "ShowPinNames", true, "Show the name and distance of the pin nearest the center of the compass, under the frame.");
            EditMode = Config.Bind("Layout", "EditMode", false, "Draw a box around the compass and let you drag it to move it, or drag the grip on its right edge to resize it. The compass stays on screen while this is on, even in menus. Turn it off when you are happy with it.");

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
            // Language is switchable from the in-game settings, not just the start menu, so the
            // cached row has to be dropped when it changes rather than read once at load.
            Localization.OnLanguageChange += () => _phrases = null;
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
            rootRt.anchoredPosition = new Vector2(FrameOffsetX.Value, -FrameOffsetY.Value);
            _frameRoot = rootRt;
            _movable.Clear();
            _movable.Add(rootRt);

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
            clockRt.anchoredPosition = new Vector2(FrameOffsetX.Value, -FrameOffsetY.Value + ClockToFrameGap + ClockHeight);
            _movable.Add(clockRt);
            // Built switched off, and switched on by ClaimClockFont once it has a real font.
            // TextMeshProUGUI.Awake calls LoadFontAsset, which - with no font assigned yet -
            // falls back to TMP_Settings.defaultFontAsset, and Valheim leaves that unset. So it
            // logs "The LiberationSans SDF Font Asset was not found. There is no Font Asset
            // assigned to Clock." and returns early, without even a material. The font this
            // clock wants is not loaded at plugin Awake either (see ClaimClockFont), so there is
            // nothing to hand it yet. Unity defers Awake on an inactive object, so building it
            // off means that branch is never reached. Nothing is lost if the font never turns
            // up: LoadFontAsset's early return leaves the text with no material and it would
            // have drawn nothing anyway.
            clockGo.SetActive(false);
            _clockText = clockGo.AddComponent<TextMeshProUGUI>();
            _clockText.fontSize = 26;
            _clockText.fontStyle = FontStyles.Bold;
            _clockText.alignment = TextAlignmentOptions.Center;
            _clockText.textWrappingMode = TextWrappingModes.NoWrap;
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
            focusRt.anchoredPosition = new Vector2(FrameOffsetX.Value, -(FrameOffsetY.Value + frameH) - 2f);
            _movable.Add(focusRt);
            _focusLabel = focusGo.AddComponent<Text>();
            _focusLabel.font = _font;
            _focusLabel.fontSize = 14;
            _focusLabel.alignment = TextAnchor.UpperCenter;
            _focusLabel.color = new Color(1f, 0.95f, 0.85f);
            _focusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            Shadow focusShadow = focusGo.AddComponent<Shadow>();
            focusShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            focusShadow.effectDistance = new Vector2(1.5f, -1.5f);

            if (EditMode.Value)
                BuildEditOverlay(root.transform);
        }

        /// Only built while EditMode is on. The box is the move handle - all of it, rather than a
        /// title bar, because the frame is 24 reference pixels tall and a strip of that is not a
        /// thing anyone can hit. The grip on its right edge resizes. These two are the only
        /// graphics in the whole UI with raycastTarget left on.
        private void BuildEditOverlay(Transform parent)
        {
            GameObject boxGo = new GameObject("EditBox");
            boxGo.transform.SetParent(parent, false);
            RectTransform boxRt = boxGo.AddComponent<RectTransform>();
            boxRt.anchorMin = Vector2.zero;
            boxRt.anchorMax = Vector2.one;
            // Stick out slightly past the frame so the box reads as a box and not as a tint.
            boxRt.offsetMin = new Vector2(-4f, -4f);
            boxRt.offsetMax = new Vector2(4f, 4f);
            Image boxImg = boxGo.AddComponent<Image>();
            boxImg.color = new Color(1f, 0.85f, 0.2f, 0.15f);
            boxGo.AddComponent<DragHandle>().Init(this, false);

            GameObject gripGo = new GameObject("ResizeGrip");
            gripGo.transform.SetParent(boxGo.transform, false);
            RectTransform gripRt = gripGo.AddComponent<RectTransform>();
            gripRt.anchorMin = new Vector2(1f, 0.5f);
            gripRt.anchorMax = new Vector2(1f, 0.5f);
            gripRt.pivot = new Vector2(0.5f, 0.5f);
            gripRt.sizeDelta = new Vector2(14f, 14f);
            gripRt.anchoredPosition = Vector2.zero;
            Image gripImg = gripGo.AddComponent<Image>();
            gripImg.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            gripGo.AddComponent<DragHandle>().Init(this, true);
        }

        private void MoveBy(Vector2 delta)
        {
            foreach (RectTransform rt in _movable)
                rt.anchoredPosition += delta;
        }

        /// The grip sits on the right edge, but the frame's pivot is its centre - so the edge only
        /// travels half as far as the width grows. Doubling the delta makes the edge track the
        /// cursor, and the compass grows about its own centre rather than crawling sideways.
        private void ResizeBy(float deltaX)
        {
            float width = Mathf.Clamp(_frameRoot.sizeDelta.x + deltaX * 2f, MinFrameWidth, MaxFrameWidth);
            _frameRoot.sizeDelta = new Vector2(width, width / FrameNativeAspect);
            // Cached at build time and read every frame by the bearing maths, so it has to keep up
            // with a live resize or the letters drift out of the window until the next rebuild.
            _contentWidthPx = (WinXMax - WinXMin) * width;
        }

        /// Read back off the transforms rather than accumulated during the drag, so whatever the
        /// clamp in ResizeBy decided is what gets saved. Writing these fires SettingChanged, which
        /// rebuilds once - the reason a drag does not write config per frame.
        private void CommitLayout()
        {
            FrameOffsetX.Value = _frameRoot.anchoredPosition.x;
            FrameOffsetY.Value = -_frameRoot.anchoredPosition.y;
            FrameWidth.Value = Mathf.RoundToInt(_frameRoot.sizeDelta.x);
        }

        /// Unity's own drag events, so there is no mouse tracking or hit testing here - the
        /// GraphicRaycaster already on the canvas works out what the cursor is over.
        private class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private Plugin _owner;
            private bool _resizes;

            public void Init(Plugin owner, bool resizes)
            {
                _owner = owner;
                _resizes = resizes;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                _owner._dragging = true;
            }

            public void OnDrag(PointerEventData eventData)
            {
                // delta is screen pixels; everything in this UI is in 1920x1080 reference units,
                // and the CanvasScaler's scaleFactor is exactly that conversion.
                Vector2 delta = eventData.delta / _owner._canvasGo.GetComponent<Canvas>().scaleFactor;
                if (_resizes)
                    _owner.ResizeBy(delta.x);
                else
                    _owner.MoveBy(delta);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                _owner._dragging = false;
                _owner.CommitLayout();
            }
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

        // Resources.FindObjectsOfTypeAll only sees objects that are already loaded, and this
        // plugin's Awake runs before the menu scene has forced the font assets in - so a single
        // lookup at build time finds nothing and the clock would keep whatever TMP handed it.
        // Retried from Update instead, twice a second until it lands, then never again. The
        // clock is built inactive and switched on here, so TMP never wakes up fontless.
        private float _nextFontTry;

        private void ClaimClockFont()
        {
            if (_clockFont != null || _clockText == null || Time.time < _nextFontTry)
                return;

            _nextFontTry = Time.time + 0.5f;
            TMP_FontAsset font = SerifFont();
            if (font == null)
                return;

            _clockText.font = font;
            _clockText.gameObject.SetActive(true);
        }

        private string[] Phrasebook()
        {
            if (_phrases == null)
            {
                string language = Localization.instance.GetSelectedLanguage();
                if (language == null || !Phrases.TryGetValue(language, out _phrases))
                    _phrases = Phrases["English"];
            }

            return _phrases;
        }

        private TMP_FontAsset SerifFont()
        {
            if (_clockFont == null)
            {
                foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (candidate != null && candidate.name == ClockFontName)
                    {
                        _clockFont = candidate;
                        break;
                    }
                }
            }

            return _clockFont;
        }

        private void Update()
        {
            // Rebuilding destroys the canvas, which would take the box being dragged with it.
            if (_rebuildQueued && !_dragging)
            {
                _rebuildQueued = false;
                Rebuild();
            }

            ClaimClockFont();

            Player player = Player.m_localPlayer;
            GameCamera cam = GameCamera.instance;
            bool active = player != null && cam != null && Minimap.instance != null;
            // While editing, the compass stays up through the inventory and the menu - otherwise
            // turning the setting on from the config manager makes the thing you are positioning
            // disappear behind the window you turned it on in.
            if (active && !EditMode.Value)
                active = !Hud.IsUserHidden()                                   // HUD toggled off
                         && Minimap.instance.m_mode != Minimap.MapMode.Large   // big map open
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
                int hour = totalMinutes / 60;
                int minute = totalMinutes % 60;

                // 12-hour wraps both ends: hour 0 reads 12 AM and hour 12 reads 12 PM, which is
                // what "hour % 12" alone gets wrong in exactly those two cases.
                string[] words = Phrasebook();
                string time = TwelveHourClock.Value
                    ? $"{(hour % 12 == 0 ? 12 : hour % 12)}:{minute:D2} {(hour < 12 ? words[Am] : words[Pm])}"
                    : $"{hour:D2}:{minute:D2}";

                _clockText.text = $"{PhaseName(dayFraction, words)} {EnvMan.instance.GetDay()} - {time}";
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

                    // Through the indexer rather than TryGetEntry<T>: the generic form has to be
                    // told the type, and being told it wrongly is exactly what broke this. The
                    // indexer hands back the entry whatever it holds, and BoxedValue reads it
                    // without this mod ever naming PinIcon.
                    foreach (string key in new[] { "LootedIcon", "LootedIconIndex" })
                    {
                        var definition = new ConfigDefinition("Chest", key);
                        if (!info.Instance.Config.ContainsKey(definition))
                            continue;
                        _mapPinsLootedIcon = info.Instance.Config[definition];
                        break;
                    }

                    if (_mapPinsLootedIcon == null)
                        Logger.LogWarning("CarturMapPins is loaded but its looted-chest icon setting was not found - looted chests will still show.");

                    // Its own idea of where the current sheet starts, so a future sheet does not
                    // silently put this back where it was.
                    try
                    {
                        Type icons = info.Instance.GetType().Assembly.GetType("CarturMapPins.CustomIcons");
                        FieldInfo baseField = icons?.GetField("CurrentBase",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        if (baseField != null && baseField.IsLiteral)
                            _mapPinsCurrentBase = (int)baseField.GetRawConstantValue();
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"Could not read CarturMapPins.CustomIcons.CurrentBase, assuming {MapPinsCurrentBaseFallback}: {e.Message}");
                    }
                }
            }

            if (_mapPinsLootedIcon == null)
                return -1;
            if (_mapPinsCustomIcons != null && !_mapPinsCustomIcons.Value)
                return -1;

            int index;
            try
            {
                index = Convert.ToInt32(_mapPinsLootedIcon.BoxedValue);
            }
            catch (Exception)
            {
                return -1;
            }

            if (index < 0)
                return -1;
            if (index >= MapPinsLegacyMarker)
                return MapPinsLegacyBase + (index - MapPinsLegacyMarker);
            return _mapPinsCurrentBase + index;
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
            Minimap.PinData focusPin = null;
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
                    focusPin = pin;
                    focusDistance = distance;
                }
            }

            for (int i = 0; i < _sortedMarkers.Count; i++)
            {
                // Reordering dirties the canvas, so only touch the ones that actually moved.
                if (_sortedMarkers[i].GetSiblingIndex() != i)
                    _sortedMarkers[i].SetSiblingIndex(i);
            }

            bool showName = ShowPinNames.Value && focusPin != null;
            _focusLabel.enabled = showName;
            if (showName)
                _focusLabel.text = $"{PinLabel(focusPin)} ({Mathf.RoundToInt(focusDistance)}m)";
        }

        // EnvMan defines only three phases of its own - Day (0.25-0.75), Afternoon (0.50-0.75)
        // and Night - so dawn, morning and dusk are this mod's, and they subdivide the daylight
        // half only. Night keeps the whole 0.75-0.25 the game's spawn tables and sleep rules run
        // on, which is what lets the word answer "is it safe out" by itself.
        //
        // Night is asked of EnvMan rather than recomputed here so the two can never disagree:
        // its test is inclusive at both ends (<= 0.25 || >= 0.75) and a reimplementation would
        // drift at exactly 06:00 and 18:00. IsNight, not IsDaylight - IsDaylight is false in any
        // m_alwaysDark environment and would read "Night" in the Mistlands at noon.
        //
        // Dusk is two hours, not one: at the default day length an in-game hour is about 75 real
        // seconds, and a warning you can miss by blinking is not a warning.
        //
        // The day number still rolls at midnight, since GetDay is time / dayLengthSec - so a night
        // reads "Night 42" before midnight and "Night 43" after. That is the number the death and
        // sleep screens show, so it agrees with the rest of the game.
        private static string PhaseName(float dayFraction, string[] words)
        {
            if (EnvMan.IsNight())
                return words[Night];

            if (dayFraction < 1f / 3f)       // 06:00 - 08:00
                return words[Dawn];
            if (dayFraction < 0.5f)          // 08:00 - 12:00
                return words[Morning];
            if (dayFraction < 2f / 3f)       // 12:00 - 16:00
                return words[Afternoon];

            return words[Dusk];              // 16:00 - 18:00
        }

        // Pin names are stored as raw localization tokens, not display text - a chest pin holds
        // "$piece_chestwood". The map never shows that because it runs the name through
        // Localization on its way to the label, and through the UGC filter as well when the pin
        // came from another player. Minimap.PinData.SetTextAndGameObject:
        //
        //     if (!ParentPin.m_author.IsValid || ParentPin.m_author == <local user>)
        //         PinNameText.text = Localization.instance.Localize(ParentPin.m_name);
        //     else
        //         PinNameText.text = CensorShittyWords.FilterUGC(
        //             Localization.instance.Localize(ParentPin.m_name), UGCType.Text, ParentPin.m_author, 0L);
        //
        // Both halves are copied, not just the localization: on a server the nearest pin can be
        // one somebody else named, and filtering is the game's call to make, not this mod's.
        private static string PinLabel(Minimap.PinData pin)
        {
            string name = Localization.instance.Localize(pin.m_name);

            if (!pin.m_author.IsValid ||
                pin.m_author == Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID)
                return name;

            return CensorShittyWords.FilterUGC(name, UGCType.Text, pin.m_author, 0L);
        }
    }
}
