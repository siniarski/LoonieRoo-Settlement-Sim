// The dead's clothes in any colour from one painted map (Looks.cs Recolour): the look model's own colour map,
// and its recolour mask (art/scripts/build_lookdev.py: red the top's clean cloth, green the trousers'; blood,
// dirt and tears left out). Under each mask the cloth becomes the colour wanted, times its own brightness
// against the cloth's mean (from the build), so the painted shading, weave and wear stay and the colour is
// the palette's on average. Plain material values (one material per model file and colour pair, shared by
// everyone wearing them): no instance uniforms.

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D mask_tex : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform vec3 top_colour = vec3(0.5);     // linear
uniform vec3 bottom_colour = vec3(0.5);
uniform float top_lum = 0.1;             // the cloth's mean linear luminance in the map
uniform float bottom_lum = 0.1;
uniform float roughness = 0.85;

void fragment() {
	vec3 c = texture(albedo_tex, UV).rgb;
	vec2 m = texture(mask_tex, UV).rg;
	float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
	c = mix(c, top_colour * (lum / top_lum), m.r);
	c = mix(c, bottom_colour * (lum / bottom_lum), m.g);
	ALBEDO = clamp(c, vec3(0.0), vec3(1.0));
	ROUGHNESS = roughness;
}
