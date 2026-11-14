using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Interactive 3D globe rendered in software from NASA Blue Marble and Black Marble imagery,
    ///  with Google Earth style handling: grab-and-drag rotation with inertia, wheel zoom anchored
    ///  under the cursor, right-drag (or Ctrl+drag) to tilt and turn, double-click and FlyTo
    ///  flights along great circles, Shift+drag to select a lat/lon box, and a HUD with a
    ///  compass, zoom buttons and a cursor read-out. Overlays show where satellite data is:
    ///  markers with labels and tooltips, regions, a gridded colour layer such as a trend per
    ///  cell, satellites on their orbits and measured ground tracks.
    ///  <see cref="GlobeRenderer"/> shades the planet; this class owns the camera animation,
    ///  the input and the crisp GDI+ layer drawn over the frame.
    /// </summary>
    [DefaultEvent(nameof(MarkerClicked))]
    public class GlobeControl : VigilControl
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int MK_SHIFT = 0x0004;
        private const int MK_CONTROL = 0x0008;

        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
        private const double HomeLatitude = 20;
        private const double HomeLongitude = 0;
        private const float LatitudeLimit = 85f;
        private const double AutoRotatePauseSeconds = 4;
        private const double FrameBudgetMilliseconds = 17;
        private const double ResizeSettleSeconds = 0.15;
        private const double SunRefreshSeconds = 30;
        private const float MinimumVisibleShift = 0.25f;   // pixels: smaller moves aren't worth a repaint
        private const double EaseRate = 11;       // per second: wheel zoom, nudges, compass reset
        private const double InertiaDecay = 4;    // per second

        /// <summary>The camera's five numbers. Longitude may run outside ±180 mid-motion.</summary>
        private struct ViewState
        {
            public float Latitude, Longitude, ZoomLog, Heading, Tilt;
        }

        /// <summary>One FlyTo in progress: eased along a great circle, lifting mid-way on long hops.</summary>
        private sealed class Flight
        {
            public ViewState From, To;
            public Vector3 FromUnit, ToUnit;
            public double Start, Duration;
            public bool EaseIn;
            public float Hop;
            public float HeadingDelta;
        }

        private enum DragMode { None, Grab, Orbit, Select }
        private enum HudButton { None, ZoomIn, ZoomOut, Home, Compass }

        private readonly GlobeRenderer renderer = new();
        private readonly GlobeRenderer.Scene scene = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<GlobeMarker> markers = new();
        private readonly List<GlobeRegion> regions = new();
        private readonly List<(GlobeMarker Marker, PointF Screen, float Facing)> markerHits = new();
        private readonly List<RectangleF> labelBoxes = new();
        private readonly Dictionary<HudButton, RectangleF> hudButtons = new();

        // One clock drives every animation (flights, inertia, eased zoom, auto-rotation, the
        // satellite, resize settling) so they step together on wall-clock time and share one
        // repaint per tick. It runs only while something is moving and the control is visible.
        private readonly System.Windows.Forms.Timer frameClock = new() { Interval = 16 };

        private ViewState view;
        private ViewState target;
        private bool easing;
        private Flight? flight;
        private bool inertia;
        private float inertiaLatitude;
        private float inertiaLongitude;

        private GridOverlay? grid;
        private float gridOpacity = 0.75f;
        private GlobeGroundTrack? groundTrack;
        private readonly List<GlobeSatellite> satellites = new();
        private readonly Dictionary<GlobeSatellite, PointF> paintedSatellites = new();
        private float renderedLongitude;
        private Rectangle sunDotBounds;
        private int paintedSunDotAlpha = -1;
        private GlobeTextures? textures;
        private string? textureError;
        private bool sceneDirty = true;
        private float renderedScale = 1f;
        private float motionScale = 1f;
        private bool wasMoving;
        private double fullFrameMilliseconds = 8;
        private int motionFrames;
        private double motionMilliseconds;
        private double lastTickSeconds;
        private double lastResizeSeconds = -1;
        private double lastSunRefreshSeconds;
        private bool resizing;

        private DragMode drag;
        private bool shiftHeld;
        private bool controlHeld;
        private Point dragStart;
        private Point lastMouse;
        private double lastMoveSeconds;
        private (double Latitude, double Longitude)? grabPoint;
        private (double Latitude, double Longitude)? selectAnchor;
        private float velocityLatitude;
        private float velocityLongitude;
        private double lastInteractionSeconds = double.NegativeInfinity;
        private GlobeMarker? hoverMarker;
        private GlobeMarker? selectedMarker;
        private HudButton hudHover;
        private (double Latitude, double Longitude)? cursorPosition;
        private GlobeRegion? selection;
        private GlobeRegion? selectionPreview;
        private Rectangle readoutBounds;

        private bool autoRotate = true;
        private float autoRotateSpeed = 1.5f;
        private bool useRealSunPosition = true;
        private DateTime? sunTimeUtc;
        private double sunLatitude = 23.4;
        private double sunLongitude;
        private bool showStars = true;
        private bool showGraticule;
        private bool showHud = true;
        private bool showAttribution = true;
        private bool showFrameRate;
        private bool flyToMarkerOnClick = true;

        public event EventHandler<MarkerEventArgs>? MarkerClicked;
        public event EventHandler<SurfaceClickEventArgs>? SurfaceClicked;
        public event EventHandler<RegionEventArgs>? RegionSelected;
        public event EventHandler<FlightEventArgs>? FlyToComplete;
        public event EventHandler? ViewChanged;
        public event EventHandler? GridOverlayChanged;
        public event EventHandler? SelectedMarkerChanged;

        public GlobeControl()
        {
            view = new ViewState { Latitude = (float)HomeLatitude, Longitude = (float)HomeLongitude };
            target = view;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            frameClock.Tick += FrameClock_Tick;

            // Set VIGIL_GLOBE_STATS=1 in the environment to see render timings without touching code.
            showFrameRate = Environment.GetEnvironmentVariable("VIGIL_GLOBE_STATS") == "1";
        }

        protected override Size DefaultSize => new(800, 600);

        // ----- Options -----

        /// <summary>Largest zoom factor; 1 fits the whole planet in the view.</summary>
        public static float MaxZoom => GlobeCamera.MaxZoom;

        /// <summary>Turn slowly when nobody is interacting. Pauses for a few seconds after any input.</summary>
        [DefaultValue(true)]
        public bool AutoRotate
        {
            get => autoRotate;
            set { autoRotate = value; UpdateFrameClock(); }
        }

        /// <summary>Auto-rotation speed in degrees of longitude per second.</summary>
        [DefaultValue(1.5f)]
        public float AutoRotateSpeed
        {
            get => autoRotateSpeed;
            set => autoRotateSpeed = value;
        }

        /// <summary>Light the planet from the real sun. Off, SunLatitude and SunLongitude apply.</summary>
        [DefaultValue(true)]
        public bool UseRealSunPosition
        {
            get => useRealSunPosition;
            set { useRealSunPosition = value; MarkSceneDirty(); UpdateFrameClock(); }
        }

        /// <summary>
        ///  Freezes the real sun at a UTC instant; null follows the current time. Only applies
        ///  while <see cref="UseRealSunPosition"/> is on.
        /// </summary>
        [Browsable(false)]
        public DateTime? SunTimeUtc
        {
            get => sunTimeUtc;
            set { sunTimeUtc = value; MarkSceneDirty(); UpdateFrameClock(); }
        }

        [DefaultValue(23.4)]
        public double SunLatitude
        {
            get => sunLatitude;
            set { sunLatitude = value; MarkSceneDirty(); }
        }

        [DefaultValue(0.0)]
        public double SunLongitude
        {
            get => sunLongitude;
            set { sunLongitude = value; MarkSceneDirty(); }
        }

        [DefaultValue(true)]
        public bool ShowStars
        {
            get => showStars;
            set { showStars = value; MarkSceneDirty(); }
        }

        /// <summary>Faint latitude and longitude lines, spaced more finely as you zoom in.</summary>
        [DefaultValue(false)]
        public bool ShowGraticule
        {
            get => showGraticule;
            set { showGraticule = value; Invalidate(); }
        }

        /// <summary>Compass, zoom buttons and the cursor read-out.</summary>
        [DefaultValue(true)]
        public bool ShowHud
        {
            get => showHud;
            set
            {
                showHud = value;
                if (!value)
                {
                    hudButtons.Clear();
                    hudHover = HudButton.None;
                }
                Invalidate();
            }
        }

        [DefaultValue(true)]
        public bool ShowAttribution
        {
            get => showAttribution;
            set { showAttribution = value; Invalidate(); }
        }

        /// <summary>Render timing in the corner, for checking performance.</summary>
        [DefaultValue(false)]
        public bool ShowFrameRate
        {
            get => showFrameRate;
            set { showFrameRate = value; Invalidate(); }
        }

        /// <summary>Clicking a marker selects it and flies in to it.</summary>
        [DefaultValue(true)]
        public bool FlyToMarkerOnClick
        {
            get => flyToMarkerOnClick;
            set => flyToMarkerOnClick = value;
        }

        /// <summary>Opacity of the gridded overlay, 0 to 1. Changing it doesn't rebuild the grid.</summary>
        [DefaultValue(0.75f)]
        public float GridOverlayOpacity
        {
            get => gridOpacity;
            set
            {
                gridOpacity = Math.Clamp(value, 0f, 1f);
                MarkSceneDirty();
                GridOverlayChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ----- State -----

        [Browsable(false)]
        public double CentreLatitude => view.Latitude;

        [Browsable(false)]
        public double CentreLongitude => Wrap(view.Longitude);

        [Browsable(false)]
        public double Zoom => Math.Pow(2, view.ZoomLog);

        /// <summary>Compass direction the camera faces, in degrees clockwise from north.</summary>
        [Browsable(false)]
        public double Heading => Wrap(view.Heading);

        /// <summary>Camera lean in degrees: 0 looks straight down, up to 70 towards the horizon.</summary>
        [Browsable(false)]
        public double Tilt => view.Tilt;

        /// <summary>Height of the eye above the surface in kilometres.</summary>
        [Browsable(false)]
        public double AltitudeKm => CurrentCamera().AltitudeKm;

        [Browsable(false)]
        public bool IsFlying => flight != null;

        [Browsable(false)]
        public bool TexturesLoaded => textures != null;

        [Browsable(false)]
        public IReadOnlyList<GlobeMarker> Markers => markers;

        [Browsable(false)]
        public IReadOnlyList<GlobeRegion> Regions => regions;

        [Browsable(false)]
        public GlobeMarker? SelectedMarker => selectedMarker;

        /// <summary>The box drawn with Shift+drag, or null.</summary>
        [Browsable(false)]
        public GlobeRegion? Selection => selection;

        [Browsable(false)]
        public ColourScale? GridOverlayScale => grid?.Scale;

        [Browsable(false)]
        public double? GridOverlayMinimum => grid?.Minimum;

        [Browsable(false)]
        public double? GridOverlayMaximum => grid?.Maximum;

        /// <summary>Milliseconds the last frame took to shade, at <see cref="LastRenderScale"/>.</summary>
        [Browsable(false)]
        public double LastFrameMilliseconds => renderer.LastFrameMilliseconds;

        /// <summary>Resolution the last frame was rendered at, relative to the control (1 = full).</summary>
        [Browsable(false)]
        public float LastRenderScale => renderedScale;

        // ----- View -----

        /// <summary>Jumps straight to a latitude and longitude with no animation.</summary>
        public void CentreOn(double latitudeDegrees, double longitudeDegrees)
        {
            CancelMotion();
            view.Latitude = ClampLatitude((float)latitudeDegrees);
            view.Longitude = Wrap((float)longitudeDegrees);
            target = view;
            NoteInteraction();
            MarkSceneDirty();
        }

        /// <summary>
        ///  Flies along the great circle to a point, easing out as it lands. Long hops rise to a
        ///  wider view mid-way, like Google Earth. Calling it again mid-flight redirects smoothly.
        ///  <paramref name="zoom"/> keeps the current zoom when null; <paramref name="duration"/>
        ///  is worked out from the distance when null. <see cref="FlyToComplete"/> fires on landing.
        /// </summary>
        public void FlyTo(double latitudeDegrees, double longitudeDegrees, double? zoom = null, TimeSpan? duration = null)
        {
            var to = flight?.To ?? (easing ? target : view);
            to.Latitude = ClampLatitude((float)latitudeDegrees);
            to.Longitude = (float)longitudeDegrees;
            if (zoom is double z)
            {
                to.ZoomLog = ClampZoomLog((float)Math.Log2(z));
            }
            StartFlight(to, duration);
        }

        /// <summary>Flies to a marker and zooms in to at least <paramref name="zoom"/>.</summary>
        public void FlyTo(GlobeMarker marker, double zoom = 3, TimeSpan? duration = null)
        {
            FlyTo(marker.Latitude, marker.Longitude, Math.Max(Zoom, zoom), duration);
        }

        /// <summary>Flies to frame a region's bounding box.</summary>
        public void FlyTo(GlobeRegion region, TimeSpan? duration = null)
        {
            FlyToRegion(region.LatitudeMin, region.LatitudeMax, region.LongitudeMin, region.LongitudeMax, duration);
        }

        /// <summary>Flies to frame a named place from <see cref="Places"/>.</summary>
        public void FlyTo(Place place, TimeSpan? duration = null)
        {
            FlyToRegion(place.LatitudeMin, place.LatitudeMax, place.LongitudeMin, place.LongitudeMax, duration);
        }

        /// <summary>
        ///  Flies to a marker by label or a place by name (see <see cref="Places"/>). Case doesn't
        ///  matter and a prefix will do. Returns false if nothing matched.
        /// </summary>
        public bool FlyTo(string name, TimeSpan? duration = null)
        {
            string q = (name ?? "").Trim();
            if (q.Length == 0)
            {
                return false;
            }

            var marker = markers.FirstOrDefault(m => m.Label.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? markers.FirstOrDefault(m => m.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase));
            if (marker != null)
            {
                FlyTo(marker, 3, duration);
                return true;
            }

            var place = Places.Find(q);
            if (place == null)
            {
                return false;
            }
            FlyTo(place, duration);
            return true;
        }

        /// <summary>Flies to the view that fits a lat/lon box, with a little margin round it.</summary>
        public void FlyToRegion(double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax, TimeSpan? duration = null)
        {
            double span = longitudeMax - longitudeMin;
            if (span <= 0) span += 360;
            span = Math.Min(span, 360);
            double centreLatitude = (latitudeMin + latitudeMax) / 2;
            double centreLongitude = Wrap((float)(longitudeMin + span / 2));
            FlyTo(centreLatitude, centreLongitude, FitZoom(latitudeMin, latitudeMax, span, centreLatitude), duration);
        }

        public void ZoomIn() => ZoomBy(0.5f, null);

        public void ZoomOut() => ZoomBy(-0.5f, null);

        /// <summary>Flies back to the starting view of the whole planet, north up and untilted.</summary>
        public void ResetView()
        {
            StartFlight(new ViewState { Latitude = (float)HomeLatitude, Longitude = (float)HomeLongitude }, null);
        }

        /// <summary>Turns the camera back to north up, keeping its position and tilt.</summary>
        public void ResetNorth()
        {
            CancelFlightAndInertia();
            view.Heading = Wrap(view.Heading);
            target.Heading = 0f;
            easing = true;
            NoteInteraction();
            UpdateFrameClock();
        }

        /// <summary>Latitude and longitude under a point in the control, if it's on the planet.</summary>
        public bool TryGetSurfacePoint(Point location, out double latitudeDegrees, out double longitudeDegrees)
        {
            return CurrentCamera().Unproject(location.X, location.Y, out latitudeDegrees, out longitudeDegrees);
        }

        /// <summary>Highlights a marker (null clears it) and optionally flies in to it.</summary>
        public void SelectMarker(GlobeMarker? marker, bool fly = false)
        {
            if (marker != null && !markers.Contains(marker))
            {
                marker = null;
            }
            if (selectedMarker != marker)
            {
                selectedMarker = marker;
                SelectedMarkerChanged?.Invoke(this, EventArgs.Empty);
            }
            if (fly && marker != null)
            {
                FlyTo(marker);
            }
            Invalidate();
        }

        /// <summary>Removes the Shift+drag selection box.</summary>
        public void ClearSelection()
        {
            selection = null;
            selectionPreview = null;
            Invalidate();
        }

        // ----- Overlays -----

        public GlobeMarker AddMarker(double latitudeDegrees, double longitudeDegrees, string label, Color colour, string detail = "")
        {
            var marker = new GlobeMarker(latitudeDegrees, longitudeDegrees, label, colour) { Detail = detail };
            markers.Add(marker);
            Invalidate();
            return marker;
        }

        public void RemoveMarker(GlobeMarker marker)
        {
            if (markers.Remove(marker))
            {
                if (hoverMarker == marker) hoverMarker = null;
                if (selectedMarker == marker) SelectMarker(null);
                Invalidate();
            }
        }

        public void ClearMarkers()
        {
            markers.Clear();
            hoverMarker = null;
            SelectMarker(null);
            Invalidate();
        }

        /// <summary>Outlines and tints a lat/lon box. Longitudes may cross the dateline.</summary>
        public GlobeRegion AddRegion(double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax,
            Color colour, string label = "", float fillOpacity = 0.2f)
        {
            var region = new GlobeRegion(latitudeMin, latitudeMax, longitudeMin, longitudeMax, colour)
            {
                Label = label,
                FillOpacity = fillOpacity
            };
            regions.Add(region);
            MarkSceneDirty();
            return region;
        }

        public void ClearRegions()
        {
            regions.Clear();
            MarkSceneDirty();
        }

        /// <summary>
        ///  Paints a grid of values onto the surface, for example a trend per cell.
        ///  <paramref name="values"/>[row, column]: rows run south to north from
        ///  <paramref name="latitudeMin"/>, columns west to east from <paramref name="longitudeMin"/>.
        ///  NaN leaves a cell clear. Without an explicit range, the colour scale spans the data
        ///  (symmetrically about zero for a diverging scale).
        /// </summary>
        public void SetGridOverlay(double[,] values, double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax,
            ColourScale? scale = null, double? minimum = null, double? maximum = null, float opacity = 0.75f, bool smooth = false)
        {
            scale ??= ColourScale.BlueWhiteRed;
            var (dataMin, dataMax) = DataRange(values);
            double low = minimum ?? dataMin;
            double high = maximum ?? dataMax;
            if (scale.Diverging && minimum == null && maximum == null)
            {
                double magnitude = Math.Max(Math.Abs(low), Math.Abs(high));
                low = -magnitude;
                high = magnitude;
            }
            if (high <= low)
            {
                high = low + 1;
            }

            grid = new GridOverlay(values, latitudeMin, latitudeMax, longitudeMin, longitudeMax, scale, low, high, smooth);
            gridOpacity = Math.Clamp(opacity, 0f, 1f);
            MarkSceneDirty();
            GridOverlayChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearGridOverlay()
        {
            grid = null;
            MarkSceneDirty();
            GridOverlayChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Draws a satellite's ground track, with a dot that loops along it when <paramref name="showSatellite"/> is set.</summary>
        public void SetGroundTrack(IReadOnlyList<(double Latitude, double Longitude)> points, Color colour, string label = "", bool showSatellite = false)
        {
            groundTrack = new GlobeGroundTrack(points, colour) { Label = label, ShowSatellite = showSatellite };
            UpdateFrameClock();
            Invalidate();
        }

        public void ClearGroundTrack()
        {
            groundTrack = null;
            UpdateFrameClock();
            Invalidate();
        }

        public IReadOnlyList<GlobeSatellite> Satellites => satellites;

        /// <summary>Shows a satellite on its orbit, placed from the real clock.</summary>
        public GlobeSatellite AddSatellite(GlobeSatellite satellite)
        {
            satellites.Add(satellite);
            UpdateFrameClock();
            Invalidate();
            return satellite;
        }

        public void RemoveSatellite(GlobeSatellite satellite)
        {
            satellites.Remove(satellite);
            paintedSatellites.Remove(satellite);
            UpdateFrameClock();
            Invalidate();
        }

        public void ClearSatellites()
        {
            satellites.Clear();
            paintedSatellites.Clear();
            UpdateFrameClock();
            Invalidate();
        }

        /// <summary>Removes markers, regions, the grid overlay, the ground track, satellites and the selection.</summary>
        public void ClearOverlays()
        {
            markers.Clear();
            hoverMarker = null;
            SelectMarker(null);
            regions.Clear();
            grid = null;
            groundTrack = null;
            satellites.Clear();
            paintedSatellites.Clear();
            selection = null;
            selectionPreview = null;
            UpdateFrameClock();
            MarkSceneDirty();
            GridOverlayChanged?.Invoke(this, EventArgs.Empty);
        }

        // ----- Lifecycle -----

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!DesignMode && textures == null && textureError == null)
            {
                _ = LoadTexturesAsync();
            }
            UpdateFrameClock();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateFrameClock();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // The nav rail animates its width, so resizes come in bursts: render them at the
            // interactive scale and refine once they stop.
            sceneDirty = true;
            resizing = true;
            lastResizeSeconds = Now;
            UpdateFrameClock();
        }

        private async Task LoadTexturesAsync()
        {
            try
            {
                textures = await GlobeTextures.LoadAsync();
            }
            catch (Exception ex)
            {
                textureError = ex.Message;
            }
            if (!IsDisposed)
            {
                MarkSceneDirty();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                frameClock.Dispose();
                renderer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ----- Animation clock -----

        private double Now => clock.Elapsed.TotalSeconds;

        private bool NeedsFrames =>
            autoRotate || groundTrack?.ShowSatellite == true || satellites.Count > 0 || flight != null || easing || inertia || resizing;

        private bool LiveSun => useRealSunPosition && sunTimeUtc == null;

        private void UpdateFrameClock()
        {
            bool frames = NeedsFrames;
            bool wanted = IsHandleCreated && Visible && !DesignMode && (frames || LiveSun);
            int interval = frames ? 16 : (int)(SunRefreshSeconds * 1000);

            if (!wanted)
            {
                if (frameClock.Enabled) frameClock.Stop();
                return;
            }
            if (frameClock.Interval != interval)
            {
                frameClock.Interval = interval;
            }
            if (!frameClock.Enabled)
            {
                lastTickSeconds = Now;
                frameClock.Start();
            }
        }

        private void FrameClock_Tick(object? sender, EventArgs e)
        {
            double now = Now;
            double dt = Math.Clamp(now - lastTickSeconds, 0, 0.1);
            lastTickSeconds = now;
            bool viewMoved = false;
            bool render = false;

            if (flight != null)
            {
                viewMoved |= StepFlight(now);
            }
            else if (easing)
            {
                viewMoved |= StepEasing(dt);
            }
            if (inertia)
            {
                viewMoved |= StepInertia(dt);
            }
            if (AutoRotating)
            {
                view.Longitude = Wrap(view.Longitude + autoRotateSpeed * (float)dt);
                target.Longitude = view.Longitude;
                // A slow spin moves the picture a fraction of a pixel per tick. Re-render only
                // once it has moved far enough to see; the rotation itself keeps exact time.
                viewMoved = RotationShiftPixels() >= MinimumVisibleShift;
            }
            if (resizing && now - lastResizeSeconds > ResizeSettleSeconds)
            {
                resizing = false;
                render = true;
            }
            if (LiveSun && now - lastSunRefreshSeconds > SunRefreshSeconds)
            {
                lastSunRefreshSeconds = now;
                render = true;
            }

            if (viewMoved || render)
            {
                sceneDirty = true;
                Invalidate();
                if (viewMoved)
                {
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (groundTrack?.ShowSatellite == true || SatellitesMovedVisibly())
            {
                // The satellite moves over a still planet, so only the GDI+ layer needs redrawing.
                Invalidate();
            }
            else if (SunDotChanged())
            {
                Invalidate(sunDotBounds);
            }
            UpdateFrameClock();
        }

        /// <summary>
        ///  How far, in pixels, the fastest-moving visible point has travelled since the frame
        ///  was rendered, from longitude alone. The point nearest the eye moves fastest, at
        ///  Focal / (eye distance − 1) pixels per radian, so this never underestimates.
        /// </summary>
        private float RotationShiftPixels()
        {
            var camera = CurrentCamera();
            float radians = MathF.Abs(WrapDelta(view.Longitude - renderedLongitude)) * MathF.PI / 180f;
            return radians * camera.Focal / Math.Max(camera.EyeLength - 1f, 1e-3f);
        }

        /// <summary>True once any satellite has moved far enough on screen to be worth a repaint.</summary>
        private bool SatellitesMovedVisibly()
        {
            if (satellites.Count == 0)
            {
                return false;
            }
            var camera = CurrentCamera();
            var utc = DateTime.UtcNow;
            foreach (var satellite in satellites)
            {
                var position = satellite.PositionAt(utc);
                if (!Projects(camera, position) || !paintedSatellites.TryGetValue(satellite, out var painted))
                {
                    return true;
                }
                camera.Project(position, out var screen, out _);
                float dx = screen.X - painted.X, dy = screen.Y - painted.Y;
                if (dx * dx + dy * dy >= MinimumVisibleShift * MinimumVisibleShift)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when the pulsing live-sunlight dot would draw differently from last time.</summary>
        private bool SunDotChanged() =>
            showHud && LiveSun && !sunDotBounds.IsEmpty && SunDotAlpha() != paintedSunDotAlpha;

        private int SunDotAlpha() => LiveSun ? (int)(255 * (0.6f + 0.4f * MathF.Sin((float)(Now * 2.5)))) : 128;

        private bool AutoRotating =>
            autoRotate && frameClock.Enabled && drag == DragMode.None && flight == null && !easing && !inertia
            && Now - lastInteractionSeconds > AutoRotatePauseSeconds;

        private bool InMotion =>
            drag is DragMode.Grab or DragMode.Orbit || flight != null || easing || inertia || resizing || AutoRotating;

        private bool StepFlight(double now)
        {
            var f = flight!;
            double p = (now - f.Start) / f.Duration;
            bool done = p >= 1;
            p = Math.Clamp(p, 0, 1);
            float s = (float)(f.EaseIn ? p * p * p * (p * (p * 6 - 15) + 10) : 1 - Math.Pow(1 - p, 3));

            var (lat, lon) = GlobeCamera.ToLatLon(Slerp(f.FromUnit, f.ToUnit, s));
            view.Latitude = ClampLatitude((float)lat);
            view.Longitude = (float)lon;
            view.ZoomLog = f.From.ZoomLog + (f.To.ZoomLog - f.From.ZoomLog) * s - f.Hop * MathF.Sin(MathF.PI * s);
            view.Heading = f.From.Heading + f.HeadingDelta * s;
            view.Tilt = f.From.Tilt + (f.To.Tilt - f.From.Tilt) * s;

            if (done)
            {
                view = f.To;
                flight = null;
                target = view;
                FlyToComplete?.Invoke(this, new FlightEventArgs(view.Latitude, Wrap(view.Longitude), Math.Pow(2, view.ZoomLog)));
            }
            else
            {
                target = view;
            }
            return true;
        }

        private bool StepEasing(double dt)
        {
            float k = 1f - (float)Math.Exp(-EaseRate * dt);
            view.Latitude += (target.Latitude - view.Latitude) * k;
            view.Longitude += (target.Longitude - view.Longitude) * k;
            view.ZoomLog += (target.ZoomLog - view.ZoomLog) * k;
            view.Heading += (target.Heading - view.Heading) * k;
            view.Tilt += (target.Tilt - view.Tilt) * k;

            bool settled = Math.Abs(target.Latitude - view.Latitude) < 0.002f
                && Math.Abs(target.Longitude - view.Longitude) < 0.002f
                && Math.Abs(target.ZoomLog - view.ZoomLog) < 0.0005f
                && Math.Abs(target.Heading - view.Heading) < 0.01f
                && Math.Abs(target.Tilt - view.Tilt) < 0.01f;
            if (settled)
            {
                view = target;
                easing = false;
            }
            return true;
        }

        private bool StepInertia(double dt)
        {
            view.Longitude += inertiaLongitude * (float)dt;
            view.Latitude = ClampLatitude(view.Latitude + inertiaLatitude * (float)dt);
            float decay = (float)Math.Exp(-InertiaDecay * dt);
            inertiaLongitude *= decay;
            inertiaLatitude *= decay;
            if (Math.Abs(inertiaLongitude) < 0.05f && Math.Abs(inertiaLatitude) < 0.05f)
            {
                inertia = false;
            }
            target = view;
            return true;
        }

        private void StartFlight(ViewState to, TimeSpan? duration)
        {
            bool alreadyMoving = flight != null || inertia || easing;
            CancelMotion();
            view.Longitude = Wrap(view.Longitude);
            view.Heading = Wrap(view.Heading);
            to.Latitude = ClampLatitude(to.Latitude);
            to.Longitude = Wrap(to.Longitude);
            to.ZoomLog = ClampZoomLog(to.ZoomLog);
            to.Tilt = Math.Clamp(to.Tilt, 0f, GlobeCamera.MaxTilt);

            var fromUnit = GlobeCamera.ToUnit(view.Latitude, view.Longitude);
            var toUnit = GlobeCamera.ToUnit(to.Latitude, to.Longitude);
            double angle = Math.Acos(Math.Clamp(Vector3.Dot(fromUnit, toUnit), -1f, 1f)) * 180 / Math.PI;
            float mid = (view.ZoomLog + to.ZoomLog) / 2f;
            float hop = mid * (float)Math.Clamp(angle / 70, 0, 1);
            double seconds = duration?.TotalSeconds
                ?? Math.Clamp(0.6 + angle / 180 * 1.4 + Math.Abs(to.ZoomLog - view.ZoomLog) * 0.15, 0.5, 2.5);

            flight = new Flight
            {
                From = view,
                To = to,
                FromUnit = fromUnit,
                ToUnit = toUnit,
                Start = Now,
                Duration = Math.Max(0.05, seconds),
                EaseIn = !alreadyMoving,
                Hop = hop,
                HeadingDelta = WrapDelta(to.Heading - view.Heading)
            };
            NoteInteraction();
            UpdateFrameClock();
        }

        /// <summary>Stops every animation where it is. A cancelled flight doesn't raise FlyToComplete.</summary>
        private void CancelMotion()
        {
            flight = null;
            inertia = false;
            easing = false;
            target = view;
        }

        private void CancelFlightAndInertia()
        {
            if (flight != null)
            {
                flight = null;
                target = view;
            }
            inertia = false;
        }

        /// <summary>Spherical interpolation, routed over an intermediate point for antipodes.</summary>
        private static Vector3 Slerp(Vector3 a, Vector3 b, float s)
        {
            float dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
            float omega = MathF.Acos(dot);
            if (omega < 1e-4f)
            {
                return a;
            }
            if (omega > MathF.PI - 1e-3f)
            {
                var side = Vector3.Cross(a, Vector3.UnitY);
                if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(a, Vector3.UnitX);
                var mid = Vector3.Normalize(side);
                return s < 0.5f ? Slerp(a, mid, s * 2f) : Slerp(mid, b, s * 2f - 1f);
            }
            float sinOmega = MathF.Sin(omega);
            return (MathF.Sin((1f - s) * omega) / sinOmega) * a + (MathF.Sin(s * omega) / sinOmega) * b;
        }

        /// <summary>Largest zoom at which a box of these half-extents fits the view with a margin.</summary>
        private double FitZoom(double latitudeMin, double latitudeMax, double longitudeSpan, double centreLatitude)
        {
            float width = Width > 0 ? Width : 800;
            float height = Height > 0 ? Height : 600;
            double halfLat = (latitudeMax - latitudeMin) / 2 * 1.15 + 0.5;
            double halfLon = longitudeSpan / 2 * Math.Cos(centreLatitude * Math.PI / 180) * 1.15 + 0.5;

            for (float log = ClampZoomLog(float.MaxValue); log >= 0f; log -= 0.05f)
            {
                var camera = new GlobeCamera(centreLatitude, 0, Math.Pow(2, log), 0, 0, width, height);
                float degreesPerPixel = camera.SurfaceDegreesPerPixel;
                if (height / 2f * degreesPerPixel >= halfLat && width / 2f * degreesPerPixel >= halfLon)
                {
                    return Math.Pow(2, log);
                }
            }
            return 1;
        }

        private void ViewChangedByInput()
        {
            sceneDirty = true;
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private void MarkSceneDirty()
        {
            sceneDirty = true;
            Invalidate();
        }

        private void NoteInteraction()
        {
            lastInteractionSeconds = Now;
        }

        private GlobeCamera CurrentCamera() => new(view.Latitude, view.Longitude, Math.Pow(2, view.ZoomLog), view.Heading, view.Tilt, Width, Height);

        private Vector3 SunDirection()
        {
            var (lat, lon) = useRealSunPosition
                ? SunPosition.SubsolarPoint(sunTimeUtc ?? DateTime.UtcNow)
                : (sunLatitude, sunLongitude);
            return GlobeCamera.ToUnit(lat, lon);
        }

        // ----- Painting -----

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The frame covers the whole control, so there's nothing to clear.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            bool moving = InMotion;
            if (moving && !wasMoving)
            {
                // Pick the interactive resolution once per motion so it doesn't flicker between steps.
                motionScale = InteractiveScale();
                motionFrames = 0;
                motionMilliseconds = 0;
            }
            wasMoving = moving;
            float scale = moving ? motionScale : 1f;

            var frame = renderer.Bitmap;
            if (sceneDirty || frame == null || scale != renderedScale)
            {
                RenderFrame(scale);
                frame = renderer.Bitmap;
                if (moving)
                {
                    motionFrames++;
                    motionMilliseconds += renderer.LastFrameMilliseconds;
                    // A flight that zooms in gets dearer as it goes, so step the resolution down
                    // mid-motion when a frame overruns. Only ever down within an episode, so it can't flicker.
                    if (renderer.LastFrameMilliseconds > FrameBudgetMilliseconds * 1.3 && motionScale > 0.45f)
                    {
                        motionScale = NextScaleDown(motionScale);
                    }
                }
            }

            if (frame != null)
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                if (frame.Width == Width && frame.Height == Height)
                {
                    g.DrawImageUnscaled(frame, 0, 0);
                }
                else
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using var attributes = new ImageAttributes();
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(frame, new Rectangle(0, 0, Width, Height), 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attributes);
                }
                g.CompositingMode = CompositingMode.SourceOver;
            }

            Draw.Smooth(g);
            labelBoxes.Clear();
            var camera = CurrentCamera();
            if (showGraticule)
            {
                DrawGraticule(g, camera);
            }
            DrawRegions(g, camera);
            DrawSelection(g, camera);
            DrawGroundTrack(g, camera);
            DrawSatellites(g, camera, e.ClipRectangle.Contains(ClientRectangle));
            DrawMarkers(g, camera);
            if (showHud)
            {
                DrawHud(g, camera);
            }
            DrawCaptions(g);
        }

        private void RenderFrame(float scale)
        {
            int w = Math.Max(1, (int)Math.Round(Width * scale));
            int h = Math.Max(1, (int)Math.Round(Height * scale));

            scene.CentreLatitude = view.Latitude;
            scene.CentreLongitude = view.Longitude;
            renderedLongitude = view.Longitude;
            scene.Zoom = Math.Pow(2, view.ZoomLog);
            scene.Heading = view.Heading;
            scene.Tilt = view.Tilt;
            scene.SunDirection = SunDirection();
            scene.Textures = textures;
            scene.Grid = grid;
            scene.GridOpacity = gridOpacity;
            scene.Regions = regions.Count == 0
                ? Array.Empty<GlobeRenderer.RegionFill>()
                : regions.Select(r => new GlobeRenderer.RegionFill(r)).ToArray();
            scene.Background = Theme.Background;
            scene.FallbackOcean = Theme.SurfaceRaised;
            scene.ShowStars = showStars;

            renderer.Render(scene, w, h);
            sceneDirty = false;
            renderedScale = scale;

            double fullEstimate = renderer.LastFrameMilliseconds / (scale * scale);
            fullFrameMilliseconds = fullFrameMilliseconds * 0.7 + fullEstimate * 0.3;
        }

        /// <summary>
        ///  Resolution to use while the view is moving: full if a full frame fits the budget,
        ///  otherwise stepped down so the frame rate holds. Idle frames are always full size.
        /// </summary>
        private float InteractiveScale()
        {
            if (fullFrameMilliseconds <= FrameBudgetMilliseconds)
            {
                return 1f;
            }
            float ideal = (float)Math.Sqrt(FrameBudgetMilliseconds / fullFrameMilliseconds);
            return ideal >= 0.85f ? 0.85f : ideal >= 0.7f ? 0.7f : ideal >= 0.55f ? 0.55f : 0.45f;
        }

        private static float NextScaleDown(float scale) =>
            scale > 0.85f ? 0.85f : scale > 0.7f ? 0.7f : scale > 0.55f ? 0.55f : 0.45f;

        private void DrawGraticule(Graphics g, GlobeCamera camera)
        {
            double zoom = Zoom;
            int spacing = zoom > 4 ? 10 : zoom > 2 ? 15 : 30;
            using var pen = new Pen(Draw.WithAlpha(Theme.TextSecondary, 60), 1f);
            for (int lat = -90 + spacing; lat < 90; lat += spacing)
            {
                DrawPolyline(g, camera, Enumerable.Range(0, 181).Select(i => ((double)lat, -180.0 + i * 2)), pen);
            }
            for (int lon = -180; lon < 180; lon += spacing)
            {
                DrawPolyline(g, camera, Enumerable.Range(0, 85).Select(i => (-84.0 + i * 2, (double)lon)), pen);
            }
        }

        private void DrawRegions(Graphics g, GlobeCamera camera)
        {
            foreach (var region in regions)
            {
                using var pen = new Pen(Draw.WithAlpha(region.Colour, 230), Px(1.5f)) { LineJoin = LineJoin.Round };
                DrawPolyline(g, camera, BoxOutline(region.LatitudeMin, region.LatitudeMax, region.LongitudeMin, region.LongitudeSpan), pen);

                // Label sits just above the north-west corner, clear of anything inside the box.
                if (region.Label.Length > 0
                    && camera.Project(GlobeCamera.ToUnit(region.LatitudeMax, region.LongitudeMin), out var corner, out float facing)
                    && facing > 0.05f)
                {
                    DrawLabel(g, region.Label, new PointF(corner.X - Px(11), corner.Y - Px(12)), region.Colour, SmoothStep(0.05f, 0.25f, facing));
                }
            }
        }

        private void DrawSelection(Graphics g, GlobeCamera camera)
        {
            var box = selectionPreview ?? selection;
            if (box == null)
            {
                return;
            }

            var outline = BoxOutline(box.LatitudeMin, box.LatitudeMax, box.LongitudeMin, box.LongitudeSpan).ToList();
            var polygon = new List<PointF>();
            bool allVisible = true;
            foreach (var (lat, lon) in outline)
            {
                if (camera.Project(GlobeCamera.ToUnit(lat, lon), out var screen, out float facing) && facing > 0.002f)
                {
                    polygon.Add(screen);
                }
                else
                {
                    allVisible = false;
                }
            }
            if (allVisible && polygon.Count >= 3)
            {
                using var brush = new SolidBrush(Draw.WithAlpha(Theme.Accent, 45));
                g.FillPolygon(brush, polygon.ToArray());
            }

            using var pen = new Pen(Draw.WithAlpha(Theme.Accent, 230), Px(1.5f)) { DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round };
            DrawPolyline(g, camera, outline, pen);
        }

        private static IEnumerable<(double Latitude, double Longitude)> BoxOutline(double latitudeMin, double latitudeMax, double longitudeMin, double span)
        {
            double height = latitudeMax - latitudeMin;
            int lonSteps = Math.Max(2, (int)Math.Ceiling(span));
            int latSteps = Math.Max(2, (int)Math.Ceiling(height));

            for (int i = 0; i <= lonSteps; i++)
            {
                yield return (latitudeMax, longitudeMin + span * i / lonSteps);
            }
            for (int i = 1; i <= latSteps; i++)
            {
                yield return (latitudeMax - height * i / latSteps, longitudeMin + span);
            }
            for (int i = 1; i <= lonSteps; i++)
            {
                yield return (latitudeMin, longitudeMin + span - span * i / lonSteps);
            }
            for (int i = 1; i <= latSteps; i++)
            {
                yield return (latitudeMin + height * i / latSteps, longitudeMin);
            }
        }

        private void DrawGroundTrack(Graphics g, GlobeCamera camera)
        {
            if (groundTrack == null)
            {
                return;
            }

            using (var pen = new Pen(Draw.WithAlpha(groundTrack.Colour, 150), Px(1.5f)) { LineJoin = LineJoin.Round })
            {
                DrawPolyline(g, camera, groundTrack.Points, pen);
            }

            if (!groundTrack.ShowSatellite)
            {
                return;
            }

            var (lat, lon) = groundTrack.At(Now / groundTrack.LoopSeconds);
            var unit = GlobeCamera.ToUnit(lat, lon);
            if (!camera.Project(unit, out _, out float facing) || facing <= 0.01f)
            {
                return;
            }

            // Lift the dot a little off the surface so it reads as something in orbit.
            camera.Project(unit * 1.05f, out var screen, out _);
            float alpha = SmoothStep(0.01f, 0.15f, facing);
            Draw.Circle(g, screen, Px(10f), Draw.WithAlpha(groundTrack.Colour, (int)(45 * alpha)));
            Draw.Circle(g, screen, Px(5f), Draw.WithAlpha(groundTrack.Colour, (int)(140 * alpha)));
            Draw.Circle(g, screen, Px(2.5f), Draw.WithAlpha(Theme.TextPrimary, (int)(255 * alpha)));
            if (groundTrack.Label.Length > 0 && alpha > 0.5f)
            {
                DrawLabel(g, groundTrack.Label, screen, groundTrack.Colour, alpha);
            }
        }

        /// <summary>
        ///  Each satellite's orbit as a ring at its altitude, brightest just behind the
        ///  satellite and fading towards the stretch ahead of it, a dotted line down to the point
        ///  below, and optionally the last half orbit of ground track trailing over the surface.
        /// </summary>
        private void DrawSatellites(Graphics g, GlobeCamera camera, bool wholeControl)
        {
            if (satellites.Count == 0)
            {
                return;
            }

            const int RingSteps = 240;
            const int TrailSteps = 90;
            var utc = DateTime.UtcNow;
            var ring = new Vector3[RingSteps + 1];
            var trail = new Vector3[TrailSteps + 1];
            using var pen = new Pen(Color.White, Px(1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            foreach (var satellite in satellites)
            {
                double phase = satellite.PhaseAt(utc);

                // The ring starts at the satellite and runs backwards round the orbit, so the
                // index doubles as "how long ago the satellite was here".
                for (int i = 0; i <= RingSteps; i++)
                {
                    ring[i] = satellite.PositionAt(utc, phase - 2 * Math.PI * i / RingSteps);
                }
                pen.Width = Px(1.4f);
                DrawPath(g, camera, ring, point => !HiddenByEarth(camera, point), pen, i =>
                    Draw.WithAlpha(satellite.Colour, (int)(30 + 170 * MathF.Pow(1f - (float)i / RingSteps, 1.6f))));

                // Ground track: where the satellite was over the last half orbit, with the Earth
                // turned back to match. Only the motion along the orbit is sped up by TimeScale.
                if (satellite.ShowGroundTrack)
                {
                    double secondsPerRadian = satellite.Period.TotalSeconds / (2 * Math.PI * Math.Max(satellite.TimeScale, 1e-6));
                    for (int i = 0; i <= TrailSteps; i++)
                    {
                        double back = Math.PI * i / TrailSteps;
                        trail[i] = Vector3.Normalize(satellite.PositionAt(utc.AddSeconds(-back * secondsPerRadian), phase - back));
                    }
                    pen.Width = Px(1.2f);
                    DrawPath(g, camera, trail, point => OnNearSide(camera, point), pen, i =>
                        Draw.WithAlpha(satellite.Colour, (int)(80 * (1f - (float)i / TrailSteps))));
                }

                var position = ring[0];
                var nadir = Vector3.Normalize(position);
                if (!Projects(camera, position))
                {
                    continue;
                }
                camera.Project(position, out var screen, out _);
                if (wholeControl)
                {
                    // Only a repaint of the whole control actually moves the satellite on screen.
                    paintedSatellites[satellite] = screen;
                }
                if (HiddenByEarth(camera, position))
                {
                    continue;
                }

                if (OnNearSide(camera, nadir) && camera.Project(nadir, out var ground, out _))
                {
                    using var drop = new Pen(Draw.WithAlpha(satellite.Colour, 120), Px(1f)) { DashStyle = DashStyle.Dot };
                    g.DrawLine(drop, screen, ground);
                    Draw.Circle(g, ground, Px(2.5f), Draw.WithAlpha(satellite.Colour, 170));
                }

                Draw.Circle(g, screen, Px(11f), Draw.WithAlpha(satellite.Colour, 40));
                Draw.Circle(g, screen, Px(6f), Draw.WithAlpha(satellite.Colour, 120));
                Draw.Circle(g, screen, Px(3f), Draw.WithAlpha(Theme.TextPrimary, 255));
                if (satellite.Label.Length > 0)
                {
                    DrawLabel(g, satellite.Label, screen, satellite.Colour, 1f);
                }
            }
        }

        /// <summary>True if the line of sight from the eye to <paramref name="point"/> passes through the planet.</summary>
        private static bool HiddenByEarth(GlobeCamera camera, Vector3 point)
        {
            var eye = camera.Eye;
            var toPoint = point - eye;
            float t = Math.Clamp(-Vector3.Dot(eye, toPoint) / toPoint.LengthSquared(), 0f, 1f);
            return (eye + t * toPoint).LengthSquared() < 0.9999f;
        }

        /// <summary>True for a surface point on the hemisphere facing the camera.</summary>
        private static bool OnNearSide(GlobeCamera camera, Vector3 unit)
        {
            camera.Project(unit, out _, out float facing);
            return facing > 0.002f;
        }

        /// <summary>
        ///  Draws a 3D path in screen space, skipping the parts <paramref name="visible"/> rejects.
        ///  Where visibility changes between two points the crossing is found by bisection, so
        ///  lines run right up to the horizon instead of stopping a segment short. With
        ///  <paramref name="colourAt"/>, each segment takes the colour for its start index.
        /// </summary>
        private static void DrawPath(Graphics g, GlobeCamera camera, IReadOnlyList<Vector3> points, Func<Vector3, bool> visible,
            Pen pen, Func<int, Color>? colourAt = null)
        {
            var run = new List<PointF>();
            var runIndices = new List<int>();
            bool previousVisible = false;
            for (int i = 0; i < points.Count; i++)
            {
                // Project's return value is about the surface; only its screen position is used here.
                var point = points[i];
                bool isVisible = visible(point) && Projects(camera, point);
                if (i > 0 && isVisible != previousVisible)
                {
                    var edge = Crossing(points[i - 1], point, previousVisible, visible, camera);
                    camera.Project(edge, out var edgeScreen, out _);
                    run.Add(Clamp(camera, edgeScreen));
                    runIndices.Add(i - 1);
                    if (!isVisible)
                    {
                        Flush();
                    }
                }
                if (isVisible)
                {
                    camera.Project(point, out var screen, out _);
                    run.Add(Clamp(camera, screen));
                    runIndices.Add(i);
                }
                previousVisible = isVisible;
            }
            Flush();

            void Flush()
            {
                if (run.Count >= 2)
                {
                    if (colourAt == null)
                    {
                        g.DrawLines(pen, run.ToArray());
                    }
                    else
                    {
                        for (int k = 1; k < run.Count; k++)
                        {
                            pen.Color = colourAt(runIndices[k - 1]);
                            g.DrawLine(pen, run[k - 1], run[k]);
                        }
                    }
                }
                run.Clear();
                runIndices.Clear();
            }
        }

        /// <summary>
        ///  Pulls a point that projects far off screen back to a few control-widths away. Points
        ///  just in front of the eye can land millions of pixels out, which GDI+ can't draw.
        /// </summary>
        private static PointF Clamp(GlobeCamera camera, PointF point)
        {
            float reach = 4f * Math.Max(camera.CentreX, camera.CentreY) * 2f;
            return new PointF(Math.Clamp(point.X, camera.CentreX - reach, camera.CentreX + reach),
                Math.Clamp(point.Y, camera.CentreY - reach, camera.CentreY + reach));
        }

        /// <summary>True if the point is in front of the camera, whatever the planet hides.</summary>
        private static bool Projects(GlobeCamera camera, Vector3 point) =>
            Vector3.Dot(point - camera.Eye, camera.Forward) > 1e-4f;

        /// <summary>Bisects between a visible and a hidden point for where the path disappears.</summary>
        private static Vector3 Crossing(Vector3 from, Vector3 to, bool fromVisible, Func<Vector3, bool> visible, GlobeCamera camera)
        {
            float radius = from.Length();
            var shown = fromVisible ? from : to;
            var hidden = fromVisible ? to : from;
            for (int step = 0; step < 12; step++)
            {
                var middle = Vector3.Normalize(Vector3.Lerp(shown, hidden, 0.5f)) * radius;
                if (visible(middle) && Projects(camera, middle))
                {
                    shown = middle;
                }
                else
                {
                    hidden = middle;
                }
            }
            return shown;
        }

        private void DrawMarkers(Graphics g, GlobeCamera camera)
        {
            markerHits.Clear();
            foreach (var marker in markers)
            {
                if (!camera.Project(GlobeCamera.ToUnit(marker.Latitude, marker.Longitude), out var screen, out float facing))
                {
                    continue;
                }

                float alpha = SmoothStep(0f, 0.12f, facing);
                if (alpha <= 0.02f)
                {
                    continue;
                }

                bool selected = marker == selectedMarker;
                float k = (0.7f + 0.3f * alpha) * (selected ? 1.25f : 1f);
                var colour = marker.Colour;
                Draw.Circle(g, screen, Px(12f) * k, Draw.WithAlpha(colour, (int)(40 * alpha)));
                Draw.Circle(g, screen, Px(7f) * k, Draw.WithAlpha(colour, (int)(110 * alpha)));
                Draw.Circle(g, screen, Px(3.8f) * k, Draw.WithAlpha(colour, (int)(255 * alpha)));
                Draw.Circle(g, screen, Px(1.6f) * k, Draw.WithAlpha(Theme.TextPrimary, (int)(230 * alpha)));

                if (selected || marker == hoverMarker)
                {
                    using var ring = new Pen(Draw.WithAlpha(selected ? Theme.TextPrimary : colour, (int)(200 * alpha)), Px(1.5f));
                    float radius = Px(10f) * (selected ? 1.2f : 1f);
                    g.DrawEllipse(ring, screen.X - radius, screen.Y - radius, radius * 2f, radius * 2f);
                }

                markerHits.Add((marker, screen, facing));
                if (marker.Label.Length > 0 && alpha > 0.4f)
                {
                    DrawLabel(g, marker.Label, screen, colour, alpha);
                }
            }

            if (hoverMarker != null)
            {
                foreach (var (marker, screen, _) in markerHits)
                {
                    if (marker == hoverMarker)
                    {
                        DrawTooltip(g, marker, screen);
                        break;
                    }
                }
            }
        }

        /// <summary>
        ///  Caption text on a translucent pill beside an anchor point: to the right if there's
        ///  room, otherwise to the left, and not at all if it would sit on another label.
        /// </summary>
        private void DrawLabel(Graphics g, string text, PointF anchor, Color accent, float alpha)
        {
            var size = Draw.Measure(g, text, Theme.Caption);
            float width = size.Width + Px(12);
            float height = size.Height + Px(4);
            float top = anchor.Y - height / 2f;
            var box = new RectangleF(anchor.X + Px(11), top, width, height);
            if (Collides(box))
            {
                box = new RectangleF(anchor.X - Px(11) - width, top, width, height);
                if (Collides(box))
                {
                    return;
                }
            }
            labelBoxes.Add(box);
            Draw.RoundedBox(g, box, Px(4f), Draw.WithAlpha(Theme.Background, (int)(170 * alpha)));

            using (var pen = new Pen(Draw.WithAlpha(accent, (int)(140 * alpha)), Px(2f)))
            {
                g.DrawLine(pen, box.Left + Px(1.5f), box.Top + Px(3), box.Left + Px(1.5f), box.Bottom - Px(3));
            }

            var textBounds = new Rectangle((int)box.X + Px(6), (int)box.Y, size.Width + Px(4), (int)box.Height);
            Draw.Text(g, text, Theme.Caption, textBounds, Draw.Blend(Theme.TextMuted, Theme.TextPrimary, alpha));
        }

        private bool Collides(RectangleF box)
        {
            foreach (var other in labelBoxes)
            {
                if (other.IntersectsWith(box))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hover box in the same style as the chart's read-out: label, detail and coordinates.</summary>
        private void DrawTooltip(Graphics g, GlobeMarker marker, PointF screen)
        {
            string title = marker.Label.Length > 0 ? marker.Label : "Marker";
            string detail = marker.Detail;
            string where = FormatCoordinates(marker.Latitude, marker.Longitude);

            var titleSize = Draw.Measure(g, title, Theme.BodyStrong);
            var detailSize = detail.Length > 0 ? Draw.Measure(g, detail, Theme.Caption) : Size.Empty;
            var whereSize = Draw.Measure(g, where, Theme.NumericSmall);

            int pad = Px(Theme.Space3);
            int gap = Px(3);
            int width = Math.Max(titleSize.Width, Math.Max(detailSize.Width, whereSize.Width)) + pad * 2;
            int height = pad + titleSize.Height + (detail.Length > 0 ? gap + detailSize.Height : 0) + gap + whereSize.Height + pad;

            float left = screen.X + Px(18);
            if (left + width > Width - Px(4))
            {
                left = screen.X - Px(18) - width;
            }
            float top = Math.Clamp(screen.Y - height / 2f, Px(4), Math.Max(Px(4), Height - height - Px(4)));

            var box = new Rectangle((int)left, (int)top, width, height);
            Draw.RoundedBox(g, box, Px(Theme.RadiusSmall), Theme.SurfaceRaised, Theme.Border);

            int y = box.Y + pad;
            Draw.Text(g, title, Theme.BodyStrong, new Rectangle(box.X + pad, y, titleSize.Width + 1, titleSize.Height), Theme.TextPrimary);
            y += titleSize.Height + gap;
            if (detail.Length > 0)
            {
                Draw.Text(g, detail, Theme.Caption, new Rectangle(box.X + pad, y, detailSize.Width + 1, detailSize.Height), Theme.TextSecondary);
                y += detailSize.Height + gap;
            }
            Draw.Text(g, where, Theme.NumericSmall, new Rectangle(box.X + pad, y, whereSize.Width + 1, whereSize.Height), Theme.TextMuted);
        }

        /// <summary>Compass and zoom buttons bottom-right, cursor read-out bottom-left.</summary>
        private void DrawHud(Graphics g, GlobeCamera camera)
        {
            hudButtons.Clear();
            int edge = Px(Theme.Space4);
            int size = Px(32);
            int gap = Px(Theme.Space2);
            int x = Width - edge - size;
            int y = Height - edge - size;

            DrawHudButton(g, HudButton.Home, new Rectangle(x, y, size, size), Icons.Home);
            y -= size + gap;
            DrawHudButton(g, HudButton.ZoomOut, new Rectangle(x, y, size, size), Icons.ZoomOut);
            y -= size + gap;
            DrawHudButton(g, HudButton.ZoomIn, new Rectangle(x, y, size, size), Icons.ZoomIn);
            y -= gap + Px(Theme.Space1);

            int compassSize = Px(44);
            DrawCompass(g, new Rectangle(x + size / 2 - compassSize / 2, y - compassSize, compassSize, compassSize));
            DrawReadout(g, camera);
        }

        private void DrawHudButton(Graphics g, HudButton button, Rectangle bounds, string glyph)
        {
            hudButtons[button] = bounds;
            bool hovered = hudHover == button;
            Draw.RoundedBox(g, bounds, Px(Theme.RadiusSmall),
                Draw.WithAlpha(hovered ? Theme.SurfaceHover : Theme.Surface, 235), Draw.WithAlpha(Theme.Border, 200));
            Draw.Text(g, glyph, Theme.Icon(11f), bounds, hovered ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private void DrawCompass(Graphics g, Rectangle bounds)
        {
            hudButtons[HudButton.Compass] = bounds;
            bool hovered = hudHover == HudButton.Compass;
            var centre = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
            float radius = bounds.Width / 2f;
            Draw.Circle(g, centre, radius, Draw.WithAlpha(hovered ? Theme.SurfaceHover : Theme.Surface, 235));
            using (var ring = new Pen(Draw.WithAlpha(Theme.Border, 200), 1f))
            {
                g.DrawEllipse(ring, centre.X - radius + 0.5f, centre.Y - radius + 0.5f, radius * 2f - 1f, radius * 2f - 1f);
            }

            var state = g.Save();
            g.TranslateTransform(centre.X, centre.Y);
            g.RotateTransform(-view.Heading);

            using (var tick = new Pen(Draw.WithAlpha(Theme.TextMuted, 160), 1f))
            {
                for (int i = 0; i < 4; i++)
                {
                    g.DrawLine(tick, 0f, -radius + Px(2), 0f, -radius + Px(5));
                    g.RotateTransform(90f);
                }
            }

            float tip = radius - Px(9);
            float wing = Px(3.5f);
            using (var north = new SolidBrush(Theme.Accent))
            {
                g.FillPolygon(north, new[] { new PointF(0f, -tip), new PointF(-wing, Px(2)), new PointF(wing, Px(2)) });
            }
            using (var south = new SolidBrush(Draw.WithAlpha(Theme.TextMuted, 200)))
            {
                g.FillPolygon(south, new[] { new PointF(0f, tip), new PointF(-wing, -Px(2)), new PointF(wing, -Px(2)) });
            }
            using (var brush = new SolidBrush(Theme.TextPrimary))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("N", Theme.CaptionStrong, brush, new PointF(0f, -radius + Px(11)), format);
            }
            g.Restore(state);
        }

        private void DrawReadout(Graphics g, GlobeCamera camera)
        {
            string coordinates = cursorPosition is (double lat, double lon)
                ? FormatCoordinates(lat, lon)
                : "Move over the globe for coordinates";
            string detail = string.Format(British, "Eye alt {0:N0} km · Zoom {1:0.0}×", camera.AltitudeKm, Zoom);
            if (Math.Abs(Heading) > 0.5 || view.Tilt > 0.5f)
            {
                double heading = Heading < 0 ? Heading + 360 : Heading;
                detail += string.Format(British, " · Heading {0:000}° · Tilt {1:0}°", heading, view.Tilt);
            }

            var coordinateFont = cursorPosition != null ? Theme.Numeric : Theme.Body;
            var coordinateSize = Draw.Measure(g, coordinates, coordinateFont);
            var detailSize = Draw.Measure(g, detail, Theme.Caption);
            string sunlight = SunlightCaption();
            var sunlightSize = Draw.Measure(g, sunlight, Theme.Caption);
            int pad = Px(Theme.Space3);
            int width = Math.Max(Math.Max(coordinateSize.Width, detailSize.Width), sunlightSize.Width + Px(Theme.Space3)) + pad * 2;
            int height = Px(Theme.Space2) + coordinateSize.Height + Px(Theme.Space1) + detailSize.Height + Px(Theme.Space1) + sunlightSize.Height + Px(Theme.Space2);
            int edge = Px(Theme.Space4);
            readoutBounds = new Rectangle(edge, Height - edge - height, width, height);

            Draw.RoundedBox(g, readoutBounds, Px(Theme.RadiusSmall), Draw.WithAlpha(Theme.Surface, 225), Draw.WithAlpha(Theme.Border, 200));
            int y = readoutBounds.Y + Px(Theme.Space2);
            Draw.Text(g, coordinates, coordinateFont, new Rectangle(readoutBounds.X + pad, y, coordinateSize.Width + 1, coordinateSize.Height),
                cursorPosition != null ? Theme.TextPrimary : Theme.TextMuted);
            y += coordinateSize.Height + Px(Theme.Space1);
            Draw.Text(g, detail, Theme.Caption, new Rectangle(readoutBounds.X + pad, y, detailSize.Width + 1, detailSize.Height), Theme.TextSecondary);
            y += detailSize.Height + Px(Theme.Space1);

            // A small sun-coloured dot, pulsing gently while the lighting follows the live clock.
            var dotCentre = new PointF(readoutBounds.X + pad + Px(3f), y + sunlightSize.Height / 2f);
            sunDotBounds = Rectangle.Inflate(new Rectangle((int)dotCentre.X, (int)dotCentre.Y, 0, 0), Px(5), Px(5));
            paintedSunDotAlpha = SunDotAlpha();
            Draw.Circle(g, dotCentre, Px(3f), Draw.WithAlpha(LiveSun ? Theme.Warning : Theme.TextMuted, paintedSunDotAlpha));
            Draw.Text(g, sunlight, Theme.Caption, new Rectangle(readoutBounds.X + pad + Px(Theme.Space3), y, sunlightSize.Width + 1, sunlightSize.Height), Theme.TextMuted);
        }

        /// <summary>Says what the lighting shows: the live sun, a frozen instant, or a fixed position.</summary>
        private string SunlightCaption()
        {
            if (!useRealSunPosition)
            {
                return "Sunlight fixed";
            }
            return sunTimeUtc is DateTime frozen
                ? "Sunlight at " + frozen.ToUniversalTime().ToString("d MMM yyyy HH:mm", British) + " UTC"
                : "Live sunlight · " + DateTime.UtcNow.ToString("HH:mm", British) + " UTC";
        }

        private void DrawCaptions(Graphics g)
        {
            int bottom = Height - Px(22);
            if (showAttribution)
            {
                Draw.Text(g, "Imagery: NASA Blue Marble · NASA Black Marble", Theme.Caption,
                    new Rectangle(0, bottom, Width, Px(18)), Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                bottom -= Px(20);
            }

            if (textures == null)
            {
                string status = textureError == null ? "Loading NASA imagery…" : "Imagery unavailable: " + textureError;
                Draw.Text(g, status, Theme.Caption, new Rectangle(0, bottom, Width, Px(18)), Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (showFrameRate)
            {
                string timing = string.Format(British, "{0:0.0} ms at {1:0.00}x · full frame ≈ {2:0.0} ms",
                    renderer.LastFrameMilliseconds, renderedScale, fullFrameMilliseconds);
                if (motionFrames > 0)
                {
                    double average = motionMilliseconds / motionFrames;
                    timing += string.Format(British, " · moving: {0:0.0} ms avg over {1} frames", average, motionFrames);
                }
                Draw.Text(g, timing, Theme.NumericSmall, new Rectangle(0, Px(8), Width, Px(18)), Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>Draws a lat/lon path as one or more screen polylines, breaking it at the horizon.</summary>
        private static void DrawPolyline(Graphics g, GlobeCamera camera, IEnumerable<(double Latitude, double Longitude)> points, Pen pen)
        {
            var units = points.Select(p => GlobeCamera.ToUnit(p.Latitude, p.Longitude)).ToArray();
            DrawPath(g, camera, units, unit => OnNearSide(camera, unit), pen);
        }

        private static string FormatCoordinates(double latitudeDegrees, double longitudeDegrees)
        {
            string lat = Math.Abs(latitudeDegrees).ToString("0.00", British) + "° " + (latitudeDegrees >= 0 ? "N" : "S");
            double lon = Wrap((float)longitudeDegrees);
            string lonText = Math.Abs(lon).ToString("0.00", British) + "° " + (lon >= 0 ? "E" : "W");
            return lat + ", " + lonText;
        }

        // ----- Input -----

        protected override void WndProc(ref Message m)
        {
            // Modifier flags travel with the button message, which also lets synthetic clicks
            // carry Shift or Ctrl without the physical keys being held.
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN)
            {
                int keys = (int)(m.WParam.ToInt64() & 0xFFFF);
                shiftHeld = (keys & MK_SHIFT) != 0 || (ModifierKeys & Keys.Shift) != 0;
                controlHeld = (keys & MK_CONTROL) != 0 || (ModifierKeys & Keys.Control) != 0;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // Wheel messages go to the focused control, so take focus while the window is active,
            // unless someone is typing: the cursor drifting over the globe mustn't steal their keys.
            var form = FindForm();
            if (form == null || Form.ActiveForm != form || Focused)
            {
                return;
            }
            Control? active = form.ActiveControl;
            while (active is ContainerControl container && container.ActiveControl != null)
            {
                active = container.ActiveControl;
            }
            if (active is not TextBoxBase)
            {
                Focus();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            bool left = e.Button == MouseButtons.Left;
            bool right = e.Button == MouseButtons.Right;
            if (!left && !right)
            {
                return;
            }

            if (left && showHud)
            {
                var hit = HudHitTest(e.Location);
                if (hit != HudButton.None)
                {
                    HudAction(hit);
                    return;
                }
            }

            var camera = CurrentCamera();
            if (left && shiftHeld)
            {
                if (!camera.Unproject(e.X, e.Y, out double lat, out double lon))
                {
                    return;
                }
                drag = DragMode.Select;
                selectAnchor = (lat, lon);
                selectionPreview = null;
            }
            else if (right || controlHeld)
            {
                drag = DragMode.Orbit;
            }
            else
            {
                drag = DragMode.Grab;
                grabPoint = camera.Unproject(e.X, e.Y, out double lat, out double lon) ? (lat, lon) : null;
            }

            dragStart = lastMouse = e.Location;
            lastMoveSeconds = Now;
            velocityLatitude = velocityLongitude = 0f;
            CancelMotion();
            NoteInteraction();
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            double now = Now;
            double dt = Math.Max(now - lastMoveSeconds, 0.001);
            var camera = CurrentCamera();
            bool onSurface = camera.Unproject(e.X, e.Y, out double lat, out double lon);
            cursorPosition = onSurface ? (lat, lon) : null;

            switch (drag)
            {
                case DragMode.Grab:
                {
                    float dLat, dLon;
                    if (onSurface && grabPoint is (double grabLat, double grabLon))
                    {
                        // Keep the grabbed point under the cursor: shift the centre by however far
                        // the point now under the cursor is from it. Converges within a move or two.
                        dLat = (float)Math.Clamp(grabLat - lat, -30, 30);
                        dLon = Math.Clamp(WrapDelta((float)(grabLon - lon)), -30f, 30f);
                    }
                    else
                    {
                        float degreesPerPixel = camera.SurfaceDegreesPerPixel;
                        dLon = -(e.X - lastMouse.X) * degreesPerPixel;
                        dLat = (e.Y - lastMouse.Y) * degreesPerPixel;
                    }
                    view.Latitude = ClampLatitude(view.Latitude + dLat);
                    view.Longitude += dLon;
                    target = view;
                    velocityLongitude = velocityLongitude * 0.5f + (float)(dLon / dt) * 0.5f;
                    velocityLatitude = velocityLatitude * 0.5f + (float)(dLat / dt) * 0.5f;
                    ViewChangedByInput();
                    break;
                }
                case DragMode.Orbit:
                    view.Heading = Wrap(view.Heading + (e.X - lastMouse.X) * 0.35f);
                    view.Tilt = Math.Clamp(view.Tilt - (e.Y - lastMouse.Y) * 0.3f, 0f, GlobeCamera.MaxTilt);
                    target = view;
                    ViewChangedByInput();
                    break;
                case DragMode.Select:
                    if (onSurface && selectAnchor is (double anchorLat, double anchorLon))
                    {
                        selectionPreview = BoxBetween(anchorLat, anchorLon, lat, lon);
                        Invalidate();
                    }
                    break;
                default:
                {
                    var marker = HitMarker(e.Location);
                    var hud = showHud ? HudHitTest(e.Location) : HudButton.None;
                    if (marker != hoverMarker || hud != hudHover)
                    {
                        hoverMarker = marker;
                        hudHover = hud;
                        Cursor = marker != null || hud != HudButton.None ? Cursors.Hand : Cursors.Default;
                        Invalidate();
                    }
                    else if (showHud)
                    {
                        Invalidate(readoutBounds);
                    }
                    break;
                }
            }

            if (drag != DragMode.None)
            {
                lastMouse = e.Location;
                lastMoveSeconds = now;
                NoteInteraction();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag == DragMode.None)
            {
                return;
            }

            var mode = drag;
            drag = DragMode.None;
            Capture = false;
            double now = Now;
            int moved = Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y);

            switch (mode)
            {
                case DragMode.Grab when moved < Px(4):
                {
                    var marker = HitMarker(e.Location);
                    if (marker != null)
                    {
                        MarkerClicked?.Invoke(this, new MarkerEventArgs(marker));
                        SelectMarker(marker, flyToMarkerOnClick);
                    }
                    else if (CurrentCamera().Unproject(e.X, e.Y, out double lat, out double lon))
                    {
                        SurfaceClicked?.Invoke(this, new SurfaceClickEventArgs(lat, lon));
                    }
                    break;
                }
                case DragMode.Grab:
                    // Fling: carry on at the release velocity and let it decay.
                    if (now - lastMoveSeconds < 0.12 && (Math.Abs(velocityLongitude) > 1f || Math.Abs(velocityLatitude) > 1f))
                    {
                        inertia = true;
                        inertiaLongitude = Math.Clamp(velocityLongitude, -400f, 400f);
                        inertiaLatitude = Math.Clamp(velocityLatitude, -400f, 400f);
                    }
                    break;
                case DragMode.Select:
                    if (selectionPreview != null && selectionPreview.LatitudeMax - selectionPreview.LatitudeMin > 0.3
                        && selectionPreview.LongitudeSpan > 0.3 && selectionPreview.LongitudeSpan < 359)
                    {
                        selection = selectionPreview;
                        RegionSelected?.Invoke(this, new RegionEventArgs(selection));
                    }
                    else
                    {
                        selection = null;
                    }
                    selectionPreview = null;
                    break;
            }

            NoteInteraction();
            UpdateFrameClock();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            cursorPosition = null;
            if (hoverMarker != null || hudHover != HudButton.None)
            {
                hoverMarker = null;
                hudHover = HudButton.None;
                Cursor = Cursors.Default;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
            ZoomBy(e.Delta / 120f * 0.4f, e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HudHitTest(e.Location) == HudButton.None
                && CurrentCamera().Unproject(e.X, e.Y, out double lat, out double lon))
            {
                FlyTo(lat, lon, Math.Min(MaxZoom, Zoom * 2.2));
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            float step = 12f / (float)Math.Sqrt(Zoom);
            switch (e.KeyCode)
            {
                case Keys.Left: Nudge(0f, -step); break;
                case Keys.Right: Nudge(0f, step); break;
                case Keys.Up: Nudge(step, 0f); break;
                case Keys.Down: Nudge(-step, 0f); break;
                case Keys.Add or Keys.Oemplus: ZoomIn(); break;
                case Keys.Subtract or Keys.OemMinus: ZoomOut(); break;
                case Keys.N: ResetNorth(); break;
                case Keys.R or Keys.Home: ResetView(); break;
                case Keys.Escape: ClearSelection(); SelectMarker(null); break;
                default: return;
            }
            e.Handled = true;
        }

        private void HudAction(HudButton button)
        {
            switch (button)
            {
                case HudButton.ZoomIn: ZoomIn(); break;
                case HudButton.ZoomOut: ZoomOut(); break;
                case HudButton.Home: ResetView(); break;
                case HudButton.Compass: ResetNorth(); break;
            }
        }

        private HudButton HudHitTest(Point location)
        {
            foreach (var (button, bounds) in hudButtons)
            {
                if (bounds.Contains(location))
                {
                    return button;
                }
            }
            return HudButton.None;
        }

        private void Nudge(float dLat, float dLon)
        {
            CancelFlightAndInertia();
            target.Latitude = ClampLatitude(target.Latitude + dLat);
            target.Longitude += dLon;
            easing = true;
            NoteInteraction();
            UpdateFrameClock();
        }

        /// <summary>
        ///  Zooms by a number of doublings, eased. With an anchor, the view also slides so the
        ///  point under the cursor stays put, as a map does when you scroll over it.
        /// </summary>
        private void ZoomBy(float deltaLog, Point? anchor)
        {
            CancelFlightAndInertia();
            float newLog = ClampZoomLog(target.ZoomLog + deltaLog);
            if (newLog == target.ZoomLog)
            {
                return;
            }

            if (anchor is Point point && CurrentCamera().Unproject(point.X, point.Y, out double lat, out double lon))
            {
                float k = 1f - (float)Math.Pow(2, target.ZoomLog - newLog);
                target.Latitude = ClampLatitude(target.Latitude + ((float)lat - target.Latitude) * k);
                target.Longitude += WrapDelta((float)lon - target.Longitude) * k;
            }

            target.ZoomLog = newLog;
            easing = true;
            NoteInteraction();
            UpdateFrameClock();
        }

        private GlobeMarker? HitMarker(Point location)
        {
            float best = Px(12f);
            GlobeMarker? hit = null;
            foreach (var (marker, screen, facing) in markerHits)
            {
                if (facing <= 0f)
                {
                    continue;
                }
                float distance = MathF.Sqrt((screen.X - location.X) * (screen.X - location.X) + (screen.Y - location.Y) * (screen.Y - location.Y));
                if (distance < best)
                {
                    best = distance;
                    hit = marker;
                }
            }
            return hit;
        }

        /// <summary>The lat/lon box with two opposite corners, going the short way round in longitude.</summary>
        private static GlobeRegion BoxBetween(double lat1, double lon1, double lat2, double lon2)
        {
            float delta = WrapDelta((float)(lon2 - lon1));
            double west = delta >= 0 ? lon1 : lon2;
            double east = delta >= 0 ? lon1 + delta : lon1;
            return new GlobeRegion(Math.Min(lat1, lat2), Math.Max(lat1, lat2), Wrap((float)west), Wrap((float)east), Theme.Accent);
        }

        // ----- Helpers -----

        private static float ClampLatitude(float value) => Math.Clamp(value, -LatitudeLimit, LatitudeLimit);

        private static float ClampZoomLog(float value) => Math.Clamp(value, 0f, (float)Math.Log2(GlobeCamera.MaxZoom));

        /// <summary>Longitude folded into [-180, 180).</summary>
        private static float Wrap(float degrees)
        {
            float wrapped = (degrees + 180f) % 360f;
            if (wrapped < 0f) wrapped += 360f;
            return wrapped - 180f;
        }

        /// <summary>The shorter way round for a change in longitude or heading.</summary>
        private static float WrapDelta(float delta) => Wrap(delta);

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static (double Min, double Max) DataRange(double[,] values)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (double value in values)
            {
                if (double.IsNaN(value)) continue;
                if (value < min) min = value;
                if (value > max) max = value;
            }
            return double.IsInfinity(min) ? (0, 1) : (min, max);
        }
    }
}
