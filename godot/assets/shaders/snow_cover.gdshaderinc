// Snow on a surface, from the global parameter WeatherLayer sets (godot/World3D/Weather.cs): 0 bare, 1 deep snow.
// In a spatial shader:  #include "res://assets/shaders/snow_cover.gdshaderinc"   then, in fragment():
//     ALBEDO = snowy(ALBEDO, (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
// Whitens what faces up (roofs, the tops of trees) as the snow deepens; anything else is left as it was.
// `weather_wet` (0 dry .. 1 raining) is there too: wetter(ALBEDO) darkens a surface a little.
global uniform float snow_cover;
global uniform float weather_wet;

vec3 snowy(vec3 albedo, vec3 world_normal) {
	float up = smoothstep(0.35, 0.8, world_normal.y);
	return mix(albedo, vec3(0.93, 0.95, 1.0), clamp(snow_cover * up, 0.0, 1.0));
}

vec3 wetter(vec3 albedo) {
	return albedo * (1.0 - 0.25 * weather_wet);
}
