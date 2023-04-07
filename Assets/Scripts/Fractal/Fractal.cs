using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;
using MathQuaternion = Unity.Mathematics.quaternion;

public class Fractal : MonoBehaviour
{
    [SerializeField, Range(1, 8)] private int _depth = 4;
    [SerializeField] private Mesh _mesh;
    [SerializeField] private Material _material;

    private static readonly int _matricesId = Shader.PropertyToID("_Matrices");

    private static readonly float3[] _directions = { up(), right(), left(), forward(), back() };

    private static readonly MathQuaternion[] _rotations = {
        MathQuaternion.identity,
        MathQuaternion.RotateZ(-0.5f * PI), MathQuaternion.RotateZ(0.5f * PI),
        MathQuaternion.RotateX(0.5f * PI), MathQuaternion.RotateX(-0.5f * PI)
    };

    private static MaterialPropertyBlock _propertyBlock;

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    private struct UpdateFractalLevelJob : IJobFor
    {
        public float spinAngleDelta;
        public float scale;

        [ReadOnly] public NativeArray<FractalPart> parents;
        [WriteOnly] public NativeArray<float3x4> matrices;
        public NativeArray<FractalPart> parts;

        public void Execute(int i)
        {
            FractalPart parent = parents[i / 5];
            FractalPart part = parts[i];
            part.spinAngle += spinAngleDelta;
            part.worldRotation = mul(parent.worldRotation,
                mul(part.rotation, MathQuaternion.RotateY(part.spinAngle))
            );
            part.worldPosition =
                parent.worldPosition +
                mul(parent.worldRotation, 1.5f * scale * part.direction);
            parts[i] = part;

            float3x3 r = float3x3(part.worldRotation) * scale;
            matrices[i] = float3x4(r.c0, r.c1, r.c2, part.worldPosition);
        }
    }

    private struct FractalPart
    {
        public float3 direction, worldPosition;
        public MathQuaternion rotation, worldRotation;
        public float spinAngle;
    }

    private NativeArray<FractalPart>[] _parts;
    private NativeArray<float3x4>[] _matrices;
    private ComputeBuffer[] _matricesBuffers;

    private void OnValidate()
    {
        if (_parts != null && enabled)
        {
            OnDisable();
            OnEnable();
        }
    }

    private void OnEnable()
    {
        _parts = new NativeArray<FractalPart>[_depth];
        _matrices = new NativeArray<float3x4>[_depth];
        _matricesBuffers = new ComputeBuffer[_depth];
        var stride = 12 * 4;

        for (int i = 0, length = 1; i < _parts.Length; i++, length *= 5)
        {
            _parts[i] = new(length, Allocator.Persistent);
            _matrices[i] = new(length, Allocator.Persistent);
            _matricesBuffers[i] = new(length, stride);
        }

        _parts[0][0] = CreatePart(0);

        for (int li = 1; li < _parts.Length; li++)
        {
            var levelParts = _parts[li];

            for (int fpi = 0; fpi < levelParts.Length; fpi += 5)
            {
                for (int ci = 0; ci < 5; ci++)
                {
                    levelParts[fpi + ci] = CreatePart(ci);
                }
            }
        }

        _propertyBlock ??= new();
    }

    private void Update()
    {
        var spinAngleDelta = 0.125f * PI * Time.deltaTime;
        var rootPart = _parts[0][0];
        rootPart.spinAngle += spinAngleDelta;
        rootPart.worldRotation = mul(
            transform.rotation,
            mul(rootPart.rotation, MathQuaternion.RotateY(rootPart.spinAngle))
        );
        rootPart.worldPosition = transform.position;
        _parts[0][0] = rootPart;
        var objectScale = transform.lossyScale.x;
        var r = float3x3(rootPart.worldRotation) * objectScale;
        _matrices[0][0] = float3x4(r.c0, r.c1, r.c2, rootPart.worldPosition);

        var scale = objectScale;
        var jobHandle = default(JobHandle);
        for (int li = 1; li < _parts.Length; li++)
        {
            scale *= 0.5f;
            jobHandle = new UpdateFractalLevelJob
            {
                spinAngleDelta = spinAngleDelta,
                scale = scale,
                parents = _parts[li - 1],
                parts = _parts[li],
                matrices = _matrices[li]
            }.ScheduleParallel(_parts[li].Length, 5, jobHandle);
        }
        jobHandle.Complete();

        var bounds = new Bounds(rootPart.worldPosition, 3f * objectScale * Vector3.one);
        for (int i = 0; i < _matricesBuffers.Length; i++)
        {
            var buffer = _matricesBuffers[i];
            buffer.SetData(_matrices[i]);
            _propertyBlock.SetBuffer(_matricesId, buffer);
            Graphics.DrawMeshInstancedProcedural(
                _mesh, 0, _material, bounds, buffer.count, _propertyBlock);
        }
    }

    private void OnDisable()
    {
        for (int i = 0; i < _matricesBuffers.Length; i++)
        {
            _matricesBuffers[i].Release();
            _parts[i].Dispose();
            _matrices[i].Dispose();
        }

        _parts = null;
        _matrices = null;
        _matricesBuffers = null;
    }

    private FractalPart CreatePart(int childIndex) => new()
    {
        direction = _directions[childIndex],
        rotation = _rotations[childIndex]
    };
}