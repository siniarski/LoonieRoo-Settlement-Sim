// Characters' skin and clothes at near range: a mesh per piece, one material for everyone, with
// per-instance values (instance uniforms, set per mesh with SetInstanceShaderParameter), so
// differently dressed and damaged characters share it. The patterns: character.gdshaderinc.
//   tint   the colour (a garment's dye, or skin); its alpha is the seed
//   wear   x dirt, y blood, z torn, w wet
shader_type spatial;
render_mode cull_disabled;

#include "res://Characters/character.gdshaderinc"

// Packed in two (each instance uniform takes a slot in a global buffer: crowds run out of them).
instance uniform vec4 tint = vec4(0.55, 0.53, 0.5, 0.0);  // rgb colour, a = seed
instance uniform vec4 wear = vec4(0.0);                   // dirt, blood, torn, wet

void fragment() {
	vec4 c = wear_on(tint.rgb, UV, tint.a, wear);
	if (c.a < 0.0) discard;
	ALBEDO = c.rgb;
	ROUGHNESS = c.a;
}
