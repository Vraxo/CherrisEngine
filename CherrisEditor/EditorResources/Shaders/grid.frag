#version 330 core
in vec3 vWorldPos;
in vec3 vToCamera;

out vec4 FragColor;

void main()
{
    float dist = length(vToCamera);
    
    float spacing = 1.0;
    if (dist > 200.0) spacing = 10.0;
    else if (dist > 50.0) spacing = 5.0;

    vec2 coord = vWorldPos.xz / spacing;
    
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / fwidth(coord);
    float line = min(grid.x, grid.y);
    
    vec2 coord_major = vWorldPos.xz / (spacing * 10.0);
    vec2 grid_major = abs(fract(coord_major - 0.5) - 0.5) / fwidth(coord_major);
    float line_major = min(grid_major.x, grid_major.y);
    
    float axis_width = 1.5;
    vec2 axis_d = fwidth(vWorldPos.xz);
    float x_axis_alpha = smoothstep(axis_width * axis_d.y, 0.0, abs(vWorldPos.z));
    float z_axis_alpha = smoothstep(axis_width * axis_d.x, 0.0, abs(vWorldPos.x));
    float axes_alpha = max(x_axis_alpha, z_axis_alpha);
    
    float fade_start = 250.0;
    float fade_end = 350.0;
    float fade = 1.0 - smoothstep(fade_start, fade_end, dist);
    
    float grid_alpha = 1.0 - min(line, 1.0);
    float grid_major_alpha = 1.0 - min(line_major, 1.0);
    
    float final_alpha = (max(grid_alpha, axes_alpha) + grid_major_alpha) * fade;
    
    if (final_alpha < 0.01)
        discard;
    
    vec3 color = vec3(0.3);
    color = mix(color, vec3(0.4), grid_major_alpha);
    color = mix(color, vec3(0.8, 0.2, 0.2), x_axis_alpha);
    color = mix(color, vec3(0.2, 0.3, 0.8), z_axis_alpha);

    FragColor = vec4(color, final_alpha);
}