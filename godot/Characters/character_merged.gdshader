// Characters at mid range: the outfit merged into one mesh (OutfitMerge.cs), so one draw call and
// one set of instance values per character. Per vertex, CUSTOM0 (floats):
//   skin      (r, g, b, 0): its colour, linear
//   garment   (slot, 0, 0, (garment seed + 1) / 98): its colour is the character's for that slot
// Per character, instance uniforms:
//   dyes_a    slot colours 0-3 (shirt, jacket, legs, feet), each 8-bit sRGB packed into a float
//   dyes_b    slot colours 4-6 (hands, head, back), w = the character's pattern seed
//   wear      x dirt, y blood, z torn, w wet (skin takes 0.6 of the dirt and blood and never tears,
//             as at near range)
shader_type spatial;
render_mode cull_disabled;

#include "res://Characters/character.gdshaderinc"

instance uniform vec4 dyes_a = vec4(0.0);
instance uniform vec4 dyes_b = vec4(0.0);
instance uniform vec4 wear = vec4(0.0);

varying vec4 dye;  // rgb linear colour, a = 0 for skin, else the garment's seed code

vec3 unpack_srgb(float f) {
	vec3 c = vec3(floor(f / 65536.0), mod(floor(f / 256.0), 256.0), mod(f, 256.0)) / 255.0;
	// sRGB to linear, as Godot converts the near tier's colours.
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	dye = CUSTOM0;
	if (CUSTOM0.a > 0.0) {
		int slot = int(round(CUSTOM0.x));
		float packed = slot < 4 ? dyes_a[slot] : dyes_b[slot - 4];
		dye.rgb = unpack_srgb(packed);
	}
}

void fragment() {
	bool skin = dye.a < 0.5 / 98.0;
	vec4 w = skin ? vec4(wear.x * 0.6, wear.y * 0.6, 0.0, wear.w) : wear;
	float s = skin ? 0.0 : round(dye.a * 98.0) - 1.0 + dyes_b.w;
	vec4 c = wear_on(dye.rgb, UV, s, w);
	if (c.a < 0.0) discard;
	ALBEDO = c.rgb;
	ROUGHNESS = c.a;
}
