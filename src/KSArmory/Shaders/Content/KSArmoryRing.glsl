// A targeting ring painted on whatever the depth buffer says is under each pixel: terrain, a pad, a
// rock. One dispatch per ring from CloudPass, over the ring's own patch of screen. The circle is
// exact at any size because it is measured from the scene point, not drawn as a polygon draped on
// sampled heights, and its line is a fixed number of pixels wide however far off or oblique.
//
// Bindings follow ComputePipelineWrapper's order: the storage images, then the images it samples.
// With RING_MSAA defined it paints splats alone, into every sample of KSA's multisampled scene, before
// the resolve that would otherwise overwrite them; the copy and the rings are the plain variant's.

layout (local_size_x = 8, local_size_y = 8) in;

#ifdef RING_MSAA
layout (set = 1, binding = 0, Rgba16f) uniform image2D sceneCopy;
layout (set = 1, binding = 1, Rgba16f) uniform image2DMS sceneColor;
#else
layout (set = 1, binding = 0, Rgba16f) uniform image2D sceneColor;
layout (set = 1, binding = 1, Rgba16f) uniform image2D sceneCopy;
#endif
layout (set = 1, binding = 2) uniform sampler2D sceneDepth;
// KSA's pre-pass normal, w exactly one on terrain and clutter; the pass's own layer when TileWidth.w is 0.
layout (set = 1, binding = 3) uniform sampler2D prePassNormal;

layout (push_constant) uniform PushConstants
{
    mat4 InvViewProj;   // clip -> camera-relative world
    vec4 CentreRadius;  // the ring's centre, camera-relative, and its radius
    vec4 UpInner;       // the local vertical there, and a second ring's radius, zero for none -- for an
                        // ellipse, the vertical and the long axis each packed into two floats
    vec4 Colour;        // the ink, and the brightness it keeps over the darkest ground -- negative for a
                        // paintball splat, minus one less its seed
    vec4 TileWidth;     // the tile's first pixel, the line's half-width in pixels, and dashes or zero --
                        // or minus an ellipse's short radius, which is what says it is one; a negative
                        // half-width copies the scene into sceneCopy instead. On a splat, w is 1 where
                        // prePassNormal can be read
} pc;

// A unit vector out of two floats, as CloudPass.OctahedralPack put it in.
vec3 OctahedralUnpack(vec2 o)
{
    vec3 n = vec3(o.x, o.y, 1.0 - abs(o.x) - abs(o.y));
    if (n.z < 0.0) n.xy = (1.0 - abs(n.yx)) * vec2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    return normalize(n);
}

vec3 WorldAt(vec2 uv, float deviceZ)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, deviceZ, 1.0);
    vec4 p = pc.InvViewProj * clip;
    return p.xyz / p.w;
}

// A splat on something standing on the ground -- a pad, an apron -- is painted only facing up and this
// close to the splat's own plane; a craft overhead is neither.
const float SplatStandsMetres = 20.0;
const float SplatFacesUp = 0.5;

// The surface's normal from the depth beside it, each axis from the nearer neighbour so an edge does
// not bend it, turned toward the camera.
vec3 SurfaceNormal(ivec2 at, ivec2 size, vec3 p)
{
    vec3 side[4];
    ivec2 offsets[4] = ivec2[4](ivec2(1, 0), ivec2(-1, 0), ivec2(0, 1), ivec2(0, -1));
    for (int k = 0; k < 4; k++)
    {
        ivec2 q = clamp(at + offsets[k], ivec2(0), size - 1);
        float z = texelFetch(sceneDepth, q, 0).r;
        side[k] = z > 0.0 ? WorldAt((vec2(q) + 0.5) / vec2(size), z) - p : vec3(1.0e30);
    }

    vec3 dx = dot(side[0], side[0]) < dot(side[1], side[1]) ? side[0] : -side[1];
    vec3 dy = dot(side[2], side[2]) < dot(side[3], side[3]) ? side[2] : -side[3];
    vec3 n = cross(dx, dy);
    if (!(dot(n, n) > 0.0) || any(isinf(n)) || any(isnan(n))) return normalize(-p);

    n = normalize(n);
    return dot(n, p) > 0.0 ? -n : n;
}

// Whether a splat's pixel is the ground it lies on: terrain or clutter by the pre-pass where it can be
// read, anything else only facing up and near the splat's plane.
bool SplatOnGround(ivec2 at, ivec2 size, vec3 p, vec3 up, float h)
{
    if (pc.TileWidth.w > 0.5)
    {
        vec4 normalRough = texelFetch(prePassNormal, at, 0);
        if (normalRough.w == 1.0) return true;
        return abs(h) < SplatStandsMetres && dot(normalize(normalRough.xyz), up) > SplatFacesUp;
    }

    return abs(h) < SplatStandsMetres && dot(SurfaceNormal(at, size, p), up) > SplatFacesUp;
}

float Hash(vec2 p)
{
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float Noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(Hash(i), Hash(i + vec2(1.0, 0.0)), f.x), mix(Hash(i + vec2(0.0, 1.0)), Hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// A paintball splat on the ground: a ragged blob filling the inner part of R, splash rays out to its rim
// and droplets between them, lit by the ground under it. GroundSplats sizes R to the rays' reach.
float Splat(vec3 across, float r, float R, vec3 up, float footprint, float seed)
{
    float blobR = R / 1.6;
    vec3 e1 = normalize(cross(up, abs(up.z) < 0.9 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0)));
    vec3 e2 = cross(up, e1);
    vec2 q = vec2(dot(across, e1), dot(across, e2));
    vec2 circle = q / max(r, 1.0e-6);
    float angle = atan(q.y, q.x);

    float edge = blobR * (0.7 + 0.2 * Noise(circle * 2.0 + seed) + 0.1 * Noise(circle * 6.0 + seed * 1.3));
    float blob = 1.0 - smoothstep(edge - footprint, edge + footprint, r);

    float turn = angle / 6.2831853 * 16.0 + Hash(vec2(seed, 1.0));
    float id = floor(turn);
    float centred = 1.0 - abs(2.0 * fract(turn) - 1.0);
    float reach = blobR * (1.1 + 0.5 * Hash(vec2(id, seed)));
    float taper = clamp(1.0 - (r - edge) / max(reach - edge, 1.0e-3), 0.0, 1.0);
    float ray = step(r, reach) * smoothstep(1.0 - 0.3 * taper, 1.0 - 0.3 * taper + 0.05, centred);

    vec2 cell = floor(q / (blobR * 0.07));
    vec2 inCell = fract(q / (blobR * 0.07)) - 0.5 - (vec2(Hash(cell + seed), Hash(cell + seed + 7.0)) - 0.5) * 0.6;
    float drop = step(0.8 + 0.15 * (r / R), Hash(cell + seed + 3.0)) * (1.0 - smoothstep(0.15, 0.22, length(inCell)))
                 * step(edge, r) * step(r, R);

    return clamp(blob + ray + drop, 0.0, 1.0);
}

// How far along the ring's radius the ground moves for one pixel's step, off the tangent plane at
// the ring: the step across the view at this depth, slid along the ray back onto the plane. Exact
// for flat ground, and what keeps the line one width from the rim to the far side of the circle.
float RadialStep(vec3 p, vec3 dir, vec3 step, vec3 up, vec3 radial)
{
    float facing = dot(dir, up);
    facing = sign(facing) * max(abs(facing), 0.05);
    vec3 onGround = step - dir * (dot(step, up) / facing);
    return abs(dot(onGround, radial));
}

// Coverage of a line of the given half-width at radius R, and of the dark border round it.
float Line(float r, float R, float footprint, float halfWidth, out float border)
{
    float px = abs(r - R) / footprint;
    border = 1.0 - smoothstep(halfWidth + 0.5, halfWidth + 1.5, px);
    return 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, px);
}

void main()
{
    ivec2 size = imageSize(sceneColor);
    ivec2 at = ivec2(gl_GlobalInvocationID.xy) + ivec2(pc.TileWidth.xy);
    if (at.x >= size.x || at.y >= size.y) return;

#ifndef RING_MSAA
    if (pc.TileWidth.z < 0.0)
    {
        imageStore(sceneCopy, at, imageLoad(sceneColor, at));
        return;
    }
#endif

    // Reverse-Z: zero is the sky, which a ring on the ground has no business with.
    float deviceZ = texelFetch(sceneDepth, at, 0).r;
    if (deviceZ <= 0.0) return;

    vec2 uv = (vec2(at) + 0.5) / vec2(size);
    vec3 p = WorldAt(uv, deviceZ);

    bool ellipse = pc.TileWidth.w < 0.0;
    vec3 up = ellipse ? OctahedralUnpack(pc.UpInner.xy) : pc.UpInner.xyz;
    vec3 d = p - pc.CentreRadius.xyz;
    float h = dot(d, up);
    vec3 across = d - up * h;
    float r = length(across);
    float R = pc.CentreRadius.w;

    // A craft or a tower standing well over the ring is not the ground it circles.
    if (abs(h) > max(R, 50.0) || r < 1.0e-3) return;

    vec3 dir = normalize(p);
    vec3 radial = across / r;

    // An ellipse is measured as its own normalised radius, and the distance off its edge as that
    // radius's excess over its gradient -- so the line holds one width round the whole of it, the
    // way a circle's does. Then it is a circle of radius R in that distance, for everything below.
    if (ellipse)
    {
        vec3 major = OctahedralUnpack(pc.UpInner.zw);
        vec3 minorDir = normalize(cross(up, major));
        float a = R;
        float b = -pc.TileWidth.w;
        float u = dot(across, major) / a;
        float v = dot(across, minorDir) / b;
        float n = sqrt(u * u + v * v);
        vec3 grad = (major * (u / a) + minorDir * (v / b)) / max(n, 1.0e-6);
        float g = max(length(grad), 1.0e-9);

        radial = grad / g;
        r = R + (n - 1.0) / g;
    }
    float stepX = RadialStep(p, dir, WorldAt(uv + vec2(1.0 / float(size.x), 0.0), deviceZ) - p, up, radial);
    float stepY = RadialStep(p, dir, WorldAt(uv + vec2(0.0, 1.0 / float(size.y)), deviceZ) - p, up, radial);
    float footprint = max(length(vec2(stepX, stepY)), 1.0e-4);

    if (pc.Colour.w < 0.0 && !ellipse)
    {
        if (!SplatOnGround(at, size, p, up, h)) return;

        float paint = Splat(across, r, R, up, footprint, -pc.Colour.w - 1.0);
        if (paint <= 0.0) return;

        float lit = dot(imageLoad(sceneCopy, at).rgb, vec3(0.2126, 0.7152, 0.0722));
        vec3 coat = pc.Colour.rgb * max(lit * 1.6, 0.03);
#ifdef RING_MSAA
        for (int s = 0; s < imageSamples(sceneColor); s++)
        {
            vec4 under = imageLoad(sceneColor, at, s);
            imageStore(sceneColor, at, s, vec4(mix(under.rgb, coat, paint * 0.95), under.a));
        }
#else
        vec4 under = imageLoad(sceneColor, at);
        imageStore(sceneColor, at, vec4(mix(under.rgb, coat, paint * 0.95), under.a));
#endif
        return;
    }

#ifdef RING_MSAA
}
#else

    float halfWidth = pc.TileWidth.z;
    float border;
    float cover = Line(r, R, footprint, halfWidth, border);

    if (pc.UpInner.w > 0.0)
    {
        float innerBorder;
        cover = max(cover, Line(r, pc.UpInner.w, footprint, halfWidth, innerBorder));
        border = max(border, innerBorder);
    }

    // Dashed by the angle round the centre, so the dashes are the same share of the ring at any
    // size or distance.
    if (!ellipse && pc.TileWidth.w > 0.0)
    {
        vec3 e1 = normalize(cross(up, abs(up.z) < 0.9 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0)));
        vec3 e2 = cross(up, e1);
        float t = fract(atan(dot(across, e2), dot(across, e1)) / 6.2831853 * pc.TileWidth.w);
        float dash = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(0.55, 0.60, t));
        cover *= dash;
        border *= dash;
    }

    if (border <= 0.0) return;

    // Brighter than the ground it lies on by a fixed factor rather than by a fixed amount: the
    // image is not yet exposed, so a set brightness is a glare at night and invisible by day. The
    // floor keeps it readable on ground too dark to multiply. The ground is read off the copy taken
    // before any ring, or stacked rings multiply each other's ink.
    vec4 was = imageLoad(sceneColor, at);
    float ground = dot(imageLoad(sceneCopy, at).rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 ink = pc.Colour.rgb * max(ground * 5.0, pc.Colour.w * 4.0);

    vec3 c = mix(was.rgb, was.rgb * 0.15, border * 0.85);
    c = mix(c, ink, cover);
    imageStore(sceneColor, at, vec4(c, was.a));
}
#endif
