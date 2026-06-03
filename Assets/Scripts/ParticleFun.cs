using UnityEngine;

public class ParticleFun : MonoBehaviour
{
    private Vector2 cursorPos;

    struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float life;
    }

    const int PARTICLE_SIZE = 7 * sizeof(float);

    [SerializeField] private int particleCount = 1000000;
    [SerializeField] private Material material;
    [SerializeField] private ComputeShader shader;

    int kernelId;
    ComputeBuffer particleBuffer;

    int groupSizeX;
    RenderParams rp;

    void Start()
    {
        Init();
    }

    void Init()
    {
        Particle[] particleArray = new Particle[particleCount];

        for (int i = 0; i < particleCount; ++i)
        {
            float x = Random.value * 2 - 1.0f;
            float y = Random.value * 2 - 1.0f;
            float z = Random.value * 2 - 1.0f;
            Vector3 xyz = new Vector3(x, y, z);
            xyz.Normalize();
            xyz *= Random.value * 5;

            particleArray[i].position = xyz;
            particleArray[i].velocity = Vector3.zero;
            particleArray[i].life = Random.value * 5.0f + 1.0f;
        }

        particleBuffer = new ComputeBuffer(particleCount, PARTICLE_SIZE);
        particleBuffer.SetData(particleArray);

        kernelId = shader.FindKernel("CSParticle");

        uint threadsX;
        shader.GetKernelThreadGroupSizes(kernelId, out threadsX, out _, out _);
        groupSizeX = Mathf.CeilToInt((float)particleCount / (float)threadsX);


        shader.SetBuffer(kernelId, "particleBuffer", particleBuffer);
        material.SetBuffer("particleBuffer", particleBuffer);

        rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one);
    }

    void Update()
    {
        float[] mousePosition = {cursorPos.x, cursorPos.y};

        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloats("mousePosition", mousePosition);

        shader.Dispatch(kernelId, groupSizeX, 1, 1);

        Graphics.RenderPrimitives(rp, MeshTopology.Points, 1, particleCount);
    }
}
