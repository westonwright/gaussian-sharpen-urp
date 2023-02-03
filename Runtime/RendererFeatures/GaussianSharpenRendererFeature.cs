using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static UnityEngine.XR.XRDisplaySubsystem;

[Serializable]
class GaussianSharpenSettings
{
    public GaussianSharpenSettings() { }
    public GaussianSharpenSettings(GaussianSharpenSettings other) 
    {
        _Amount = other._Amount;
        _Threshold = other._Threshold;
        _ThresholdRange = other._ThresholdRange;
        _Diameter = other._Diameter;
        _Detail = other._Detail;

    }
    [SerializeField, Range(0, 10)]
    float _Amount = 1f;
    public float Amount
    {
        get => _Amount;
        set => _Amount = Mathf.Abs(value); 
    }
    [SerializeField, Range(0, 1)]
    float _Threshold = 0;
    public float Threshold
    {
        get => _Threshold;
        set => _Threshold = Mathf.Clamp(value, 0.0f, 1.0f);
    }
    [SerializeField, Range(0, 1)]
    float _ThresholdRange = .1f;
    public float ThresholdRange
    {
        get => _ThresholdRange;
        set => _ThresholdRange = Mathf.Clamp(value, 0.0f, 1.0f);
    }
    [SerializeField, Range(2, 12)]
    int _Diameter = 2;
    public int Diameter
    {
        get => _Diameter;
        set => _Diameter = Mathf.Clamp(2, 64, value); // if set too high could cause problems
    }
    [SerializeField, Range(.01f, 10)]
    float _Detail = 2;
    public float Detail
    {
        get => _Detail;
        set => _Detail = Mathf.Max(.01f, value);
    }
    [SerializeField]
    private RenderPassEvent _RenderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    public RenderPassEvent RenderPassEvent
    {
        get => _RenderPassEvent;
        set => _RenderPassEvent = value;
    }
    [SerializeField]
    private string _ProfilerTag = "Sharpen Renderer Feature";
    public string ProfilerTag
    {
        get => _ProfilerTag;
        set => _ProfilerTag = value;
    }
}
class GaussianSharpenRendererFeature : ScriptableRendererFeature
{
    // Serialized Fields
    [SerializeField, HideInInspector]
    private Shader m_SharpenShader;
    [SerializeField]
    private GaussianSharpenSettings m_Settings = new GaussianSharpenSettings();
    [SerializeField]
    private CameraType m_CameraType = CameraType.SceneView | CameraType.Game;

    // Private Fields
    private GaussianSharpenPass m_SharpenPass = null;
    private bool m_Initialized = false;
    private Material m_SharpenMaterial;

    // Constants
    private const string k_ShaderPath = "Shaders/";
    private const string k_SharpenShaderName = "GaussianSharpen";

    public GaussianSharpenSettings GetSettings()
    {
        return new GaussianSharpenSettings(m_Settings);
    }
    public void SetSettings(GaussianSharpenSettings settings)
    {
        m_Settings = settings;
    }

    public override void Create()
    {
        if (!RendererFeatureHelper.ValidUniversalPipeline(GraphicsSettings.defaultRenderPipeline, true, false)) return;
        
        m_Initialized = Initialize();

        if (m_Initialized)
            if (m_SharpenPass == null)
                m_SharpenPass = new GaussianSharpenPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!m_Initialized) return;

        if (!RendererFeatureHelper.CameraTypeMatches(m_CameraType, renderingData.cameraData.cameraType)) return;

        bool shouldAdd = m_SharpenPass.Setup(m_Settings, renderer, m_SharpenMaterial);
        if (shouldAdd)
        {
            renderer.EnqueuePass(m_SharpenPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        m_SharpenPass.Dispose();
        RendererFeatureHelper.DisposeMaterial(ref m_SharpenMaterial);
        base.Dispose(disposing);
    }

    private bool Initialize()
    {
        if (!RendererFeatureHelper.LoadShader(ref m_SharpenShader, k_ShaderPath, k_SharpenShaderName)) return false;
        if (!RendererFeatureHelper.GetMaterial(m_SharpenShader, ref m_SharpenMaterial)) return false;
        return true;
    }


    class GaussianSharpenPass : ScriptableRenderPass
    {
        // Private Variables
        private Material m_SharpenMaterial;
        RenderTargetIdentifier m_TempTextureTarget;
        private ProfilingSampler m_ProfilingSampler = null;
        private ScriptableRenderer m_Renderer = null;
        private GaussianSharpenSettings m_CurrentSettings = new GaussianSharpenSettings();

        // Constants
        private const string k_PassProfilerTag = "Gaussian Sharpen Pass";

        // Statics
        private static readonly int s_TempTextureID = Shader.PropertyToID("_Sharpen_TempTex");

        public GaussianSharpenPass() { }

        public bool Setup(GaussianSharpenSettings settings, ScriptableRenderer renderer, Material sharpenMaterial)
        {
            m_CurrentSettings = settings;
            m_Renderer = renderer;
            m_SharpenMaterial = sharpenMaterial;

            m_ProfilingSampler = new ProfilingSampler(k_PassProfilerTag);
            renderPassEvent = m_CurrentSettings.RenderPassEvent;
            ConfigureInput(ScriptableRenderPassInput.Color);

            if (m_SharpenMaterial == null) return false;
            return true;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        // called each frame before Execute, use it to set up things the pass will need
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cmd.GetTemporaryRT(s_TempTextureID, cameraTextureDescriptor);
            m_TempTextureTarget = new RenderTargetIdentifier(s_TempTextureID);
            ConfigureTarget(m_TempTextureTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // fetch a command buffer to use
            CommandBuffer cmd = CommandBufferPool.Get(m_CurrentSettings.ProfilerTag);
            using(new ProfilingScope(cmd, m_ProfilingSampler))
            {
                m_SharpenMaterial.SetFloat("_Amount", m_CurrentSettings.Amount);
                m_SharpenMaterial.SetFloat("_Threshold", m_CurrentSettings.Threshold);
                m_SharpenMaterial.SetFloat("_ThresholdRange", m_CurrentSettings.ThresholdRange);
                m_SharpenMaterial.SetInt("_Diameter", m_CurrentSettings.Diameter);
                m_SharpenMaterial.SetFloat("_Detail", m_CurrentSettings.Detail);

                // where the render pass does its work
                cmd.Blit(m_Renderer.cameraColorTarget, m_TempTextureTarget, m_SharpenMaterial, 0);

                // then blit back into color target 
                cmd.Blit(m_TempTextureTarget, m_Renderer.cameraColorTarget);
            }

            // don't forget to tell ScriptableRenderContext to actually execute the commands
            context.ExecuteCommandBuffer(cmd);

            // tidy up after ourselves
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Release Temporary RT here
            cmd.ReleaseTemporaryRT(s_TempTextureID);
        }

        public void Dispose()
        {
            // Dispose of buffers here
            // this pass doesnt have any buffers
        }
    }
}
