using UnityEngine;
using System.Collections.Generic;

public class ToggleMeshRenderers : MonoBehaviour
{
    [SerializeField] private MeshRenderer[] meshRenderers;

    // For shaders that expose emission as _EmissionColor (URP Lit) or _Emissive (Autodesk Interactive)
    private struct EmissionInfo
    {
        public Material Mat;
        public string ColorProp;
        public string ToggleProp;
        public Color OnColor;
    }

    private readonly List<EmissionInfo> emissionMaterials = new List<EmissionInfo>();
    private bool isEmissionOn = false;

    public void InitializeEmissionState()
    {
        CacheMaterials();
        isEmissionOn = false;
        SetEmissionState(false);
    }

    public void ToggleEmissionStates()
    {
        CacheMaterials();
        isEmissionOn = !isEmissionOn;
        SetEmissionState(isEmissionOn);
    }

    private void CacheMaterials()
    {
        if (emissionMaterials.Count > 0) return;

        foreach (MeshRenderer renderer in meshRenderers)
        {
            // Instantiate per-renderer materials so we don't edit shared assets
            var mats = renderer.materials;

            foreach (var mat in mats)
            {
                string colorProp = null;
                string toggleProp = null;

                if (mat.HasProperty("_Emissive"))
                {
                    colorProp = "_Emissive";          // Autodesk Interactive
                }
                else if (mat.HasProperty("_EmissionColor"))
                {
                    colorProp = "_EmissionColor";     // URP Lit / standard emission
                }

                if (!string.IsNullOrEmpty(colorProp))
                {
                    if (mat.HasProperty("_UseEmissiveMap"))
                        toggleProp = "_UseEmissiveMap"; // Autodesk Interactive uses this float toggle

                    var info = new EmissionInfo
                    {
                        Mat = mat,
                        ColorProp = colorProp,
                        ToggleProp = toggleProp,
                        OnColor = mat.GetColor(colorProp)
                    };
                    emissionMaterials.Add(info);
                }
            }

            renderer.materials = mats;
        }
    }

    private void SetEmissionState(bool state)
    {
        foreach (var info in emissionMaterials)
        {
            if (state)
            {
                info.Mat.SetColor(info.ColorProp, info.OnColor);
                if (!string.IsNullOrEmpty(info.ToggleProp))
                    info.Mat.SetFloat(info.ToggleProp, 1f);
                info.Mat.EnableKeyword("_EMISSION");
                info.Mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                info.Mat.SetColor(info.ColorProp, Color.black);
                if (!string.IsNullOrEmpty(info.ToggleProp))
                    info.Mat.SetFloat(info.ToggleProp, 0f);
                info.Mat.DisableKeyword("_EMISSION");
                info.Mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }
    }
}
