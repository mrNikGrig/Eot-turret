using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(RealYoloVision))]
public class TurretShooter : MonoBehaviour
{
    [Header("Telemetry Radar (Fallback)")]
    public float radarDetectionRange = 25f;
    public float targetHeightOffset = 1.0f;

    [Header("Shooting")]
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletSpeed = 25f;
    public float fireCooldown = 0.5f;

    [Header("Ballistics")]
    public bool useBallisticCalculation = true;
    public bool useHighArc = false;

    [Header("Leading")]
    public bool useLeading = true;
    public float leadingAccuracy = 1f;
    
    [Header("Inaccuracy")]
    [Range(0f, 10f)]
    public float horizontalSpread = 0.1f;
    [Range(0f, 10f)]
    public float verticalSpread = 0.1f;
    public bool usePerlinNoise = true;
    
    [Header("Turret Parts")]
    public Transform baseTransform;
    public Transform headTransform;
    public float headRotationSpeed = 3f;
    public float minHeadAngle = -10f;
    public float maxHeadAngle = 45f;

    private RealYoloVision yoloVision;
    private float fireTimer;
    
    private Transform lockedTarget;
    private Vector3 lockedAimPoint;
    private Vector3 lockedVelocity;
    
    private float noiseOffsetX;
    private float noiseOffsetY;
    
    private Dictionary<Transform, Vector3> velocityHistory = new Dictionary<Transform, Vector3>();
    private Transform radarTarget;

    void Start()
    {
        yoloVision = GetComponent<RealYoloVision>();
        
        if (baseTransform == null) baseTransform = transform;
        if (headTransform == null) headTransform = transform.Find("Head");
        
        if (firePoint == null)
        {
            Transform muzzle = transform.Find("Head/Muzzle") ?? transform.Find("Head/Head_Muzzle");
            firePoint = muzzle;
        }

        noiseOffsetX = Random.Range(0f, 100f);
        noiseOffsetY = Random.Range(0f, 100f);
    }

    void Update()
    {
        ProcessTargetAcquisition();

        if (lockedTarget != null)
        {
            AimHead();

            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                ExecuteFire();
                fireTimer = fireCooldown;
            }
        }
        else
        {
            ResetHeadTracking();
        }
    }

    void ProcessTargetAcquisition()
    {
        List<YoloDetection> detections = yoloVision.GetDetections();
        
        YoloDetection bestDetection = null;
        float bestConf = 0f;

        foreach (var det in detections)
        {
            if (det.classId == 0 && det.trackedTarget != null && det.confidence > bestConf)
            {
                SoldierHealth health = det.trackedTarget.GetComponent<SoldierHealth>();
                if (health == null || !health.IsDead())
                {
                    bestConf = det.confidence;
                    bestDetection = det;
                }
            }
        }

        if (bestDetection != null)
        {
            lockedTarget = bestDetection.trackedTarget;
            lockedAimPoint = lockedTarget.position + Vector3.up * targetHeightOffset;
            UpdateVelocity(lockedTarget, lockedAimPoint);
        }
        else
        {
            ActivateTelemetryFallback();
        }
    }

    void ActivateTelemetryFallback()
    {
        GameObject[] sceneTargets = GameObject.FindGameObjectsWithTag(yoloVision.targetTag);
        float closestDist = Mathf.Infinity;
        Transform optimalTarget = null;

        foreach (GameObject t in sceneTargets)
        {
            if (t == null) continue;
            
            SoldierHealth health = t.GetComponent<SoldierHealth>();
            if (health != null && health.IsDead()) continue;

            float distance = Vector3.Distance(baseTransform.position, t.transform.position);
            if (distance < radarDetectionRange && distance < closestDist)
            {
                closestDist = distance;
                optimalTarget = t.transform;
            }
        }

        radarTarget = optimalTarget;

        if (radarTarget != null)
        {
            lockedTarget = radarTarget;
            lockedAimPoint = radarTarget.position + Vector3.up * targetHeightOffset;
            UpdateVelocity(lockedTarget, lockedAimPoint);
        }
        else
        {
            lockedTarget = null;
        }
    }

    void UpdateVelocity(Transform target, Vector3 currentPos)
    {
        if (velocityHistory.TryGetValue(target, out Vector3 prevPos))
        {
            lockedVelocity = Time.deltaTime > 0 ? (currentPos - prevPos) / Time.deltaTime : Vector3.zero;
        }
        else
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            lockedVelocity = rb != null ? rb.linearVelocity : Vector3.zero;
        }
        
        velocityHistory[target] = currentPos;
    }

    void AimHead()
    {
        if (lockedTarget == null || firePoint == null || headTransform == null) return;

        Vector3 ballisticTarget = ComputePredictedCoordinates();
        ApplyHeadRotation(ballisticTarget);
    }

    Vector3 ComputePredictedCoordinates()
    {
        if (!useLeading) return lockedAimPoint;

        Vector3 predictedCoords = lockedAimPoint;

        for (int i = 0; i < 5; i++)
        {
            float tof = 0f;

            if (useBallisticCalculation)
            {
                Vector3 trajectory = BallisticsCalculator.CalculateTrajectory(
                    firePoint.position,
                    predictedCoords,
                    bulletSpeed,
                    Physics.gravity.y,
                    useHighArc
                );

                if (trajectory != Vector3.zero)
                {
                    Vector2 flatStart = new Vector2(firePoint.position.x, firePoint.position.z);
                    Vector2 flatEnd = new Vector2(predictedCoords.x, predictedCoords.z);
                    float range = Vector2.Distance(flatStart, flatEnd);
                    float velocityH = new Vector2(trajectory.x, trajectory.z).magnitude * bulletSpeed;

                    tof = velocityH > 0.001f ? range / velocityH : Vector3.Distance(firePoint.position, predictedCoords) / bulletSpeed;
                }
                else
                {
                    tof = Vector3.Distance(firePoint.position, predictedCoords) / bulletSpeed;
                }
            }
            else
            {
                tof = Vector3.Distance(firePoint.position, predictedCoords) / bulletSpeed;
            }

            predictedCoords = lockedAimPoint + lockedVelocity * (tof * leadingAccuracy);
        }

        return predictedCoords;
    }

    void ApplyHeadRotation(Vector3 targetPosition)
    {
        Vector3 aimVector;

        if (useBallisticCalculation)
        {
            aimVector = BallisticsCalculator.CalculateTrajectory(
                firePoint.position,
                targetPosition,
                bulletSpeed,
                Physics.gravity.y,
                useHighArc
            );

            if (aimVector == Vector3.zero)
                aimVector = (targetPosition - firePoint.position).normalized;
        }
        else
        {
            aimVector = (targetPosition - firePoint.position).normalized;
        }

        Vector3 localAim = baseTransform.InverseTransformDirection(aimVector);
        
        float yaw = Mathf.Atan2(localAim.x, localAim.z) * Mathf.Rad2Deg;
        float pitchDist = Mathf.Sqrt(localAim.x * localAim.x + localAim.z * localAim.z);
        float pitch = -Mathf.Atan2(localAim.y, pitchDist) * Mathf.Rad2Deg;
        
        yaw += EvaluateNoiseHorizontal();
        pitch += EvaluateNoiseVertical();
        
        pitch = Mathf.Clamp(pitch, minHeadAngle, maxHeadAngle);

        Quaternion targetQuaternion = Quaternion.Euler(pitch, yaw, 0f);
        
        headTransform.localRotation = Quaternion.Slerp(
            headTransform.localRotation, 
            targetQuaternion, 
            headRotationSpeed * Time.deltaTime
        );
    }

    float EvaluateNoiseHorizontal()
    {
        if (horizontalSpread <= 0f) return 0f;
        
        if (usePerlinNoise)
        {
            noiseOffsetX += Time.deltaTime * 0.5f;
            return (Mathf.PerlinNoise(noiseOffsetX, 0f) * 2f - 1f) * horizontalSpread;
        }
        return Random.Range(-horizontalSpread, horizontalSpread);
    }

    float EvaluateNoiseVertical()
    {
        if (verticalSpread <= 0f) return 0f;
        
        if (usePerlinNoise)
        {
            noiseOffsetY += Time.deltaTime * 0.5f;
            return (Mathf.PerlinNoise(noiseOffsetY, 0f) * 2f - 1f) * verticalSpread;
        }
        return Random.Range(-verticalSpread, verticalSpread);
    }

    void ResetHeadTracking()
    {
        if (headTransform == null) return;
        
        headTransform.localRotation = Quaternion.Slerp(
            headTransform.localRotation, 
            Quaternion.identity, 
            headRotationSpeed * Time.deltaTime * 0.5f
        );
    }

    void ExecuteFire()
    {
        if (bulletPrefab == null || firePoint == null || lockedTarget == null) return;

        GameObject projectile = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Rigidbody rb = projectile.GetComponent<Rigidbody>();

        if (rb != null)
        {
            Vector3 targetCoords = ComputePredictedCoordinates();
            Vector3 launchVector;

            if (useBallisticCalculation)
            {
                launchVector = BallisticsCalculator.CalculateTrajectory(
                    firePoint.position,
                    targetCoords,
                    bulletSpeed,
                    Physics.gravity.y,
                    useHighArc
                );

                if (launchVector == Vector3.zero)
                    launchVector = (targetCoords - firePoint.position).normalized;
            }
            else
            {
                launchVector = (targetCoords - firePoint.position).normalized;
            }

            launchVector = InjectFiringSpread(launchVector);
            rb.linearVelocity = launchVector * bulletSpeed;
        }
    }

    Vector3 InjectFiringSpread(Vector3 baseDirection)
    {
        float devH = Random.Range(-horizontalSpread * 0.5f, horizontalSpread * 0.5f);
        float devV = Random.Range(-verticalSpread * 0.5f, verticalSpread * 0.5f);
        return Quaternion.Euler(devV, devH, 0f) * baseDirection;
    }
}