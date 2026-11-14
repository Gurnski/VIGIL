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
    /// </summary>
    internal readonly struct GlobeCamera
    {
        public const float MaxZoom = 8f;
        public const float MaxTilt = 70f;
        public const float EarthRadiusKm = 6371f;
        private const float FarDistance = 4.2f;   // planet radii from the centre at zoom 1, untilted
        private const float NearDistance = 1.3f;  // planet radii at MaxZoom

        public readonly float CentreX;
        public readonly float CentreY;

        /// <summary>Radius of the planet's silhouette in pixels when untilted.</summary>
        public readonly float Radius;

        /// <summary>Distance from the target point to the eye, in planet radii.</summary>
        public readonly float Range;

        /// <summary>Focal length in pixels.</summary>
        public readonly float Focal;

        /// <summary>Distance from the planet's centre to the eye, in planet radii.</summary>
        public readonly float EyeLength;

        public readonly Vector3 Target;
        public readonly Vector3 Eye;
        public readonly Vector3 Right;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;

        public GlobeCamera(double latitude, double longitude, double zoom, double headingDegrees, double tiltDegrees, float width, float height)
        {
            CentreX = width / 2f;
            CentreY = height / 2f;

            float fit = MathF.Min(width, height) * 0.5f * 0.84f;
            Radius = fit * (float)zoom;
            float fraction = Math.Clamp((float)(Math.Log(zoom) / Math.Log(MaxZoom)), 0f, 1f);
            float distance = FarDistance + (NearDistance - FarDistance) * fraction;
            Range = distance - 1f;
            Focal = Radius * MathF.Sqrt(distance * distance - 1f);

            Target = ToUnit(latitude, longitude);
            var north = Vector3.UnitY;
            var localNorth = Vector3.Normalize(north - Vector3.Dot(north, Target) * Target);
            var localEast = Vector3.Cross(localNorth, Target);

            float heading = (float)(headingDegrees * Math.PI / 180);
            float tilt = (float)(Math.Clamp(tiltDegrees, 0, MaxTilt) * Math.PI / 180);
            var ahead = MathF.Cos(heading) * localNorth + MathF.Sin(heading) * localEast;
            var lift = MathF.Cos(tilt) * Target - MathF.Sin(tilt) * ahead;   // from the target towards the eye

            Eye = Target + Range * lift;
            Forward = -lift;
            Up = MathF.Cos(tilt) * ahead + MathF.Sin(tilt) * Target;
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

        /// <summary>
        ///  Screen position of a point on (or just above) the sphere. <paramref name="facing"/>
        ///  is 1 directly below the camera and 0 on the horizon; negative means hidden.
        /// </summary>
        public bool Project(Vector3 point, out PointF screen, out float facing)
        {
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

        /// <summary>Latitude and longitude in degrees under a screen position, if it's on the planet.</summary>
        public bool Unproject(float x, float y, out double latitude, out double longitude)
        {
            float a = (x - CentreX) / Focal;
            float b = (CentreY - y) / Focal;
            var direction = a * Right + b * Up + Forward;

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
    }
}
