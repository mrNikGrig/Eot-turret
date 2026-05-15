using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(RealYoloVision))]
public class TurretShooter : MonoBehaviour
{
    [Header("Telemetry Radar (Fallback)")]
    public float radarDetectionRange = 25f;

    [Header("Shooting")]
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletSpeed = 25f;
    public float fireRate = 4f;
    public float muzzleForwardOffset = 0.5f;

    [Header("Leading")]
    public bool useLeading = true;
    public float leadingAccuracy = 1f;

    [Header("Turret Parts")]
    public Transform baseTransform;
    public Transform headTransform;
    public float minHeadAngle = -10f;
    public float maxHeadAngle = 45f;

    [Header("Rotation Physics")]
    public float maxYawSpeed = 120f;
    public float maxPitchSpeed = 90f;
    public float yawAcceleration = 300f;
    public float pitchAcceleration = 250f;
    public float yawDamping = 8f;
    public float pitchDamping = 8f;

    private RealYoloVision yoloVision;
    private float fireTimer;

    private Transform lockedTarget;
    private Vector3 lockedAimPoint;
    private Vector3 lockedVelocity;

    private Dictionary<Transform, Vector3> velocityHistory = new Dictionary<Transform, Vector3>();
    private Transform radarTarget;

    private float currentYaw;
    private float currentPitch;
    private float yawVelocity;
    private float pitchVelocity;

    private Collider[] turretColliders;

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

        Vector3 startEuler = headTransform.localEulerAngles;
        currentYaw = NormalizeAngle(startEuler.y);
        currentPitch = NormalizeAngle(startEuler.x);

        turretColliders = GetComponentsInChildren<Collider>();
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
                fireTimer = 1f / fireRate;
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
            lockedAimPoint = GetTargetCenter(lockedTarget);
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
            lockedAimPoint = GetTargetCenter(radarTarget);
            UpdateVelocity(lockedTarget, lockedAimPoint);
        }
        else
        {
            lockedTarget = null;
        }
    }

    Vector3 GetTargetCenter(Transform target)
    {
        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
            return rend.bounds.center;

        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null)
            return col.bounds.center;

        return target.position;
    }

    void UpdateVelocity(Transform target, Vector3 currentPos)
    {
        if (velocityHistory.TryGetValue(target, out Vector3 prevPos))
            lockedVelocity = (currentPos - prevPos) / Mathf.Max(Time.deltaTime, 0.0001f);
        else
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            lockedVelocity = rb != null ? rb.linearVelocity : Vector3.zero;
        }

        velocityHistory[target] = currentPos;
    }

    void AimHead()
    {
        Vector3 predicted = ComputePredictedCoordinates();
        ApplyHeadRotation(predicted);
    }

    Vector3 ComputePredictedCoordinates()
    {
        if (!useLeading) return lockedAimPoint;

        Vector3 predicted = lockedAimPoint;

        for (int i = 0; i < 5; i++)
        {
            float tof = Vector3.Distance(firePoint.position, predicted) / bulletSpeed;
            predicted = lockedAimPoint + lockedVelocity * (tof * leadingAccuracy);
        }

        return predicted;
    }

    void ApplyHeadRotation(Vector3 targetPosition)
    {
        Vector3 aimVector = (targetPosition - firePoint.position).normalized;
        Vector3 localAim = baseTransform.InverseTransformDirection(aimVector);

        float targetYaw = Mathf.Atan2(localAim.x, localAim.z) * Mathf.Rad2Deg;
        float pitchDist = Mathf.Sqrt(localAim.x * localAim.x + localAim.z * localAim.z);
        float targetPitch = -Mathf.Atan2(localAim.y, pitchDist) * Mathf.Rad2Deg;
        targetPitch = Mathf.Clamp(targetPitch, minHeadAngle, maxHeadAngle);

        float yawError = Mathf.DeltaAngle(currentYaw, targetYaw);
        float desiredYawVelocity = Mathf.Clamp(yawError * yawDamping, -maxYawSpeed, maxYawSpeed);
        yawVelocity = Mathf.MoveTowards(yawVelocity, desiredYawVelocity, yawAcceleration * Time.deltaTime);
        currentYaw += yawVelocity * Time.deltaTime;

        float pitchError = Mathf.DeltaAngle(currentPitch, targetPitch);
        float desiredPitchVelocity = Mathf.Clamp(pitchError * pitchDamping, -maxPitchSpeed, maxPitchSpeed);
        pitchVelocity = Mathf.MoveTowards(pitchVelocity, desiredPitchVelocity, pitchAcceleration * Time.deltaTime);
        currentPitch += pitchVelocity * Time.deltaTime;
        currentPitch = Mathf.Clamp(currentPitch, minHeadAngle, maxHeadAngle);

        headTransform.localRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    void ResetHeadTracking()
    {
        float yawError = Mathf.DeltaAngle(currentYaw, 0f);
        float desiredYawVelocity = Mathf.Clamp(yawError * yawDamping, -maxYawSpeed, maxYawSpeed);
        yawVelocity = Mathf.MoveTowards(yawVelocity, desiredYawVelocity, yawAcceleration * Time.deltaTime);
        currentYaw += yawVelocity * Time.deltaTime;

        float pitchError = Mathf.DeltaAngle(currentPitch, 0f);
        float desiredPitchVelocity = Mathf.Clamp(pitchError * pitchDamping, -maxPitchSpeed, maxPitchSpeed);
        pitchVelocity = Mathf.MoveTowards(pitchVelocity, desiredPitchVelocity, pitchAcceleration * Time.deltaTime);
        currentPitch += pitchVelocity * Time.deltaTime;
        currentPitch = Mathf.Clamp(currentPitch, minHeadAngle, maxHeadAngle);

        headTransform.localRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    void ExecuteFire()
    {
        if (bulletPrefab == null || firePoint == null || lockedTarget == null) return;

        Vector3 spawnPos = firePoint.position + firePoint.forward * muzzleForwardOffset;
        Quaternion spawnRot = firePoint.rotation;

        GameObject projectile = Instantiate(bulletPrefab, spawnPos, spawnRot);
        Rigidbody rb = projectile.GetComponent<Rigidbody>();

        Collider projCol = projectile.GetComponent<Collider>();
        if (projCol != null)
        {
            foreach (var col in turretColliders)
                Physics.IgnoreCollision(projCol, col);
        }

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = firePoint.forward * bulletSpeed;
        }
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}