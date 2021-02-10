using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.HighDefinition
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct ShaderVariablesClouds
    {
        // Maximal ray marching distance
        public float _MaxRayMarchingDistance;
        // The highest altitude clouds can reach in meters
        public float _HighestCloudAltitude;
        // The lowest altitude clouds can reach in meters
        public float _LowestCloudAltitude;
        // Radius of the earth so that the dome falls exactly at the horizon
        public float _EarthRadius;

        // Stores (_HighestCloudAltitude + _EarthRadius)^2 and (_LowestCloudAltitude + _EarthRadius)^2
        public Vector2 _CloudRangeSquared;
        // Maximal primary steps that a ray can do
        public int _NumPrimarySteps;
        // Maximal number of light steps a ray can do
        public int _NumLightSteps;

        // Controls the tiling of the cloud map
        public Vector4 _CloudMapTiling;

        // Direction of the wind
        public Vector2 _WindDirection;
        // Displacement vector of the wind
        public Vector2 _WindVector;

        // Wind speed controllers
        public float _GlobalWindSpeed;
        public float _LargeWindSpeed;
        public float _MediumWindSpeed;
        public float _SmallWindSpeed;

        // Flag that tells us if we should apply the exposure to the sun light color (in case no directional is specified)
        public int _ExposureSunColor;
        // Color * intensity of the directional light
        public Vector3 _SunLightColor;

        // Direction to the sun
        public Vector3 _SunDirection;
        // Is the current sun a physically based one
        public int _PhysicallyBasedSun;

        // Scattering Tint
        public Vector4 _ScatteringTint;

        // Factor for the multi scattering
        public float _MultiScattering;
        // Defines how we blend the forward and backward HG function
        public float _ScatteringDirection;
        // Controls the strength of the powder effect intensity
        public float _PowderEffectIntensity;
        // NormalizationFactor
        public float _NormalizationFactor;

        // Maximal cloud distance
        public float _MaxCloudDistance;
        // Global multiplier to the density
        public float _DensityMultiplier;
        // Controls the amount of low frenquency noise
        public float _ShapeFactor;
        // Controls the forward eccentricity of the clouds
        public float _ErosionFactor;

        // Global offset applied to the cloud map
        public float _CloudMapOffset;
        // Maximal temporal accumulation
        public float _TemporalAccumulationFactor;
        // Frame index for the accumulation
        public int _AccumulationFrameIndex;
        // Index for which of the 4 local pixels should be evaluated
        public int _SubPixelIndex;

        // Resolution of the final size of the effect
        public Vector4 _FinalScreenSize;
        // Half/ Intermediate resolution
        public Vector4 _IntermediateScreenSize;
        // Quarter/Trace resolution
        public Vector4 _TraceScreenSize;
        // Resolution of the history buffer size
        public Vector2 _HistoryViewportSize;
        // Resolution of the history depth buffer
        public Vector2 _HistoryBufferSize;
        // MipOffset of the first depth mip
        public Vector2 _DepthMipOffset;

        // The resolution of the shadow cookie to fill
        public int _ShadowCookieResolution;
        // The size of the shadow region (meters)
        public float _ShadowRegionSize;

        [HLSLArray(7, typeof(Vector4))]
        public fixed float _AmbientProbeCoeffs[7 * 4];  // 3 bands of SH, packed, rescaled and convolved with the phase function

        // Right direction of the sun
        public Vector3 _SunRight;
        // Intensity of the volumetric clouds shadow
        public float _ShadowIntensity;

        // Up direction of the sun
        public Vector3 _SunUp;
        // Fallback intensity used when the shadow is not defined
        public float _ShadowFallbackValue;
    }

    public partial class HDRenderPipeline
    {
        // Intermediate values for ambient probe evaluation
        Vector4[] m_PackedCoeffsClouds;
        ZonalHarmonicsL2 m_PhaseZHClouds;

        // Cloud preset maps
        RTHandle[] m_VolumetricCloudsShadowTexture = new RTHandle[(int)VolumetricClouds.CloudShadowResolution.Count];
        Texture2D m_SparsePresetMap;
        Texture2D m_CloudyPresetMap;
        Texture2D m_OvercastPresetMap;
        Texture2D m_StormCloudsPresetMap;

        // The set of kernels that are required
        int m_CloudRenderKernel;
        int m_CloudReprojectKernel;
        int m_UpscaleAndCombineCloudsKernel;
        int m_ComputeShadowCloudsKernel;

        void InitializeVolumetricClouds()
        {
            // Allocate the buffers for ambient probe evaluation
            m_PackedCoeffsClouds = new Vector4[7];
            m_PhaseZHClouds = new ZonalHarmonicsL2();
            m_PhaseZHClouds.coeffs = new float[3];

            // Grab the kernels we need
            ComputeShader volumetricCloudsCS = m_Asset.renderPipelineResources.shaders.volumetricCloudsCS;
            m_CloudRenderKernel = volumetricCloudsCS.FindKernel("RenderClouds");
            m_CloudReprojectKernel = volumetricCloudsCS.FindKernel("ReprojectClouds");
            m_UpscaleAndCombineCloudsKernel = volumetricCloudsCS.FindKernel("UpscaleAndCombineClouds");
            m_ComputeShadowCloudsKernel = volumetricCloudsCS.FindKernel("ComputeVolumetricCloudsShadow");

            // Allocate all the texture initially
            AllocatePresetTextures();
        }

        void ReleaseVolumetricClouds()
        {
            for (int i = 0; i < (int)VolumetricClouds.CloudShadowResolution.Count; ++i)
            {
                RTHandles.Release(m_VolumetricCloudsShadowTexture[i]);
            }
        }

        void AllocatePresetTextures()
        {
            // Build our default cloud map
            m_SparsePresetMap = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { name = "Default Sparse Texture" };
            m_SparsePresetMap.SetPixel(0, 0, new Color(0.2f, 0.0f, 0.5429687f, 1.0f));
            m_SparsePresetMap.Apply();

            m_CloudyPresetMap = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { name = "Default Cloudy Texture" };
            m_CloudyPresetMap.SetPixel(0, 0, new Color(0.4f, 0.0f, 0.66f, 1.0f));
            m_CloudyPresetMap.Apply();

            m_OvercastPresetMap = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { name = "Default Overcast Texture" };
            m_OvercastPresetMap.SetPixel(0, 0, new Color(0.5f, 0.0f, 0.0625f, 1.0f));
            m_OvercastPresetMap.Apply();

            m_StormCloudsPresetMap = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { name = "Default Storm Texture" };
            m_StormCloudsPresetMap.SetPixel(0, 0, new Color(0.4f, 0.0f, 0.8f, 1.0f));
            m_StormCloudsPresetMap.Apply();
        }

        // Function that fills the buffer with the ambient probe values
        unsafe void SetPreconvolvedAmbientLightProbe(ref ShaderVariablesClouds cb, HDCamera hdCamera, VolumetricClouds settings)
        {
            SphericalHarmonicsL2 probeSH = SphericalHarmonicMath.UndoCosineRescaling(m_SkyManager.GetAmbientProbe(hdCamera));
            probeSH = SphericalHarmonicMath.RescaleCoefficients(probeSH, settings.ambientLightProbeDimmer.value);
            ZonalHarmonicsL2.GetCornetteShanksPhaseFunction(m_PhaseZHClouds, 0.0f);
            SphericalHarmonicsL2 finalSH = SphericalHarmonicMath.PremultiplyCoefficients(SphericalHarmonicMath.Convolve(probeSH, m_PhaseZHClouds));

            SphericalHarmonicMath.PackCoefficients(m_PackedCoeffsClouds, finalSH);
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 4; ++j)
                    cb._AmbientProbeCoeffs[i * 4 + j] = m_PackedCoeffsClouds[i][j];
        }

        // Allocation of the first history buffer
        static RTHandle VolumetricClouds0HistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            return rtHandleSystem.Alloc(Vector2.one * 0.5f, TextureXR.slices, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureXR.dimension,
                enableRandomWrite: true, useMipMap: false, autoGenerateMips: false,
                name: string.Format("{0}_CloudsHistory0Buffer{1}", viewName, frameIndex));
        }

        // Allocation of the second history buffer
        static RTHandle VolumetricClouds1HistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            return rtHandleSystem.Alloc(Vector2.one * 0.5f, TextureXR.slices, colorFormat: GraphicsFormat.R16_SFloat, dimension: TextureXR.dimension,
                enableRandomWrite: true, useMipMap: false, autoGenerateMips: false,
                name: string.Format("{0}_CloudsHistory1Buffer{1}", viewName, frameIndex));
        }

        // Functions to request the history buffers
        static RTHandle RequestCurrentVolumetricCloudsHistoryTexture0(HDCamera hdCamera)
        {
            return hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds0)
                ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds0,
                VolumetricClouds0HistoryBufferAllocatorFunction, 2);
        }

        static RTHandle RequestPreviousVolumetricCloudsHistoryTexture0(HDCamera hdCamera)
        {
            return hdCamera.GetPreviousFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds0)
                ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds0,
                VolumetricClouds0HistoryBufferAllocatorFunction, 2);
        }

        static RTHandle RequestCurrentVolumetricCloudsHistoryTexture1(HDCamera hdCamera)
        {
            return hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds1)
                ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds1,
                VolumetricClouds1HistoryBufferAllocatorFunction, 2);
        }

        static RTHandle RequestPreviousVolumetricCloudsHistoryTexture1(HDCamera hdCamera)
        {
            return hdCamera.GetPreviousFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds1)
                ?? hdCamera.AllocHistoryFrameRT((int)HDCameraFrameHistoryType.VolumetricClouds1,
                VolumetricClouds1HistoryBufferAllocatorFunction, 2);
        }

        // Function to evaluate if a camera should have volumetric clouds
        static bool HasVolumetricClouds(HDCamera hdCamera)
        {
            VolumetricClouds settings = hdCamera.volumeStack.GetComponent<VolumetricClouds>();
            // If the current volume does not enable the feature, quit right away.
            return settings.enable.value;
        }

        struct VolumetricCloudsParameters
        {
            // Resolution parameters
            public int traceWidth;
            public int traceHeight;
            public int intermediateWidth;
            public int intermediateHeight;
            public int finalWidth;
            public int finalHeight;
            public int viewCount;
            public Vector2Int previousViewportSize;

            // Static textures
            public Texture3D worley128RGBA;
            public Texture3D worley32RGB;
            public Texture cloudMapTexture;
            public Texture cloudLutTexture;
            public BlueNoise.DitheredTextureSet ditheredTextureSet;
            public Light sunLight;

            // Compute shader and kernels
            public ComputeShader volumetricCloudsCS;
            public int renderKernel;
            public int reprojectKernel;
            public int upscaleAndCombineKernel;
            public int shadowsKernel;

            // Cloud constant buffer buffer
            public ShaderVariablesClouds cloudsCB;
        }

        float Square(float x)
        {
            return x * x;
        }

        float ComputeNormalizationFactor(float earthRadius, float lowerCloudRadius)
        {
            return Mathf.Sqrt((earthRadius + lowerCloudRadius) * (earthRadius + lowerCloudRadius) - earthRadius * earthRadius);
        }

        void GetPresetCloudMapValues(VolumetricClouds.CloudPresets preset, out float densityMultiplier, out float shapeFactor, out float erosionFactor)
        {
            switch (preset)
            {
                case VolumetricClouds.CloudPresets.Sparse:
                {
                    densityMultiplier = 0.3f;
                    shapeFactor = 0.4f;
                    erosionFactor = 0.6f;
                    return;
                }
                case VolumetricClouds.CloudPresets.Cloudy:
                {
                    densityMultiplier = 0.2f;
                    shapeFactor = 0.6f;
                    erosionFactor = 0.6f;
                    return;
                }
                case VolumetricClouds.CloudPresets.Overcast:
                {
                    densityMultiplier = 0.05f;
                    shapeFactor = 0.1f;
                    erosionFactor = 0.0f;
                    return;
                }
                case VolumetricClouds.CloudPresets.StormClouds:
                {
                    densityMultiplier = 0.4f;
                    shapeFactor = 1.0f;
                    erosionFactor = 0.155f;
                    return;
                }
            }

            // Default unused values
            densityMultiplier = 0.6f;
            shapeFactor = 0.6f;
            erosionFactor = 0.6f;
        }

        // The earthRadius
        const float earthRadius = 6378100.0f;

        void UpdateShaderVariableslClouds(ref ShaderVariablesClouds cb, HDCamera hdCamera, VolumetricClouds settings, HDUtils.PackedMipChainInfo info, in VolumetricCloudsParameters parameters)
        {
            // Convert to kilometers
            cb._LowestCloudAltitude = settings.lowestCloudAltitude.value;
            cb._HighestCloudAltitude = settings.highestCloudAltitude.value;
            cb._EarthRadius = settings.earthRadiusMultiplier.value * earthRadius;
            cb._CloudRangeSquared.Set(Square(cb._LowestCloudAltitude + cb._EarthRadius), Square(cb._HighestCloudAltitude + cb._EarthRadius));

            cb._NumPrimarySteps = settings.numPrimarySteps.value;
            cb._NumLightSteps = settings.numLightSteps.value;
            cb._MaxRayMarchingDistance = Mathf.Min(1500.0f * cb._NumPrimarySteps, hdCamera.camera.farClipPlane);
            cb._CloudMapTiling = settings.cloudTiling.value;

            cb._ScatteringDirection = settings.scatteringDirection.value;
            cb._ScatteringTint = Color.white - settings.scatteringTint.value * 0.75f;
            cb._PowderEffectIntensity = settings.powderEffectIntensity.value;
            cb._NormalizationFactor = ComputeNormalizationFactor(cb._EarthRadius, (cb._LowestCloudAltitude + cb._HighestCloudAltitude) * 0.5f);

            // We need 16 samples per pixel and we are alternating between 4 pixels (16 x 4 = 64)
            int frameIndex = RayTracingFrameIndex(hdCamera, 64);
            cb._AccumulationFrameIndex = frameIndex / 4;
            cb._SubPixelIndex = frameIndex % 4;

            // PB Sun/Sky settings
            var visualEnvironment = hdCamera.volumeStack.GetComponent<VisualEnvironment>();
            cb._PhysicallyBasedSun =  visualEnvironment.skyType.value == (int)SkyType.PhysicallyBased ? 1 : 0;
            Light currentSun = GetCurrentSunLight();
            if (currentSun != null)
            {
                // Grab the target sun additional data
                HDAdditionalLightData additionalLightData;
                m_CurrentSunLight.TryGetComponent<HDAdditionalLightData>(out additionalLightData);
                cb._SunDirection = -currentSun.transform.forward;
                cb._SunRight = currentSun.transform.right;
                cb._SunUp = currentSun.transform.up;

                cb._SunLightColor = m_lightList.directionalLights[0].color;

                cb._ExposureSunColor = 1;
            }
            else
            {
                cb._SunDirection = Vector3.up;
                cb._SunRight = Vector3.right;
                cb._SunUp = Vector3.forward;

                cb._SunLightColor = Vector3.one;
                cb._ExposureSunColor = 0;
            }

            // Compute the theta angle for the wind direction
            float theta = settings.windRotation.value / 180.0f * Mathf.PI;
            cb._WindDirection = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
            cb._WindVector = cb._WindDirection * settings.globalWindSpeed.value * 2.0f * hdCamera.time;

            cb._GlobalWindSpeed = settings.globalWindSpeed.value;
            cb._LargeWindSpeed = settings.cloudMapWindSpeedMultiplier.value;
            cb._MediumWindSpeed = settings.shapeWindSpeedMultiplier.value;
            cb._SmallWindSpeed = settings.erosionWindSpeedMultiplier.value;

            cb._MultiScattering = 1.0f - settings.multiScattering.value * 0.8f;

            if (settings.cloudControl.value == VolumetricClouds.CloudControl.Simple && settings.cloudPresets.value != VolumetricClouds.CloudPresets.Manual)
            {
                GetPresetCloudMapValues(settings.cloudPresets.value, out cb._DensityMultiplier, out cb._ShapeFactor, out cb._ErosionFactor);
            }
            else
            {
                // The density multiplier is not used linearly
                cb._DensityMultiplier = settings.densityMultiplier.value * settings.densityMultiplier.value;
                cb._ShapeFactor = settings.shapeFactor.value;
                cb._ErosionFactor = settings.erosionFactor.value;
            }

            // If the sun has moved more than 2.0°, reduce significantly the history accumulation
            float sunAngleDifference = 0.0f;
            if (m_CurrentSunLightAdditionalLightData != null)
                sunAngleDifference = Quaternion.Angle(m_CurrentSunLightAdditionalLightData.previousTransform.rotation, m_CurrentSunLightAdditionalLightData.transform.localToWorldMatrix.rotation);
            float sunAttenuation = sunAngleDifference > 2.0f ? 0.5f : 1.0f;
            cb._TemporalAccumulationFactor = settings.temporalAccumulationFactor.value * sunAttenuation;

            cb._FinalScreenSize.Set((float)parameters.finalWidth, (float)parameters.finalHeight, 1.0f / (float)parameters.finalWidth, 1.0f / (float)parameters.finalHeight);
            cb._IntermediateScreenSize.Set((float)parameters.intermediateWidth, (float)parameters.intermediateHeight, 1.0f / (float)parameters.intermediateWidth, 1.0f / (float)parameters.intermediateHeight);
            cb._TraceScreenSize.Set((float)parameters.traceWidth, (float)parameters.traceHeight, 1.0f / (float)parameters.traceWidth, 1.0f / (float)parameters.traceHeight);

            cb._DepthMipOffset = new Vector2(info.mipLevelOffsets[1].x, info.mipLevelOffsets[1].y);

            float absoluteCloudHighest = cb._HighestCloudAltitude + cb._EarthRadius;
            cb._MaxCloudDistance = Mathf.Sqrt(absoluteCloudHighest * absoluteCloudHighest - cb._EarthRadius * cb._EarthRadius);

            // We should control this with controlling the quality presets
            cb._CloudMapOffset = 0;

            // Evaluate the ambient probe data
            SetPreconvolvedAmbientLightProbe(ref cb, hdCamera, settings);

            // Resolution of the cloud shadow
            cb._ShadowCookieResolution = (int)settings.shadowResolution.value;
            cb._ShadowRegionSize = settings.shadowSize.value * 0.5f;
            cb._ShadowIntensity = settings.shadowIntensity.value;
            cb._ShadowFallbackValue = settings.shadowFallbackValue.value;

            if (HasVolumetricShadows(settings))
            {
                Light sunLight = GetCurrentSunLight();
                sunLight.transform.position = new Vector3(0.0f, 0.0f, 0.0f);
                HDAdditionalLightData additionalLightData;
                sunLight.TryGetComponent<HDAdditionalLightData>(out additionalLightData);
                additionalLightData.shapeWidth = settings.shadowSize.value;
                additionalLightData.shapeHeight = settings.shadowSize.value;
            }
        }

        Texture2D GetPresetCloudMapTexture(VolumetricClouds.CloudPresets preset)
        {
            // Textures may become null if a new scene was loaded in the editor (and maybe other reasons).
            if (m_SparsePresetMap == null || Object.ReferenceEquals(m_SparsePresetMap, null))
                AllocatePresetTextures();

            switch (preset)
            {
                case VolumetricClouds.CloudPresets.Sparse:
                    return m_SparsePresetMap;
                case VolumetricClouds.CloudPresets.Cloudy:
                    return m_CloudyPresetMap;
                case VolumetricClouds.CloudPresets.Overcast:
                    return m_OvercastPresetMap;
                case VolumetricClouds.CloudPresets.StormClouds:
                    return m_StormCloudsPresetMap;
                case VolumetricClouds.CloudPresets.Manual:
                    return m_CloudyPresetMap;
            }
            return Texture2D.blackTexture;
        }

        VolumetricCloudsParameters PrepareVolumetricCloudsParameters(HDCamera hdCamera, VolumetricClouds settings, HDUtils.PackedMipChainInfo info)
        {
            VolumetricCloudsParameters parameters = new VolumetricCloudsParameters();
            // We need to make sure that the allocated size of the history buffers and the dispatch size are perfectly equal.
            // The ideal approach would be to have a function for that returns the converted size from a viewport and texture size.
            // but for now we do it like this.
            // Final resolution at which the effect should be exported
            parameters.finalWidth = hdCamera.actualWidth;
            parameters.finalHeight = hdCamera.actualHeight;
            // Intermediate resolution at which the effect is accumulated
            parameters.intermediateWidth = Mathf.RoundToInt(0.5f * hdCamera.actualWidth);
            parameters.intermediateHeight = Mathf.RoundToInt(0.5f * hdCamera.actualHeight);
            // Resolution at which the effect is traced
            parameters.traceWidth = Mathf.RoundToInt(0.25f * hdCamera.actualWidth);
            parameters.traceHeight = Mathf.RoundToInt(0.25f * hdCamera.actualHeight);
            parameters.viewCount = hdCamera.viewCount;
            parameters.previousViewportSize = hdCamera.historyRTHandleProperties.previousViewportSize;

            // Compute shader and kernels
            parameters.volumetricCloudsCS = m_Asset.renderPipelineResources.shaders.volumetricCloudsCS;
            parameters.renderKernel = m_CloudRenderKernel;
            parameters.reprojectKernel = m_CloudReprojectKernel;
            parameters.upscaleAndCombineKernel = m_UpscaleAndCombineCloudsKernel;
            parameters.shadowsKernel = m_ComputeShadowCloudsKernel;

            // Static textures
            if (settings.cloudControl.value == VolumetricClouds.CloudControl.Simple)
            {
                parameters.cloudMapTexture = GetPresetCloudMapTexture(settings.cloudPresets.value);
                parameters.cloudLutTexture = m_Asset.renderPipelineResources.textures.cloudLutRainAO;
            }
            else if (settings.cloudControl.value == VolumetricClouds.CloudControl.Advanced)
            {
                parameters.cloudMapTexture = settings.cloudMap.value != null ? settings.cloudMap.value : Texture2D.blackTexture;
                parameters.cloudLutTexture = m_Asset.renderPipelineResources.textures.cloudLutRainAO;
            }
            else
            {
                parameters.cloudMapTexture = settings.cloudMap.value != null ? settings.cloudMap.value : Texture2D.blackTexture;
                parameters.cloudLutTexture = settings.cloudLut.value != null ? settings.cloudLut.value : Texture2D.blackTexture;
            }

            parameters.worley128RGBA = m_Asset.renderPipelineResources.textures.worleyNoise128RGBA;
            parameters.worley32RGB = m_Asset.renderPipelineResources.textures.worleyNoise32RGB;
            BlueNoise blueNoise = GetBlueNoiseManager();
            parameters.ditheredTextureSet = blueNoise.DitheredTextureSet8SPP();
            parameters.sunLight = GetCurrentSunLight();

            // Update the constant buffer
            UpdateShaderVariableslClouds(ref parameters.cloudsCB, hdCamera, settings, info, parameters);

            return parameters;
        }

        static void TraceVolumetricClouds(CommandBuffer cmd, VolumetricCloudsParameters parameters,
            RTHandle colorBuffer, RTHandle depthPyramid, RTHandle historyDepthTexture, TextureHandle motionVectors, TextureHandle volumetricLightingTexture, TextureHandle scatteringFallbackTexture,
            RTHandle currentHistory0Buffer, RTHandle previousHistory0Buffer,
            RTHandle currentHistory1Buffer, RTHandle previousHistory1Buffer,
            RTHandle intermediateLightingBuffer, RTHandle intermediateDepthBuffer)
        {
            // Compute the number of tiles to evaluate
            int traceTX = (parameters.traceWidth + (8 - 1)) / 8;
            int traceTY = (parameters.traceHeight + (8 - 1)) / 8;

            // Bind the sampling textures
            BlueNoise.BindDitheredTextureSet(cmd, parameters.ditheredTextureSet);

            // We need to make sure that the allocated size of the history buffers and the dispatch size are perfectly equal.
            // The ideal approach would be to have a function for that returns the converted size from a viewport and texture size.
            // but for now we do it like this.
            Vector2Int previousViewportSize = previousHistory0Buffer.GetScaledSize(parameters.previousViewportSize);
            parameters.cloudsCB._HistoryViewportSize = new Vector2(previousViewportSize.x, previousViewportSize.y);
            parameters.cloudsCB._HistoryBufferSize = new Vector2(previousHistory0Buffer.rt.width, previousHistory0Buffer.rt.height);

            // Bind the constant buffer
            ConstantBuffer.Push(cmd, parameters.cloudsCB, parameters.volumetricCloudsCS, HDShaderIDs._ShaderVariablesClouds);

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.VolumetricCloudsTrace)))
            {
                CoreUtils.SetKeyword(cmd, "PHYSICALLY_BASED_SUN", parameters.cloudsCB._PhysicallyBasedSun == 1);

                // Ray-march the clouds for this frame
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._DepthTexture, depthPyramid);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._HistoryVolumetricClouds1Texture, previousHistory1Buffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._Worley128RGBA, parameters.worley128RGBA);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._Worley32RGB, parameters.worley32RGB);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._CloudMapTexture, parameters.cloudMapTexture);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._CloudLutTexture, parameters.cloudLutTexture);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._CloudsLightingTextureRW, intermediateLightingBuffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._CloudsDepthTextureRW, intermediateDepthBuffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._VBufferLighting, volumetricLightingTexture);
                if (parameters.cloudsCB._PhysicallyBasedSun == 0)
                {
                    cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._AirSingleScatteringTexture, scatteringFallbackTexture);
                    cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._AerosolSingleScatteringTexture, scatteringFallbackTexture);
                    cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.renderKernel, HDShaderIDs._MultipleScatteringTexture, scatteringFallbackTexture);
                }
                cmd.DispatchCompute(parameters.volumetricCloudsCS, parameters.renderKernel, traceTX, traceTY, parameters.viewCount);

                CoreUtils.SetKeyword(cmd, "PHYSICALLY_BASED_SUN", false);
            }

            // Compute the number of tiles to evaluate
            int intermediateTX = (parameters.intermediateWidth + (8 - 1)) / 8;
            int intermediateTY = (parameters.intermediateHeight + (8 - 1)) / 8;

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.VolumetricCloudsReproject)))
            {
                // Re-project the result from the previous frame
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._CloudsLightingTexture, intermediateLightingBuffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._CloudsDepthTexture, intermediateDepthBuffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._DepthTexture, depthPyramid);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._HistoryDepthTexture, historyDepthTexture);

                // History buffers
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._HistoryVolumetricClouds0Texture, previousHistory0Buffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._HistoryVolumetricClouds1Texture, previousHistory1Buffer);

                // Output textures
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._CloudsLightingTextureRW, currentHistory0Buffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.reprojectKernel, HDShaderIDs._CloudsAdditionalTextureRW, currentHistory1Buffer);

                // Re-project from the previous frame
                cmd.DispatchCompute(parameters.volumetricCloudsCS, parameters.reprojectKernel, intermediateTX, intermediateTY, parameters.viewCount);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.VolumetricCloudsUpscaleAndCombine)))
            {
                // Compute the final resolution parameters
                int finalTX = (parameters.finalWidth + (8 - 1)) / 8;
                int finalTY = (parameters.finalHeight + (8 - 1)) / 8;
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.upscaleAndCombineKernel, HDShaderIDs._DepthTexture, depthPyramid);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.upscaleAndCombineKernel, HDShaderIDs._VolumetricCloudsTexture, currentHistory0Buffer);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.upscaleAndCombineKernel, HDShaderIDs._CameraColorTextureRW, colorBuffer);
                cmd.DispatchCompute(parameters.volumetricCloudsCS, parameters.upscaleAndCombineKernel, finalTX, finalTY, parameters.viewCount);
            }
        }

        class VolumetricCloudsData
        {
            public VolumetricCloudsParameters parameters;
            public TextureHandle colorBuffer;
            public TextureHandle depthPyramid;
            public TextureHandle historyDepthPyramid;
            public TextureHandle motionVectors;
            public TextureHandle volumetricLighting;
            public TextureHandle scatteringFallbackTexture;
            public TextureHandle previousHistoryBuffer0;
            public TextureHandle currentHistoryBuffer0;
            public TextureHandle previousHistoryBuffer1;
            public TextureHandle currentHistoryBuffer1;
            public TextureHandle intermediateBuffer0;
            public TextureHandle intermediateBuffer1;
        }

        TextureHandle TraceVolumetricClouds(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle colorBuffer, TextureHandle depthPyramid, TextureHandle motionVectors, TextureHandle volumetricLighting, HDUtils.PackedMipChainInfo info)
        {
            using (var builder = renderGraph.AddRenderPass<VolumetricCloudsData>("Generating the rays for RTR", out var passData, ProfilingSampler.Get(HDProfileId.VolumetricClouds)))
            {
                builder.EnableAsyncCompute(false);
                VolumetricClouds settings = hdCamera.volumeStack.GetComponent<VolumetricClouds>();

                passData.parameters = PrepareVolumetricCloudsParameters(hdCamera, settings, info);
                passData.colorBuffer = builder.ReadTexture(builder.WriteTexture(colorBuffer));
                passData.depthPyramid = builder.ReadTexture(depthPyramid);
                RTHandle historyDepthPyramid = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.Depth1);
                passData.historyDepthPyramid = historyDepthPyramid != null ? renderGraph.ImportTexture(historyDepthPyramid) : renderGraph.defaultResources.blackTextureXR;
                passData.motionVectors = builder.ReadTexture(motionVectors);
                passData.volumetricLighting = builder.ReadTexture(volumetricLighting);
                passData.scatteringFallbackTexture = renderGraph.defaultResources.blackTexture3DXR;

                passData.currentHistoryBuffer0 = renderGraph.ImportTexture(RequestCurrentVolumetricCloudsHistoryTexture0(hdCamera));
                passData.previousHistoryBuffer0 = renderGraph.ImportTexture(RequestPreviousVolumetricCloudsHistoryTexture0(hdCamera));
                passData.currentHistoryBuffer1 = renderGraph.ImportTexture(RequestCurrentVolumetricCloudsHistoryTexture1(hdCamera));
                passData.previousHistoryBuffer1 = renderGraph.ImportTexture(RequestPreviousVolumetricCloudsHistoryTexture1(hdCamera));

                passData.intermediateBuffer0 = builder.CreateTransientTexture(new TextureDesc(Vector2.one, true, true)
                    { colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true, name = "Temporary Clouds Lighting Buffer" });
                passData.intermediateBuffer1 = builder.CreateTransientTexture(new TextureDesc(Vector2.one, true, true)
                    { colorFormat = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "Temporary Clouds Depth Buffer" });

                builder.SetRenderFunc(
                    (VolumetricCloudsData data, RenderGraphContext ctx) =>
                    {
                        TraceVolumetricClouds(ctx.cmd, data.parameters,
                            data.colorBuffer, data.depthPyramid, data.historyDepthPyramid, data.motionVectors, data.volumetricLighting, data.scatteringFallbackTexture,
                            data.currentHistoryBuffer0, data.previousHistoryBuffer0, data.currentHistoryBuffer1, data.previousHistoryBuffer1,
                            data.intermediateBuffer0, data.intermediateBuffer1);
                    });

                PushFullScreenDebugTexture(m_RenderGraph, passData.currentHistoryBuffer0, FullScreenDebugMode.VolumetricClouds);

                return passData.colorBuffer;
            }
        }

        void RenderVolumetricClouds(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle colorBuffer, TextureHandle depthPyramid, TextureHandle motionVector, TextureHandle volumetricLighting, HDUtils.PackedMipChainInfo info)
        {
            VolumetricClouds settings = hdCamera.volumeStack.GetComponent<VolumetricClouds>();
            // If the current volume does not enable the feature, quit right away.
            if (!settings.enable.value)
                return;

            TraceVolumetricClouds(renderGraph, hdCamera, colorBuffer, depthPyramid, motionVector, volumetricLighting, info);
        }

        static void TraceVolumetricCloudShadow(CommandBuffer cmd, VolumetricCloudsParameters parameters, RTHandle shadowTexture)
        {
            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.VolumetricCloudsShadow)))
            {
                // Compute the final resolution parameters
                int shadowTX = (parameters.cloudsCB._ShadowCookieResolution + (8 - 1)) / 8;
                int shadowTY = (parameters.cloudsCB._ShadowCookieResolution + (8 - 1)) / 8;
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.shadowsKernel, HDShaderIDs._CloudMapTexture, parameters.cloudMapTexture);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.shadowsKernel, HDShaderIDs._CloudLutTexture, parameters.cloudLutTexture);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.shadowsKernel, HDShaderIDs._Worley128RGBA, parameters.worley128RGBA);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.shadowsKernel, HDShaderIDs._Worley32RGB, parameters.worley32RGB);
                cmd.SetComputeTextureParam(parameters.volumetricCloudsCS, parameters.shadowsKernel, HDShaderIDs._VolumetricCloudsShadowRW, shadowTexture);
                cmd.DispatchCompute(parameters.volumetricCloudsCS, parameters.shadowsKernel, shadowTX, shadowTY, parameters.viewCount);

                // Apply the texture to the sun
                shadowTexture.rt.IncrementUpdateCount();
                parameters.sunLight.cookie = shadowTexture;
            }
        }

        class VolumetricCloudsShadowData
        {
            public VolumetricCloudsParameters parameters;
            public TextureHandle shadowTexture;
        }

        bool HasVolumetricShadows(in VolumetricClouds settings)
        {
            return (settings.enable.value && GetCurrentSunLight() != null && settings.shadow.value);
        }

        RTHandle RequestShadowTexture(in VolumetricClouds settings)
        {
            int shadowResolution = (int)settings.shadowResolution.value;
            int shadowResIndex = 0;
            switch (settings.shadowResolution.value)
            {
                case VolumetricClouds.CloudShadowResolution.VeryLow:
                    shadowResIndex = 0;
                    break;
                case VolumetricClouds.CloudShadowResolution.Low:
                    shadowResIndex = 1;
                    break;
                case VolumetricClouds.CloudShadowResolution.Medium:
                    shadowResIndex = 2;
                    break;
                case VolumetricClouds.CloudShadowResolution.High:
                    shadowResIndex = 3;
                    break;
            }

            if (m_VolumetricCloudsShadowTexture[shadowResIndex] == null)
            {
                m_VolumetricCloudsShadowTexture[shadowResIndex] = RTHandles.Alloc(shadowResolution, shadowResolution, 1, colorFormat: GraphicsFormat.B10G11R11_UFloatPack32,
                    enableRandomWrite: true, useDynamicScale: false, useMipMap: false, wrapMode: TextureWrapMode.Clamp, name: "Volumetric Clouds Shadow Texture");
            }

            return m_VolumetricCloudsShadowTexture[shadowResIndex];
        }

        void PreRenderVolumetricClouds(RenderGraph renderGraph, HDCamera hdCamera, HDUtils.PackedMipChainInfo info)
        {
            VolumetricClouds settings = hdCamera.volumeStack.GetComponent<VolumetricClouds>();

            // Make sure we need to compute the shadow
            if (!HasVolumetricShadows(settings))
            {
                // We need to make sure that none of the textures that the component owns is assigned to the light
                Light currentSun = GetCurrentSunLight();
                if (currentSun != null)
                {
                    for (int i = 0; i < (int)VolumetricClouds.CloudShadowResolution.Count; ++i)
                    {
                        if (currentSun.cookie == m_VolumetricCloudsShadowTexture[i])
                        {
                            currentSun.cookie = null;
                            break;
                        }
                    }
                }
                return;
            }

            // Make sure the shadow texture is the right size
            // TODO: Right now we can endup with a bunch of textures allocated which should be solved by an other PR.
            RTHandle currentHandle = RequestShadowTexture(settings);

            // Evaluate the shadow
            TextureHandle shadowHandle;
            using (var builder = renderGraph.AddRenderPass<VolumetricCloudsShadowData>("Volumetric cloud shadow", out var passData, ProfilingSampler.Get(HDProfileId.VolumetricCloudsShadow)))
            {
                builder.EnableAsyncCompute(false);

                passData.parameters = PrepareVolumetricCloudsParameters(hdCamera, settings, info);
                int shadowResolution = (int)settings.shadowResolution.value;
                passData.shadowTexture = builder.WriteTexture(renderGraph.ImportTexture(currentHandle));

                builder.SetRenderFunc(
                    (VolumetricCloudsShadowData data, RenderGraphContext ctx) =>
                    {
                        TraceVolumetricCloudShadow(ctx.cmd, data.parameters, data.shadowTexture);
                    });

                shadowHandle = passData.shadowTexture;
            }

            // Push the shadow
            PushFullScreenDebugTexture(m_RenderGraph, shadowHandle, FullScreenDebugMode.VolumetricCloudsShadow, xrTexture: false);
        }
    }
}
