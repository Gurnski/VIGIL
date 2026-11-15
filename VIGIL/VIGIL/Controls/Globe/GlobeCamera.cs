using System.Numerics;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Perspective camera orbiting a target point on the unit sphere, as Google Earth's does:
    ///  it looks at the target from a range set by the zoom, turned by a heading (0 = north up)
    ///  and leaned over by a tilt (0 = looking straight down). Zooming also brings the camera
    ///  closer, so the horizon curves more the further in you go.
    ///  World axes: x through 0° N 0° E, y through the north pole, z through 0° N 90° W
    ///  (a right-handed frame in which east appears on the right when north is up).
    ///
    ///  The surface can be unrolled into a flat map. <see cref="Fold"/> is 0 for the globe and
    ///  1 for the map. Between the two the surface is part of a larger sphere, of radius
    ///  1 / (1 − fold), on which every latitude and longitude is scaled by (1 − fold), so that
    ///  it unrolls from 180° and distances along it stay what they were. The place at 0° N 0° E
    ///  stays where it is, and flat, the map lies in the plane through it with north along y
    ///  and east along −z. The camera orbits its target on that surface in the same way, so
    ///  the same five numbers describe the view whatever the fold. The web viewer's
    ///  <c>stage.ts</c> unrolls its globe by the same rule.
    /// </summary>
    internal readonly struct GlobeCamera
    {
        public const float MaxZoom = 8f;
        public const float MaxTilt = 70f;
        public const float EarthRadiusKm = 6371f;
        private const float FarDistance = 4.2f;   // planet radii from the centre at zoom 1, untilted
        private const float NearDistance = 1.3f;  // planet radii at MaxZoom

        /// <summary>
        ///  How many times as far off the surface things stand on the flat map as on the globe.
        ///  The whole map is seen from further away than the globe is, so a column would
        ///  otherwise all but vanish. The web viewer does the same.
        /// </summary>
        public const float FlatHeightScale = 2f;

        public readonly float CentreX;
        public readonly float CentreY;

        /// <summary>Radius of the planet's silhouette in pixels when untilted.</summary>
        public readonly float Radius;

        /// <summary>Distance from the target point to the eye, in planet radii.</summary>
        public readonly float Range;

        /// <summary>Focal length in pixels.</summary>
        public readonly float Focal;

        /// <summary>Distance from the planet's centre to the eye, in planet radii. Only meaningful for the globe.</summary>
        public readonly float EyeLength;

        public readonly Vector3 Target;
        public readonly Vector3 Eye;
        public readonly Vector3 Right;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;

        /// <summary>0 for the globe, 1 for the flat map, between the two while it unrolls.</summary>
        public readonly float Fold;

        /// <summary>1 − fold: the curvature left in the surface. 1 for the globe, 0 for the flat map.</summary>
        public readonly float Curve;

        public GlobeCamera(double latitude, double longitude, double zoom, double headingDegrees, double tiltDegrees, float width, float height, float fold = 0f)
        {
            CentreX = width / 2f;
            CentreY = height / 2f;
            Fold = Math.Clamp(fold, 0f, 1f);
            Curve = 1f - Fold;

            float fit = MathF.Min(width, height) * 0.5f * 0.84f;
            Radius = fit * (float)zoom;
            float fraction = Math.Clamp((float)(Math.Log(zoom) / Math.Log(MaxZoom)), 0f, 1f);
            float distance = FarDistance + (NearDistance - FarDistance) * fraction;
            Range = distance - 1f;
            Focal = Radius * MathF.Sqrt(distance * distance - 1f);

            Vector3 outward, localNorth;
            if (Fold <= 0f)
            {
                Target = ToUnit(latitude, longitude);
                outward = Target;
                var north = Vector3.UnitY;
                localNorth = Vector3.Normalize(north - Vector3.Dot(north, Target) * Target);
            }
            else
            {
                // The same frame on the unrolled surface: straight out of it, and north along it.
                Target = PlaceOnFold(latitude, longitude, 0f, Fold);
                double a = latitude * Math.PI / 180 * Curve;
                double b = WrapLongitude(longitude) * Math.PI / 180 * Curve;
                outward = new Vector3((float)(Math.Cos(a) * Math.Cos(b)), (float)Math.Sin(a), (float)(-Math.Cos(a) * Math.Sin(b)));
                localNorth = new Vector3((float)(-Math.Sin(a) * Math.Cos(b)), (float)Math.Cos(a), (float)(Math.Sin(a) * Math.Sin(b)));
            }
            var localEast = Vector3.Cross(localNorth, outward);

            float heading = (float)(headingDegrees * Math.PI / 180);
            float tilt = (float)(Math.Clamp(tiltDegrees, 0, MaxTilt) * Math.PI / 180);
            var ahead = MathF.Cos(heading) * localNorth + MathF.Sin(heading) * localEast;
            var lift = MathF.Cos(tilt) * outward - MathF.Sin(tilt) * ahead;   // from the target towards the eye

            Eye = Target + Range * lift;
            Forward = -lift;
            Up = MathF.Cos(tilt) * ahead + MathF.Sin(tilt) * outward;
            Right = Vector3.Cross(Forward, Up);
            EyeLength = Eye.Length();
        }

        /// <summary>Degrees of surface under one pixel at the target point.</summary>
        public float SurfaceDegreesPerPixel => Range / Focal * (180f / MathF.PI);

        /// <summary>Height of the eye above the target, in kilometres.</summary>
        public float AltitudeKm => Range * EarthRadiusKm;

        /// <summary>Unit vector in world space for a latitude and longitude in degrees.</summary>
        public static Vector3 ToUnit(double latitudeDegrees, double longitudeDegrees)
        {
            double lat = latitudeDegrees * Math.PI / 180;
            double lon = longitudeDegrees * Math.PI / 180;
            double cosLat = Math.Cos(lat);
            return new Vector3((float)(cosLat * Math.Cos(lon)), (float)Math.Sin(lat), (float)(-cosLat * Math.Sin(lon)));
        }

        /// <summary>Latitude and longitude in degrees of a unit vector.</summary>
        public static (double Latitude, double Longitude) ToLatLon(Vector3 unit)
        {
            double lat = Math.Asin(Math.Clamp(unit.Y, -1f, 1f)) * 180 / Math.PI;
            double lon = Math.Atan2(-unit.Z, unit.X) * 180 / Math.PI;
            return (lat, lon);
        }

        /// <summary>A longitude brought into 180° W to 180° E, which is all an unrolled surface has.</summary>
        public static double WrapLongitude(double degrees) =>
            degrees > 180 || degrees < -180 ? ((degrees + 180) % 360 + 360) % 360 - 180 : degrees;

        /// <summary>
        ///  Where a place is in world space on the surface as it is folded: a latitude and
        ///  longitude in degrees and a height above the surface in planet radii. On the globe it
        ///  is the unit vector scaled out by the height.
        /// </summary>
        public static Vector3 PlaceOnFold(double latitudeDegrees, double longitudeDegrees, float height, float fold)
        {
            if (fold <= 0f)
            {
                return ToUnit(latitudeDegrees, longitudeDegrees) * (1f + height);
            }
            double lat = latitudeDegrees * Math.PI / 180;
            double lon = WrapLongitude(longitudeDegrees) * Math.PI / 180;
            double lifted = height * (1 + (FlatHeightScale - 1) * fold);
            double curve = 1 - fold;
            if (curve < 1e-6)
            {
                return new Vector3((float)lifted, (float)lat, (float)-lon);
            }
            double sphere = 1 / curve;
            double a = lat * curve, b = lon * curve;
            double reach = sphere + lifted;
            double across = Math.Cos(a);
            // Written so that the two large terms cancel before anything is rounded to a float.
            double x = reach * across * Math.Cos(b) - sphere + curve;
            return new Vector3((float)x, (float)(reach * Math.Sin(a)), (float)(-reach * across * Math.Sin(b)));
        }

        /// <summary>The unit vector straight out of the surface at a place, as the surface is folded.</summary>
        public Vector3 UpAt(double latitudeDegrees, double longitudeDegrees)
        {
            double a = latitudeDegrees * Math.PI / 180 * Curve;
            double b = (Fold > 0f ? WrapLongitude(longitudeDegrees) : longitudeDegrees) * Math.PI / 180 * Curve;
            return new Vector3((float)(Math.Cos(a) * Math.Cos(b)), (float)Math.Sin(a), (float)(-Math.Cos(a) * Math.Sin(b)));
        }

        /// <summary>Where a place is in world space for this camera's fold.</summary>
        public Vector3 Place(double latitudeDegrees, double longitudeDegrees, float height = 0f) =>
            PlaceOnFold(latitudeDegrees, longitudeDegrees, height, Fold);

        /// <summary>
        ///  Takes a point given as it is on the globe, a unit vector scaled out by its height,
        ///  to where it is on the surface as it is folded. On the globe it is the point itself.
        /// </summary>
        public Vector3 Folded(Vector3 point)
        {
            if (Fold <= 0f)
            {
                return point;
            }
            float length = point.Length();
            if (length < 1e-6f)
            {
                return point;
            }
            var (lat, lon) = ToLatLon(point / length);
            return PlaceOnFold(lat, lon, length - 1f, Fold);
        }

        /// <summary>True if a point, given as it is on the globe, is in front of the camera, whatever the planet hides.</summary>
        public bool InFront(Vector3 point) => Vector3.Dot(Folded(point) - Eye, Forward) > 1e-4f;

        /// <summary>
        ///  Screen position of a point on (or just above) the sphere. <paramref name="facing"/>
        ///  is 1 directly below the camera and 0 on the horizon; negative means hidden. The point
        ///  is given as it is on the globe, and is drawn where the fold puts it.
        /// </summary>
        public bool Project(Vector3 point, out PointF screen, out float facing)
        {
            if (Fold > 0f)
            {
                return ProjectPlaced(Folded(point), out screen, out facing);
            }
            var v = point - Eye;
            float z = Vector3.Dot(v, Forward);
            facing = (Vector3.Dot(Eye, point) - 1f) / (EyeLength - 1f);
            if (z <= 1e-4f)
            {
                screen = PointF.Empty;
                return false;
            }
            screen = new PointF(CentreX + Focal * Vector3.Dot(v, Right) / z, CentreY - Focal * Vector3.Dot(v, Up) / z);
            return facing > 0f;
        }

        /// <summary>
        ///  Screen position of a point already placed on the folded surface, as <see cref="Place"/>
        ///  gives it. <paramref name="facing"/> is as for <see cref="Project"/>.
        /// </summary>
        public bool ProjectPlaced(Vector3 placed, out PointF screen, out float facing)
        {
            var v = placed - Eye;
            float z = Vector3.Dot(v, Forward);
            facing = FacingOf(placed);
            if (z <= 1e-4f)
            {
                screen = PointF.Empty;
                return false;
            }
            screen = new PointF(CentreX + Focal * Vector3.Dot(v, Right) / z, CentreY - Focal * Vector3.Dot(v, Up) / z);
            return facing > 0f;
        }

        /// <summary>
        ///  How squarely a placed point faces the camera: 1 directly below it and 0 on the
        ///  horizon of the sphere the surface is part of. A flat map has no horizon, so every
        ///  point on it faces the camera.
        /// </summary>
        public float FacingOf(Vector3 placed)
        {
            if (Fold <= 0f)
            {
                return (Vector3.Dot(Eye, placed) - 1f) / (EyeLength - 1f);
            }
            // Measured from the place at 0° N 0° E, which the sphere touches whatever its size,
            // so nothing here grows without limit as the surface flattens.
            var eye = Eye - new Vector3(Curve, 0f, 0f);
            var point = placed - new Vector3(Curve, 0f, 0f);
            float above = eye.LengthSquared() * Curve + 2f * eye.X;
            float centreToEye = MathF.Sqrt(Math.Max(0f, eye.LengthSquared() * Curve * Curve + 2f * eye.X * Curve + 1f));
            float height = above / (centreToEye + 1f);
            return height <= 1e-6f ? 0f : (Curve * Vector3.Dot(eye, point) + eye.X + point.X) / height;
        }

        /// <summary>Latitude and longitude in degrees under a screen position, if it's on the planet.</summary>
        public bool Unproject(float x, float y, out double latitude, out double longitude)
        {
            float a = (x - CentreX) / Focal;
            float b = (CentreY - y) / Focal;
            var direction = a * Right + b * Up + Forward;

            if (Fold > 0f)
            {
                return UnprojectFolded(direction, out latitude, out longitude);
            }

            float quadA = Vector3.Dot(direction, direction);
            float quadB = 2f * Vector3.Dot(Eye, direction);
            float quadC = Vector3.Dot(Eye, Eye) - 1f;
            float discriminant = quadB * quadB - 4f * quadA * quadC;
            if (discriminant <= 0f)
            {
                latitude = longitude = 0;
                return false;
            }

            float t = (-quadB - MathF.Sqrt(discriminant)) / (2f * quadA);
            (latitude, longitude) = ToLatLon(Eye + t * direction);
            return true;
        }

        /// <summary>
        ///  Where a line of sight meets the unrolled surface. The sphere is written about the
        ///  place at 0° N 0° E, which keeps the sum exact as its radius grows, and becomes the
        ///  flat map's plane when the curve is nothing. Beyond the poles or 180° there is no surface.
        /// </summary>
        private bool UnprojectFolded(Vector3 direction, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            double curve = Curve;
            double ex = Eye.X - curve, ey = Eye.Y, ez = Eye.Z;
            double dx = direction.X, dy = direction.Y, dz = direction.Z;
            double quadA = (dx * dx + dy * dy + dz * dz) * curve / 2;
            double quadB = (ex * dx + ey * dy + ez * dz) * curve + dx;
            double quadC = (ex * ex + ey * ey + ez * ez) * curve / 2 + ex;
            double discriminant = quadB * quadB - 4 * quadA * quadC;
            if (discriminant < 0 || quadB >= 0)
            {
                return false;
            }
            double t = 2 * quadC / (-quadB + Math.Sqrt(discriminant));
            double hx = ex + t * dx, hy = ey + t * dy, hz = ez + t * dz;
            double lat, lon;
            if (curve < 1e-6)
            {
                lat = hy;
                lon = -hz;
            }
            else
            {
                lat = Math.Asin(Math.Clamp(curve * hy, -1, 1)) / curve;
                lon = Math.Atan2(-curve * hz, 1 + curve * hx) / curve;
            }
            if (Math.Abs(lat) > Math.PI / 2 || Math.Abs(lon) > Math.PI)
            {
                return false;
            }
            latitude = lat * 180 / Math.PI;
            longitude = lon * 180 / Math.PI;
            return true;
        }
    }
}
