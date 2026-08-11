shader_type canvas_item;
render_mode unshaded;

// Port del CrtFilter.kt: scanlines, separación RGB y parpadeo sutil.
uniform float scanline_alpha : hint_range(0.0, 1.0) = 0.22;
uniform float rgb_stripe_alpha : hint_range(0.0, 1.0) = 0.08;
uniform float flicker_strength : hint_range(0.0, 1.0) = 0.035;

void fragment() {
	float scanline = mod(floor(FRAGCOORD.y), 2.0);
	float channel = mod(floor(FRAGCOORD.x), 3.0);
	vec3 stripes = channel < 1.0 ? vec3(1.0, 0.0, 0.0) : (channel < 2.0 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0));
	float flicker = fract(sin(TIME * 93.17) * 43758.5453) * flicker_strength;
	vec3 color = mix(vec3(0.0), stripes, rgb_stripe_alpha);
	float alpha = rgb_stripe_alpha + scanline * scanline_alpha + flicker;
	COLOR = vec4(color, alpha);
}
