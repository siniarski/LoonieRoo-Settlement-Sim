// The far / crowd tier: a MultiMesh of characters animated by a vertex animation texture (Vat.cs).
// No skeleton: each vertex reads its position for this frame (and the next, blended) from the
// texture, by its index. Per instance:
//   INSTANCE_CUSTOM  x = clip start row x 1024 + frame count, y = phase 0..1, z = speed, w = pattern seed
//   COLOR            dirt, blood, torn, wet (the instance colour)
// Per vertex, CUSTOM0: linear rgb and the garment's seed code (0 = skin), as character_merged.gdshader.
// Normals: flat, from the screen-space derivatives of the position (no normals in the texture).
shader_type spatial;
render_mode cull_disabled;

#include "res://Characters/character.gdshaderinc"

uniform sampler2D vat : filter_nearest, repeat_disable;
uniform float fps = 30.0;

varying vec4 dye;
varying vec4 cond;
varying float own_seed;

void vertex() {
	float packed = INSTANCE_CUSTOM.x;
	float frames = mod(packed, 1024.0);
	int start = int(floor(packed / 1024.0));
	float t = mod(TIME * fps * INSTANCE_CUSTOM.z + INSTANCE_CUSTOM.y * frames, frames);
	int f0 = int(floor(t));
	int f1 = int(mod(float(f0 + 1), frames));
	vec3 a = texelFetch(vat, ivec2(VERTEX_ID, start + f0), 0).xyz;
	vec3 b = texelFetch(vat, ivec2(VERTEX_ID, start + f1), 0).xyz;
	VERTEX = mix(a, b, fract(t));
	dye = CUSTOM0;
	cond = COLOR;
	own_seed = INSTANCE_CUSTOM.w;
}

void fragment() {
	bool skin = dye.a < 0.5 / 98.0;
	vec4 w = skin ? vec4(cond.x * 0.6, cond.y * 0.6, 0.0, cond.w) : cond;
	float s = skin ? 0.0 : round(dye.a * 98.0) - 1.0 + own_seed;
	vec4 c = wear_on(dye.rgb, UV, s, w);
	if (c.a < 0.0) discard;
	ALBEDO = c.rgb;
	ROUGHNESS = c.a;
	NORMAL = normalize(cross(dFdy(VERTEX), dFdx(VERTEX)));
}
