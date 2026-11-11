// A look model's own painted map, with the dead's clothes in any colour and the kit's detail on cloth and shoes
// (Looks.cs Recolour; the living and the dead both draw through it).
//
// Recolouring: the look model's recolour mask (art/scripts/build_lookdev.py: red the top's clean cloth, green the
// trousers'; blood, dirt and tears left out). Under each mask the cloth becomes the colour wanted, times its own
// brightness against the cloth's mean (from the build), so the painted shading, weave and wear stay and the colour
// is the palette's on average. No mask (the living): the map's own colours.
//
// Detail (docs/textures.md, docs/characters.md "Detail on bodies"): the model's detail map
// (art/scripts/build_detail_maps.py: red cloth, green shoe) says where the kit's cloth and rubber layers go, laid by
// the model's own atlas UV (it moves with the skin: nothing swims), repeated `detail` times a UV unit, faded out
// between BODY_DETAIL_FROM and BODY_DETAIL_TO metres from the eye (past that the painted map is all a body needs).
// Skin, hair, eyes and teeth are never in the map: they stay plain.
//
// Plain material values (one material per model file and colour pair, shared by everyone wearing them): no
// instance uniforms. The texture array is the kit's global: make the material after Surfaces.Ensure().

#include "res://assets/shaders/surface_detail.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D mask_tex : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform sampler2D detail_tex : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform vec3 top_colour = vec3(0.5);     // linear
uniform vec3 bottom_colour = vec3(0.5);
uniform float top_lum = 0.1;             // the cloth's mean linear luminance in the map
uniform float bottom_lum = 0.1;
uniform float roughness = 0.85;
/// x, y: the cloth's and the shoe's repeats a UV unit; z, w: their layers (Surfaces.Layer).
uniform vec4 detail = vec4(0.0);

const float BODY_DETAIL_FROM = 2.0;
const float BODY_DETAIL_TO = 5.0;
/// How much of the layer's contrast a body takes (the painted map has its own weave and shading).
const float BODY_DETAIL_STRENGTH = 1.0;

void fragment() {
	vec3 c = texture(albedo_tex, UV).rgb;
	vec2 m = texture(mask_tex, UV).rg;
	float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
	c = mix(c, top_colour * (lum / top_lum), m.r);
	c = mix(c, bottom_colour * (lum / bottom_lum), m.g);
	vec2 k = texture(detail_tex, UV).rg * (BODY_DETAIL_STRENGTH * (1.0 - smoothstep(BODY_DETAIL_FROM, BODY_DETAIL_TO, length(VERTEX))));
	c *= mix(vec3(1.0), surface_sample(UV * detail.x, detail.z), k.x);
	c *= mix(vec3(1.0), surface_sample(UV * detail.y, detail.w), k.y);
	ALBEDO = clamp(c, vec3(0.0), vec3(1.0));
	ROUGHNESS = roughness;
}
