using UnityEngine;
using UnityEngine.InputSystem;

public class ExplosionManager : MonoBehaviour
{
    [SerializeField] private ComputeShader fluidSimCompute;
    [SerializeField] private int resolution = 64;
    [SerializeField] private float simScale = 1.0f;
    [SerializeField] private float injectRadius = 1.5f;
    [SerializeField] private Transform injectionPoint = null;

    private DoubleBuffer<RenderTexture> velocityTexture;
    private DoubleBuffer<RenderTexture> divergenceTexture;
    private DoubleBuffer<RenderTexture> smokePropTexture;
    private Material rayMarchMaterial;

    private int initKernel;
    private int divergenceKernel;
    private int stepKernel;

    private int threadGroups;

    void Start()
    {
        initKernel = fluidSimCompute.FindKernel("Init");
        divergenceKernel = fluidSimCompute.FindKernel("ComputeDivergence");
        stepKernel = fluidSimCompute.FindKernel("Step");
        fluidSimCompute.SetInt("Resolution", resolution);

        uint groupSize;
        fluidSimCompute.GetKernelThreadGroupSizes(initKernel, out groupSize, out _, out _);
        threadGroups = resolution / (int)groupSize;

        velocityTexture = new(() => CreateVolume());
        divergenceTexture = new(() => CreateVolume(RenderTextureFormat.RHalf));
        smokePropTexture = new(() => CreateVolume());
        for (int i = 0; i < 2; ++i)
        {
            fluidSimCompute.SetTexture(initKernel, "VelocityWrite", velocityTexture.WriteBuffer);
            fluidSimCompute.SetTexture(initKernel, "DivergenceWrite", divergenceTexture.WriteBuffer);
            fluidSimCompute.SetTexture(initKernel, "SmokePropWrite", smokePropTexture.WriteBuffer);
            fluidSimCompute.Dispatch(initKernel, threadGroups, threadGroups, threadGroups);

            smokePropTexture.SwapBuffers();
        }

        rayMarchMaterial = GetComponent<Renderer>().material;
    }

    RenderTexture CreateVolume(RenderTextureFormat format = RenderTextureFormat.ARGBHalf)
    {
        RenderTexture rt = new RenderTexture(resolution, resolution, 0, format);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rt.volumeDepth = resolution;
        rt.enableRandomWrite = true;
        rt.filterMode = FilterMode.Trilinear;
        rt.Create();
        return rt;
    }

    void Update()
    {
        Bounds bounds = GetComponent<Renderer>().bounds;

        // Global parameters
        fluidSimCompute.SetFloat("Time", Time.time);
        fluidSimCompute.SetFloat("DeltaTime", Time.deltaTime);
        fluidSimCompute.SetFloat("SimScale", simScale);
        fluidSimCompute.SetVector("BoundsMin", bounds.min);
        fluidSimCompute.SetVector("BoundsSize", bounds.size);

        // Check if we should inject
        bool spacePressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
        fluidSimCompute.SetBool("IsInjecting", spacePressed);

        // Injection parameters
        Vector3 injectPos = injectionPoint != null ? injectionPoint.position : transform.position;
        fluidSimCompute.SetVector("InjectWorldPos", injectPos);
        fluidSimCompute.SetFloat("InjectRadius", injectRadius);

        // Calculate divergence
        fluidSimCompute.SetTexture(divergenceKernel, "VelocityRead", velocityTexture.ReadBuffer);
        fluidSimCompute.SetTexture(divergenceKernel, "DivergenceWrite", divergenceTexture.WriteBuffer);

        fluidSimCompute.Dispatch(divergenceKernel, threadGroups, threadGroups, threadGroups);
        divergenceTexture.SwapBuffers();

        // Run simulation step
        fluidSimCompute.SetTexture(stepKernel, "SmokePropRead", smokePropTexture.ReadBuffer);
        fluidSimCompute.SetTexture(stepKernel, "SmokePropWrite", smokePropTexture.WriteBuffer);

        fluidSimCompute.SetTexture(stepKernel, "VelocityRead", velocityTexture.ReadBuffer);
        fluidSimCompute.SetTexture(stepKernel, "VelocityWrite", velocityTexture.WriteBuffer);

        fluidSimCompute.Dispatch(stepKernel, threadGroups, threadGroups, threadGroups);
        smokePropTexture.SwapBuffers();
        velocityTexture.SwapBuffers();

        // Share result with ray march material for rendering
        rayMarchMaterial.SetTexture("_VolumeTex", smokePropTexture.ReadBuffer);
    }

    void OnDestroy()
    {
        smokePropTexture.ForEach(t => {if (t != null) t.Release();});
        velocityTexture.ForEach(t => {if (t != null) t.Release();});
    }
}
