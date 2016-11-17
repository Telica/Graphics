namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    //@TODO: We should continously move these values
    // into the engine when we can see them being generally useful
    [RequireComponent(typeof(Light))]
    public class AdditionalLightData : MonoBehaviour
    {
        public const int DefaultShadowResolution = 512;

        public int shadowResolution = DefaultShadowResolution;

        public static int GetShadowResolution(AdditionalLightData lightData)
        {
            if (lightData != null)
                return lightData.shadowResolution;
            else
                return DefaultShadowResolution;
        }

        [RangeAttribute(0.0F, 100.0F)]
        private float m_innerSpotPercent = 0.0F;

        public float GetInnerSpotPercent01()
        {
            return Mathf.Clamp(m_innerSpotPercent, 0.0f, 100.0f) / 100.0f;
        }

        [RangeAttribute(0.0F, 1.0F)]
        public float shadowDimmer = 1.0f;

        public bool affectDiffuse = true;
        public bool affectSpecular = true;

        // Area Light Hack
        public bool treatAsAreaLight = false;
        public bool isDoubleSided    = false;

        [RangeAttribute(0.0f, 20.0f)]
        public float areaLightLength = 0.0f;

        [RangeAttribute(0.0f, 20.0f)]
        public float areaLightWidth = 0.0f;
    }
}
