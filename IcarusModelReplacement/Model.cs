using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace IcarusModelReplacement;

internal sealed class Model : IDisposable
{
    public GameObject Root { get; }
    private readonly PackInfo info;
    private readonly Transform[] bones;
    private readonly Vector3[] rest, positions, rotations;
    private readonly MaterialPropertyBlock lighting = new MaterialPropertyBlock();
    private readonly SphericalHarmonicsL2[] probe = new SphericalHarmonicsL2[1];
    private Material material;
    private Mesh mesh;
    private Texture2D texture;
    private SkinnedMeshRenderer renderer;
    private static readonly int Ambient0 = Shader.PropertyToID("_Global_AmbientColor0");
    private static readonly int Ambient1 = Shader.PropertyToID("_Global_AmbientColor1");
    private static readonly int Ambient2 = Shader.PropertyToID("_Global_AmbientColor2");

    public Model(ModelPack pack, Transform parent, int layer)
    {
        var shader = Shader.Find("Standard");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("The game's Standard shader is unavailable.");
        info = pack.Info;
        bones = new Transform[info.Bones.Length];
        rest = new Vector3[bones.Length];
        positions = new Vector3[bones.Length + 1];
        rotations = new Vector3[bones.Length + 1];
        Root = new GameObject(info.Name) { layer = layer };
        Root.SetActive(false);
        Root.transform.SetParent(parent, false);
        Root.transform.localScale = Vector3.one * info.Scale;
        try
        {
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = info.Bones[i];
                var pivot = Vector(bone.Position);
                bones[i] = new GameObject(bone.Name) { layer = layer }.transform;
                bones[i].SetParent(bone.Parent < 0 ? Root.transform : bones[bone.Parent], false);
                rest[i] = pivot - (bone.Parent < 0 ? Vector3.zero : Vector(info.Bones[bone.Parent].Position));
                bones[i].localPosition = rest[i];
                bindposes[i] = Matrix4x4.Translate(-pivot);
            }
            var vertices = new Vector3[pack.Vertices.Length];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = pack.Vertices[i];
                vertices[i] = new Vector3(v.X, v.Y, v.Z);
                normals[i] = new Vector3(v.Nx, v.Ny, v.Nz);
                uv[i] = new Vector2(v.U, v.V);
                weights[i] = new BoneWeight
                {
                    boneIndex0 = v.B0, boneIndex1 = v.B1, boneIndex2 = v.B2, boneIndex3 = v.B3,
                    weight0 = v.W0, weight1 = v.W1, weight2 = v.W2, weight3 = v.W3
                };
            }
            mesh = new Mesh { name = info.Name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            mesh.triangles = pack.Indices;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                { name = info.Name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, pack.Texture, true))
                throw new InvalidOperationException("Cannot decode model texture.");
            material = new Material(shader) { name = info.Name, mainTexture = texture, color = Color.white };
            material.SetFloat("_Metallic", info.Material.Metallic);
            material.SetFloat("_Glossiness", info.Material.Smoothness);
            if (!info.Material.SpecularHighlights)
            {
                material.SetFloat("_SpecularHighlights", 0);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            if (info.Material.Emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", texture);
                float e = info.Material.Emission;
                material.SetVector("_EmissionColor", new Vector4(e, e, e, 1));
            }
            renderer = Root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.bones = bones;
            renderer.rootBone = Root.transform;
            renderer.quality = SkinQuality.Bone4;
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            var bounds = mesh.bounds;
            bounds.Expand(info.BoundsPadding * 2);
            renderer.localBounds = bounds;
            Animate(Motion.Sample(0, 0, 0, 0));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static Vector3 Vector(float[] v) => new Vector3(v[0], v[1], v[2]);

    public void UpdateLighting()
    {
        // DSP supplies ambient colors to its own shaders; Standard needs a Unity light probe.
        var ambient = (Shader.GetGlobalColor(Ambient0) + Shader.GetGlobalColor(Ambient1)
            + Shader.GetGlobalColor(Ambient2)) / 3f;
        ambient.r = Mathf.Max(0.12f, ambient.r);
        ambient.g = Mathf.Max(0.12f, ambient.g);
        ambient.b = Mathf.Max(0.12f, ambient.b);
        probe[0] = RenderSettings.ambientProbe;
        probe[0].AddAmbientLight(ambient);
        lighting.CopySHCoefficientArraysFrom(probe);
        renderer.SetPropertyBlock(lighting);
    }

    public void Animate(Motion state)
    {
        Array.Clear(positions, 0, positions.Length);
        Array.Clear(rotations, 0, rotations.Length);
        foreach (var motion in info.Motions)
        {
            float weight = state.Get(motion.Kind);
            positions[motion.Index] += Vector(motion.Position) * weight;
            rotations[motion.Index] += Vector(motion.Rotation) * weight;
        }
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i].localPosition = rest[i] + positions[i];
            bones[i].localRotation = Quaternion.Euler(rotations[i]);
        }
        Root.transform.localPosition = Vector(info.Offset) + positions[bones.Length];
        Root.transform.localRotation = Quaternion.Euler(Vector(info.Rotation)) * Quaternion.Euler(rotations[bones.Length]);
    }

    public void Dispose()
    {
        if (Root != null) { Root.SetActive(false); Object.Destroy(Root); }
        if (mesh != null) Object.Destroy(mesh);
        if (material != null) Object.Destroy(material);
        if (texture != null) Object.Destroy(texture);
    }
}
