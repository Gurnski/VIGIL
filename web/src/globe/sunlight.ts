import { LinearFilter, ShaderMaterial, SRGBColorSpace, Texture, Vector3 } from 'three';
import type { Material, Mesh } from 'three';

// Day and night on the globe, from where the sun really is, as the desktop app lights its own
// (`SunPosition.cs` and the surface shading in `GlobeRenderer.cs`). The day side is the Blue
// Marble image, dimmer and warmer towards the terminator, with a narrow amber band along it.
// The night side is the Black Marble image, which carries the lights of cities.
//
// It is the picture of the Earth that is lit and nothing else. The layer, the columns and the
// markers keep the colours the legend gives them on either side of the terminator.
//
// A flat map is not lit this way. `setAmount` takes the lighting from all of it to none as the
// globe unrolls, so the map is the day image, even from edge to edge, and nothing jumps on the way.

const Radians = Math.PI / 180;
/** The sun moves a quarter of a degree a minute, so its place is worked out again this often. */
const RefreshMilliseconds = 30000;

/** Days since noon on 1 January 2000, the moment the sun's coordinates are counted from. */
function daysSinceJ2000(date: Date): number {
  return (date.getTime() - Date.UTC(2000, 0, 1, 12, 0, 0)) / 86400000;
}

/**
 * Where the sun is directly overhead at a moment, in degrees. The US Naval Observatory's
 * approximate solar coordinates, good to a fraction of a degree, which is plenty for lighting.
 */
export function subsolarPoint(date: Date): { lat: number; lng: number } {
  const days = daysSinceJ2000(date);
  const meanAnomaly = (357.529 + 0.98560028 * days) * Radians;
  const meanLongitude = 280.459 + 0.98564736 * days;
  const eclipticLongitude = (meanLongitude + 1.915 * Math.sin(meanAnomaly) + 0.02 * Math.sin(2 * meanAnomaly)) * Radians;
  const obliquity = (23.439 - 0.00000036 * days) * Radians;
  const rightAscension = Math.atan2(Math.cos(obliquity) * Math.sin(eclipticLongitude), Math.cos(eclipticLongitude)) / Radians;
  const declination = Math.asin(Math.sin(obliquity) * Math.sin(eclipticLongitude)) / Radians;
  // The sun is overhead where the local sidereal time equals its right ascension.
  const sidereal = (((280.46061837 + 360.98564736629 * days) % 360) + 360) % 360;
  const lng = ((((rightAscension - sidereal) % 360) + 540) % 360) - 180;
  return { lat: declination, lng };
}

const vertexShader = /* glsl */ `
  varying vec2 vUv;
  void main() {
    vUv = uv;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`;

// The figures are the desktop renderer's, which works on the image's own values and not on
// light. So the images are taken back to those values, shaded, and returned.
const fragmentShader = /* glsl */ `
  uniform sampler2D dayMap;
  uniform sampler2D nightMap;
  uniform float hasNight;
  uniform vec3 sunDirection;
  uniform float amount;
  varying vec2 vUv;

  const float pi = 3.141592653589793;

  void main() {
    vec3 day = pow(texture2D(dayMap, vUv).rgb, vec3(1.0 / 2.2));

    // Each place is lit as it stands on the real globe, whatever shape the surface is in:
    // where it is comes from its spot on the image, not from the mesh.
    float lat = (vUv.y - 0.5) * pi;
    float lng = (vUv.x - 0.5) * 2.0 * pi;
    vec3 up = vec3(cos(lat) * sin(lng), sin(lat), cos(lat) * cos(lng));
    float sun = dot(up, sunDirection);

    // The terminator is a soft band round the sun's horizon, not a hard line.
    float dayFactor = smoothstep(-0.10, 0.08, sun);
    // Daylight: mostly even, dimmer and warmer towards the terminator.
    float shade = 0.36 + 0.64 * sqrt(max(sun, 0.0));
    float warm = smoothstep(0.0, 0.35, sun);
    vec3 lit = day * shade * vec3(1.0, 0.84 + 0.16 * warm, 0.66 + 0.34 * warm);
    // Twilight: a narrow amber band along the terminator, where sunlight crosses the most air before it lands.
    float dusk = sun - 0.02;
    float twilight = exp(-dusk * dusk / (0.055 * 0.055));
    lit *= vec3(1.0 + 0.28 * twilight, 1.0 + 0.04 * twilight, 1.0 - 0.22 * twilight);

    // Night: the Black Marble image, which already carries faintly lit land. Until it has arrived, the day image, very dark.
    vec3 night = mix(day * vec3(0.05, 0.06, 0.09), pow(texture2D(nightMap, vUv).rgb, vec3(1.0 / 2.2)) * vec3(1.15, 1.12, 1.05), hasNight);

    vec3 globe = mix(night, lit, dayFactor);
    vec3 colour = mix(day, globe, amount);
    gl_FragColor = vec4(pow(clamp(colour, 0.0, 1.0), vec3(2.2)), 1.0);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }
`;

/** Lights the Earth's own mesh from the real sun. `dispose` gives the mesh back as it was. */
export class Sunlight {
  private readonly material: ShaderMaterial;
  private readonly original: Material | Material[];
  private readonly timer: number;
  private readonly before: Mesh['onBeforeRender'];
  private night: Texture | null = null;
  private disposed = false;

  /** `largestTexture` is the widest image the device can hold as one texture. A wider night image is scaled down to it. */
  constructor(
    private readonly earth: Mesh,
    nightUrl: string,
    largestTexture: number,
  ) {
    this.original = earth.material;
    const lent = earth.material as Material & { map?: Texture | null };
    this.material = new ShaderMaterial({
      uniforms: {
        dayMap: { value: lent.map ?? null },
        nightMap: { value: null },
        hasNight: { value: 0 },
        sunDirection: { value: new Vector3(0, 0, 1) },
        amount: { value: 1 },
      },
      vertexShader,
      fragmentShader,
    });
    earth.material = this.material;
    // The library fetches the day image in its own time and puts it on whichever material the
    // mesh has when it arrives: the one it made, or this one once it has taken its place. So
    // the image is looked for on both at each drawing.
    const given = this.material as unknown as { map?: Texture | null };
    this.before = earth.onBeforeRender;
    earth.onBeforeRender = () => {
      const day = given.map ?? lent.map ?? null;
      if (this.material.uniforms.dayMap.value !== day) {
        this.material.uniforms.dayMap.value = day;
      }
    };
    this.place();
    this.timer = window.setInterval(() => this.place(), RefreshMilliseconds);

    const image = new Image();
    image.onload = () => {
      if (this.disposed) {
        return;
      }
      let source: HTMLImageElement | HTMLCanvasElement = image;
      if (image.naturalWidth > largestTexture) {
        const canvas = document.createElement('canvas');
        canvas.width = largestTexture;
        canvas.height = Math.round((image.naturalHeight * largestTexture) / image.naturalWidth);
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        source = canvas;
      }
      const texture = new Texture(source);
      texture.colorSpace = SRGBColorSpace;
      texture.minFilter = LinearFilter;
      texture.generateMipmaps = false;
      texture.needsUpdate = true;
      this.night = texture;
      this.material.uniforms.nightMap.value = texture;
      this.material.uniforms.hasNight.value = 1;
    };
    image.src = nightUrl;
  }

  /** How much of the day and night is shown: 1 on the globe, 0 on the flat map, and between the two as it folds. */
  setAmount(amount: number): void {
    this.material.uniforms.amount.value = Math.min(1, Math.max(0, amount));
  }

  dispose(): void {
    this.disposed = true;
    window.clearInterval(this.timer);
    this.earth.material = this.original;
    this.earth.onBeforeRender = this.before;
    this.material.dispose();
    this.night?.dispose();
  }

  /** Points the light from where the sun is now. */
  private place(): void {
    const { lat, lng } = subsolarPoint(new Date());
    const across = Math.cos(lat * Radians);
    (this.material.uniforms.sunDirection.value as Vector3).set(across * Math.sin(lng * Radians), Math.sin(lat * Radians), across * Math.cos(lng * Radians));
  }
}
