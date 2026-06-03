using UnityEngine;
using UnityEngine.InputSystem;

public class ExplosionManager : MonoBehaviour
{
    [SerializeField] private ComputeShader fluidSimCompute;
    [SerializeField] private int resolution = 64;
    [SerializeField] private float simScale = 1.0f;
    [SerializeField] private float injectRadius = 1.5f;
    [SerializeField] private Transform injectionPoint = null;

    private RenderTexture volumeTexture;
    private Material rayMarchMaterial;

    private int initKernel;
    private int injectKernel;
    private int simulateKernel;

    void Start()
    {
        volumeTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf);
        volumeTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        volumeTexture.volumeDepth = resolution;
        volumeTexture.enableRandomWrite = true;
        volumeTexture.Create();

        initKernel = fluidSimCompute.FindKernel("Initialize");
        injectKernel = fluidSimCompute.FindKernel("InjectDensity");
        simulateKernel = fluidSimCompute.FindKernel("Simulate");

        // Set Parameters
        fluidSimCompute.SetInt("Resolution", resolution);

        // Bind texture
        fluidSimCompute.SetTexture(initKernel, "VolumeTexture", volumeTexture);
        fluidSimCompute.SetTexture(injectKernel, "VolumeTexture", volumeTexture);
        fluidSimCompute.SetTexture(simulateKernel, "VolumeTexture", volumeTexture);

        rayMarchMaterial = GetComponent<Renderer>().material;
        rayMarchMaterial.SetTexture("_VolumeTex", volumeTexture);

        // Clear memory
        // TODO // Poll the thread group size instead
        fluidSimCompute.Dispatch(initKernel, resolution / 8, resolution / 8, resolution / 8);

    }

    void Update()
    {
        Bounds bounds = GetComponent<Renderer>().bounds;

        fluidSimCompute.SetFloat("Time", Time.time);
        fluidSimCompute.SetFloat("DeltaTime", Time.deltaTime);
        fluidSimCompute.SetFloat("SimScale", simScale);
        fluidSimCompute.SetVector("BoundsMin", bounds.min);
        fluidSimCompute.SetVector("BoundsSize", bounds.size);

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Vector3 injectPos = injectionPoint != null ? injectionPoint.position : transform.position;

            fluidSimCompute.SetVector("InjectWorldPos", injectPos);
            fluidSimCompute.SetFloat("InjectRadius", injectRadius);

            // TODO // Poll the thread group size instead
            fluidSimCompute.Dispatch(injectKernel, resolution / 8, resolution / 8, resolution /8);
        }

        // TODO // Poll the thread group size instead
        fluidSimCompute.Dispatch(simulateKernel, resolution / 8, resolution / 8, resolution / 8);
    }

    void OnDestroy()
    {
        if (volumeTexture != null) volumeTexture.Release();
    }
}
