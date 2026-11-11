// Grain and scale over a flat colour, without the mesh being unwrapped: a detail texture (a layer of
// godot/assets/surfaces, loaded by godot/World3D/Surfaces.cs into the global `surface_layers`) laid over the
// surface by where it is, and multiplied into its colour. docs/textures.md is the page on this.
//
// In a spatial shader:
//     #include "res://assets/shaders/surface_detail.gdshaderinc"
//     void vertex()   { SURFACE_BY_WORLD }                      // the static world: the texture stays put in the world
//                     { SURFACE_BY_OBJECT }                     // a thing that moves: it stays put on the thing
//     void fragment() { ALBEDO = COLOR.rgb * SURFACE_DETAIL; }  // before snowy() and wetter(), if the shader has them
//
// Which layer, per vertex, in UV (all three corners of a face alike; Surfaces.What, Turned and Mapped make it):
//     UV.x   0: no detail (so a mesh without UVs draws as it always did)
//            layer + 1: that layer, laid by position: from above on what faces up or down, from the side on the rest
//            -(layer + 1): that layer at UV2 (the mesh's own coordinates, in metres: a roof's slope, a skinned body)
//     UV.y   repeats per metre (1 / the layer's size in layers.json); negative: turned a quarter (boards upright)
// Up in a texture is up on a wall; on the ground its top is north.
//
// A detail texel is a multiplier stored halved and averages 0.5, so the colour's average is unchanged: the mipmaps
// take a far surface back to its flat colour, and past SURFACE_FADE_TO metres there is none at all (the far view has
// none). One texture sample a fragment, no branches.

global uniform sampler2DArray surface_layers : filter_linear_mipmap_anisotropic, repeat_enable;

/// Metres from the eye over which the detail fades out.
const float SURFACE_FADE_FROM = 26.0;
const float SURFACE_FADE_TO = 48.0;

varying vec3 surface_p;
varying vec3 surface_n;

#define SURFACE_BY_WORLD surface_p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; surface_n = mat3(MODEL_MATRIX) * NORMAL;
#define SURFACE_BY_OBJECT surface_p = VERTEX; surface_n = NORMAL;
#define SURFACE_DETAIL surface_detail(surface_p, surface_n, UV, UV2, length(VERTEX))

/// A layer's texel as a multiplier round 1 (for a shader with coordinates of its own).
vec3 surface_sample(vec2 uv, float layer) {
	return texture(surface_layers, vec3(uv, layer)).rgb * 2.0;
}

/// The multiplier for a surface's colour at position p with normal n (in the space the texture should stick to),
/// `what` as UV above, `mapped` the mesh's own coordinates (UV2), `dist` metres from the eye.
vec3 surface_detail(vec3 p, vec3 n, vec2 what, vec2 mapped, float dist) {
	vec3 a = abs(n);
	vec2 uv = a.y >= max(a.x, a.z) ? p.xz : (a.x > a.z ? vec2(p.z, -p.y) : vec2(p.x, -p.y));
	uv = what.y < 0.0 ? uv.yx : uv;
	uv = what.x < 0.0 ? mapped : uv;
	float layer = max(abs(what.x) - 1.0, 0.0);
	float on = step(0.5, abs(what.x)) * (1.0 - smoothstep(SURFACE_FADE_FROM, SURFACE_FADE_TO, dist));
	return mix(vec3(1.0), surface_sample(uv * abs(what.y), layer), on);
}
