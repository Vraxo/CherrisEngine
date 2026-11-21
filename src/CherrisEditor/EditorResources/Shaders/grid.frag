#version 410 core
in vec3 vWorldPos;
in vec3 vToCamera;

out vec4 FragColor;

// Calculates grid line intensity for a specific scale
float grid(vec2 pos, float scale) {
    vec2 coord = pos / scale;
    // fwidth provides anti-aliasing for the lines
    vec2 derivative = fwidth(coord);
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);
    return 1.0 - min(line, 1.0);
}

void main()
{
    float dist = length(vToCamera);

    // --- Configuration ---
    float smallScale = 1.0;
    float largeScale = 10.0;
    
    // Fade zones (in meters/units)
    // Small grid visible up to 50m
    float smallFadeStart = 20.0;
    float smallFadeEnd = 50.0;
    
    // Large grid visible up to 400m
    float largeFadeStart = 250.0;
    float largeFadeEnd = 400.0;

    // --- Grid Calculations ---
    
    // 1. Calculate the raw grid patterns
    float smallGrid = grid(vWorldPos.xz, smallScale);
    float largeGrid = grid(vWorldPos.xz, largeScale);
    
    // 2. Calculate visibility factors (0.0 to 1.0) based on distance
    float smallVis = 1.0 - smoothstep(smallFadeStart, smallFadeEnd, dist);
    float largeVis = 1.0 - smoothstep(largeFadeStart, largeFadeEnd, dist);
    
    // 3. Axes (X = Red, Z = Blue)
    float axisWidth = 1.5;
    vec2 derivative = fwidth(vWorldPos.xz);
    float xAxis = smoothstep(axisWidth * derivative.y, 0.0, abs(vWorldPos.z));
    float zAxis = smoothstep(axisWidth * derivative.x, 0.0, abs(vWorldPos.x));
    float axesVis = max(xAxis, zAxis);

    // --- Composition ---

    // Colors
    vec3 colorSmall = vec3(0.35); // Slightly lighter gray
    vec3 colorLarge = vec3(0.20); // Darker gray for major lines
    vec3 colorX = vec3(0.8, 0.2, 0.2);
    vec3 colorZ = vec3(0.2, 0.3, 0.8);

    // Combine grids
    // We dim the small grid (multiply by 0.5) so it doesn't overpower the scene
    float smallAlpha = smallGrid * smallVis * 0.5; 
    float largeAlpha = largeGrid * largeVis;
    
    // Determine final alpha and base color
    // Use max() to ensure lines don't double-add opacity weirdly
    float gridAlpha = max(largeAlpha, smallAlpha);
    
    // Determine color priority (Large > Small)
    vec3 gridColor = mix(colorSmall, colorLarge, largeAlpha > smallAlpha ? 1.0 : 0.0);

    // Apply Axes override
    if (axesVis > 0.0) {
        if (xAxis > zAxis) gridColor = colorX;
        else gridColor = colorZ;
        
        // Axes fade out with the large grid
        gridAlpha = max(gridAlpha, axesVis * largeVis);
    }

    if (gridAlpha < 0.01)
        discard;

    FragColor = vec4(gridColor, gridAlpha);
}