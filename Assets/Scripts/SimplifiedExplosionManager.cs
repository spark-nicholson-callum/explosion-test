using UnityEngine;
using UnityEngine.InputSystem;

public class ExplosionManager : MonoBehaviour
{
    public ComputeShader fluidSimCompute;
    public int resolution = 64;

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

        fluidSimCompute.SetInt("Resolution", resolution);

        // 1. Clear the garbage memory out of the texture
        fluidSimCompute.SetTexture(initKernel, "VolumeTexture", volumeTexture);
        fluidSimCompute.Dispatch(initKernel, resolution / 8, resolution / 8, resolution / 8);

        // 2. Bind to other kernels
        fluidSimCompute.SetTexture(injectKernel, "VolumeTexture", volumeTexture);
        fluidSimCompute.SetTexture(simulateKernel, "VolumeTexture", volumeTexture);

        rayMarchMaterial = GetComponent<Renderer>().material;
        rayMarchMaterial.SetTexture("_VolumeTex", volumeTexture);
    }

    void Update()
    {
        fluidSimCompute.SetFloat("Time", Time.time);
        fluidSimCompute.SetFloat("DeltaTime", Time.deltaTime);

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            fluidSimCompute.Dispatch(injectKernel, resolution / 8, resolution / 8, resolution /8);
        }

        fluidSimCompute.Dispatch(simulateKernel, resolution / 8, resolution / 8, resolution / 8);
    }

    void OnDestroy()
    {
        if (volumeTexture != null) volumeTexture.Release();
    }
}
