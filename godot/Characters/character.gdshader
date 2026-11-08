// Characters' skin and clothes (docs/characters.md "Clothing" and "conditions"): one material for
// everyone, with per-instance values (instance uniforms, set per mesh with
// SetInstanceShaderParameter), so differently dressed and damaged characters share it.
// The patterns come from the mesh's "Rest" UV (art/scripts/garments.py rest_uv: u = rest height
// up the body, v = round it), so they stick to the cloth as it moves and dirt can gather low.
//   tint   the colour (a garment's dye, or skin); its alpha is the seed
//   wear   x dirt, y blood, z torn, w wet:
//   dirt   brown grime, heavier towards the ground, in patches
//   blood  dark red splashes
//   torn   holes (cut out) and frayed, faded wear around them
//   wet    darker and glossier
//   seed   moves the patterns, so two shirts don't tear the same way
shader_type spatial;
render_mode cull_disabled;

// Packed in two (each instance uniform takes a slot in a global buffer: crowds run out of them).
instance uniform vec4 tint = vec4(0.55, 0.53, 0.5, 0.0);  // rgb colour, a = seed
instance uniform vec4 wear = vec4(0.0);                   // dirt, blood, torn, wet

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

void fragment() {
	float seed = tint.a, dirt = wear.x, blood = wear.y, torn = wear.z, wet = wear.w;
	vec2 p = UV * vec2(9.0, 9.0) + vec2(seed * 13.7, seed * 7.3);
	float height = UV.x;  // 0 at the feet, 1 at the top of the head
	vec3 col = tint.rgb;

	float holes = fbm(p * 2.6 + 7.7);
	if (torn > 0.0 && holes > 1.0 - torn * 0.65) discard;
	float fray = torn * smoothstep(0.45, 0.8, holes);
	col = mix(col, col * 0.75 + 0.12, fray);  // worn and faded round the holes

	float grime = dirt * clamp(1.5 - height * 1.3, 0.0, 1.0) * smoothstep(0.3, 0.7, fbm(p * 1.3));
	col = mix(col, vec3(0.22, 0.17, 0.11), clamp(grime, 0.0, 0.9));

	float splash = smoothstep(1.0 - blood * 0.7, 1.03 - blood * 0.7, fbm(p * 1.8 + 3.1));
	col = mix(col, vec3(0.26, 0.02, 0.02), splash);

	col *= 1.0 - 0.35 * wet;
	ALBEDO = col;
	ROUGHNESS = mix(0.88, 0.35, max(wet, splash * 0.7));
}
