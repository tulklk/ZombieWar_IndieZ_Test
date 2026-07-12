Shader "Hidden/LowHealthScreenEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SaturationLoss;
            float _VignetteIntensity;
            fixed4 _VignetteColor;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Desaturate toward luminance (Rec. 601 weights), cheap single-tap grayscale.
                float luminance = dot(col.rgb, fixed3(0.299, 0.587, 0.114));
                col.rgb = lerp(col.rgb, luminance.xxx, saturate(_SaturationLoss));

                // Soft radial vignette, darkens/tints the corners only.
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 1.4142136;
                float vignette = saturate((dist - 0.35) * _VignetteIntensity * 2.5);
                col.rgb = lerp(col.rgb, _VignetteColor.rgb, vignette * _VignetteIntensity);

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
