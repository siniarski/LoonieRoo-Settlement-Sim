// The conditions on skin and cloth (docs/characters.md "Clothing" and "conditions"), shared by
// character.gdshader (near: a mesh per piece, colour per mesh) and character_merged.gdshader (mid:
// one merged mesh, colour per vertex). Patterns come from the mesh's "Rest" UV (art/scripts/garments.py
// rest_uv: u = rest height up the body, v = round it), so they stick to the cloth as it moves and dirt
// can gather low.
//   wear   x dirt, y blood, z torn, w wet:
//   dirt   brown grime, heavier towards the ground, in patches
//   blood  dark red splashes
//   torn   holes (cut out) and frayed, faded wear around them
//   wet    darker and glossier
//   seed   moves the patterns, so two shirts don't tear the same way

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
	return v;
}

// The colour with its conditions in rgb and the roughness in a; a < 0 means a hole (discard).
// The parameters must not share a name with an instance uniform (wear, seed, tint): Godot puts the
// uniform's buffer read in place of the name everywhere, parameters included, so the merged
// shader's skin (passed torn = 0) tore with the clothes and every garment got seed 0.
vec4 wear_on(vec3 col, vec2 uv, float pattern_seed, vec4 cond) {
	float dirt = cond.x, blood = cond.y, torn = cond.z, wet = cond.w;
	vec2 p = uv * vec2(9.0, 9.0) + vec2(pattern_seed * 13.7, pattern_seed * 7.3);
	float height = uv.x;  // 0 at the feet, 1 at the top of the head

	float holes = fbm(p * 2.6 + 7.7);
	if (torn > 0.0 && holes > 1.0 - torn * 0.65) return vec4(0.0, 0.0, 0.0, -1.0);
	float fray = torn * smoothstep(0.45, 0.8, holes);
	col = mix(col, col * 0.75 + 0.12, fray);  // worn and faded round the holes

	float grime = dirt * clamp(1.5 - height * 1.3, 0.0, 1.0) * smoothstep(0.3, 0.7, fbm(p * 1.3));
	col = mix(col, vec3(0.22, 0.17, 0.11), clamp(grime, 0.0, 0.9));

	float splash = smoothstep(1.0 - blood * 0.7, 1.03 - blood * 0.7, fbm(p * 1.8 + 3.1));
	col = mix(col, vec3(0.26, 0.02, 0.02), splash);

	col *= 1.0 - 0.35 * wet;
	return vec4(col, mix(0.88, 0.35, max(wet, splash * 0.7)));
}
