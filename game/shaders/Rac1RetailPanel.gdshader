shader_type canvas_item;
render_mode unshaded, blend_disabled;

// R&C1 native FUN_00223e28 layer order, composited over the projected
// screen region. GS ALPHA=0x44 is source-over. PS2 CLUT alpha 0..128 has
// already been expanded to 0..255 by Ps2Texture.DecodePaletted8.
uniform sampler2D scene_tex : hint_screen_texture, filter_nearest;
uniform sampler2D base_frame : source_color, filter_nearest, repeat_disable;
uniform sampler2D streak_tex : source_color, filter_nearest, repeat_enable;
uniform sampler2D surface_tex : source_color, filter_nearest, repeat_enable;
uniform sampler2D flash_tex : source_color, filter_nearest, repeat_enable;

uniform vec2 projected_size = vec2(365.0, 64.0);
uniform float flash_fix = 0.0;
uniform vec2 flash_uv_start = vec2(0.0);
uniform bool flash_enabled = false;

void fragment() {
    vec2 extent = max(projected_size, vec2(1.0));
    vec2 pixel = UV * extent;
    vec3 result = texture(scene_tex, SCREEN_UV).rgb;

    // Anim E99E frame 7, source-over (native ALPHA=0x44).
    vec4 base = texture(base_frame, UV);
    result = mix(result, base.rgb, base.a);

    // Effect 0x1A, native additive ALPHA=0x68 and FIX=0..128.
    // Transparent texels are suppressed by a texture-alpha gate.
    if (flash_enabled && flash_fix > 0.0) {
        vec2 flash_uv = (flash_uv_start + pixel) / vec2(textureSize(flash_tex, 0));
        vec4 flash = texture(flash_tex, flash_uv);
        if (flash.a > 0.001) {
            result = clamp(result + flash.rgb * flash_fix, vec3(0.0), vec3(1.0));
        }
    }

    // Effect 0x1C, UV height=(height*3)>>1, tint RGBA=0x50606060:
    // RGB 96/128 and alpha 80/128, source-over.
    vec2 streak_uv = vec2(pixel.x, pixel.y * 1.5) /
        vec2(textureSize(streak_tex, 0));
    vec4 streak = texture(streak_tex, streak_uv);
    result = mix(result, streak.rgb * 0.75, streak.a * 0.625);

    // Effect 0x19: source rectangle (1,1,62,62), inset destination;
    // native width/height adjustment -2 below 0x4c, -1 below 0x97.
    float dw = extent.x >= 151.0 ? 0.0 : (extent.x >= 76.0 ? -1.0 : -2.0);
    float dh = extent.y >= 151.0 ? 0.0 : (extent.y >= 76.0 ? -1.0 : -2.0);
    vec2 inner = (pixel - vec2(1.0)) / max(extent + vec2(dw, dh), vec2(1.0));
    if (all(greaterThanEqual(inner, vec2(0.0))) &&
        all(lessThan(inner, vec2(1.0)))) {
        vec2 surface_uv = (vec2(1.0) + inner * 62.0) /
            vec2(textureSize(surface_tex, 0));
        vec4 surface = texture(surface_tex, surface_uv);
        result = mix(result, surface.rgb, surface.a);
    }

    COLOR = vec4(result, 1.0);
}
