using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;
using Random = UnityEngine.Random;
using Unity.Collections;

public class AsteroidBelt : MonoBehaviour
{
    [Header("Spawner Settings")]
    [SerializeField] private GameObject _asteroidPrefab;
    [SerializeField, Min(1)] private int _density = 50;
    [SerializeField] private int _seed;
    [SerializeField, Min(0)] private float _innerRadius = 25;
    [SerializeField, Min(0)] private float _outerRadius = 25;
    [SerializeField, Min(0)] private float _height = 5;
    [SerializeField] private bool _rotatingClockwise = true;

    [Header("Asteroid Settings")]
    [SerializeField, Min(0)] private float _minOrbitSpeed = 1;
    [SerializeField, Min(0)] private float _maxOrbitSpeed = 1.5f;
    [SerializeField, Min(0)] private float _minRotationSpeed = 1;
    [SerializeField, Min(0)] private float _maxRotationSpeed = 1;

    private Transform[] _transforms;
    private NativeArray<Asteroid> _asteroids;

    private struct Asteroid
    {
        public float3 position;
        public quaternion rotation;
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    private struct AsteroidRotateJob : IJobFor
    {
        [ReadOnly] public float3 parentPosition;
        [ReadOnly] public float3 parentDirectionUp;
        [ReadOnly] public float orbitSpeed;
        [ReadOnly] public float rotationSpeed;
        [ReadOnly] public float3 rotationDirection;
        [ReadOnly] public bool rotationClockwise;
        [ReadOnly] public float deltaTime;

        public NativeArray<Asteroid> asteroids;

        public void Execute(int index)
        {
            var asteroid = asteroids[index];

            if (rotationClockwise)
            {
                asteroid.position = 
                    math.mul(
                            quaternion.AxisAngle(parentDirectionUp, orbitSpeed * deltaTime),
                            asteroid.position - parentPosition) + parentPosition;
            }
            else
            {
                asteroid.position =
                    math.mul(
                            quaternion.AxisAngle(-parentDirectionUp, orbitSpeed * deltaTime),
                            asteroid.position - parentPosition) + parentPosition;
            }

            asteroid.rotation = 
                math.mul(
                    asteroid.rotation, 
                    quaternion.AxisAngle(rotationDirection, rotationSpeed * deltaTime));

            asteroids[index] = asteroid;
        }
    }

    private void Start()
    {
        _transforms = new Transform[_density];
        var asteroids = new Asteroid[_density];

        Random.InitState(_seed);

        for (int i = 0; i < _density; i++)
        {
            var randomRadius = Random.Range(_innerRadius, _outerRadius);
            var randomRadian = Random.Range(0, 2 * Mathf.PI);

            var y = Random.Range(-_height / 2, _height / 2);
            var x = randomRadius * Mathf.Cos(randomRadian);
            var z = randomRadius * Mathf.Sin(randomRadian);
            
            var localPosition = new Vector3(x, y, z);
            var worldOffset = transform.rotation * localPosition;
            var worldPosition = transform.position + worldOffset;

            var asteroid = Instantiate(
                _asteroidPrefab, 
                worldPosition, 
                Quaternion.Euler(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360)));
            asteroid.transform.SetParent(transform);

            _transforms[i] = asteroid.transform;
            asteroids[i].position = asteroid.transform.position;
            asteroids[i].rotation = asteroid.transform.rotation;
        }

        _asteroids = new NativeArray<Asteroid>(asteroids, Allocator.Persistent);
    }

    private void Update()
    {
        var jobHandle = default(JobHandle);

        var job = new AsteroidRotateJob()
        {
            parentPosition = transform.position,
            parentDirectionUp = transform.up,
            orbitSpeed = Random.Range(_minOrbitSpeed, _maxOrbitSpeed),
            rotationSpeed = Random.Range(_minRotationSpeed, _maxRotationSpeed),
            rotationDirection = new(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360)),
            rotationClockwise = _rotatingClockwise,
            deltaTime = Time.deltaTime,
            asteroids = _asteroids
        };

        jobHandle = job.ScheduleParallel(_density, 5, jobHandle);
        jobHandle.Complete();


        for (int i = 0; i < _density; i++)
        {
            _transforms[i].SetPositionAndRotation(_asteroids[i].position, _asteroids[i].rotation);
        }
    }

    private void OnDisable()
    {
        _asteroids.Dispose();
    }
}