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
    public float fireRate = 4f;
    public float muzzleForwardOffset = 0.5f;

    [Header("BB / Ballistics (mean parameters)")]
    public float muzzleEnergyJ = 1.2f;
    public float bbMassGrams = 0.25f;
    public float bbDiameterMm = 5.95f;

    [Header("Environment")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    public float airDensity = 1.225f;
    public Vector3 wind = Vector3.zero;

    [Header("Quadratic Drag")]
    public float dragCd = 0.47f;

    [Header("Hop-up / Magnus (mean)")]
    public float hopUpSpinRps = 150f;
    public float magnusLiftSlope = 1.2f;
    public float maxLiftCoefficient = 1.0f;
    public float spinDecayRate = 1.5f;

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

    [Header("Intercept solver")]
    public float interceptSolveHz = 10f;    // как часто пересчитывать решение
    public float simDt = 0.01f;             // шаг интегрирования в решателе
    public float maxSimTime = 3.0f;         // максимум времени полёта в расчёте
    public float hitRadius = 0.25f;         // радиус "попадания" в расчёте (м)
    public float coarseAngleStepDeg = 4.0f; // начальный шаг поиска
    public int refineIterations = 4;        // уточнения
    public float fireConeDeg = 1.5f;        // стрелять только если в этом конусе

    private RealYoloVision yoloVision;
    private float fireTimer;

    private Transform lockedTarget;
    private Vector3 lockedAimPoint;

    // kinematics state per target
    private readonly Dictionary<Transform, Vector3> lastPos = new();
    private readonly Dictionary<Transform, Vector3> lastVel = new();
    private readonly Dictionary<Transform, float> lastTime = new();

    private Vector3 lockedVelocity;
    private Vector3 lockedAcceleration;

    private Transform radarTarget;

    private float currentYaw;
    private float currentPitch;
    private float yawVelocity;
    private float pitchVelocity;

    private Collider[] turretColliders;

    // intercept result
    private float interceptTimer;
    private Vector3 desiredAimDirWorld = Vector3.forward;
    private float desiredTOF;
    private float desiredScore;

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
            interceptTimer -= Time.deltaTime;
            if (interceptTimer <= 0f)
            {
                SolveIntercept();
                interceptTimer = 1f / Mathf.Max(1f, interceptSolveHz);
            }

            AimHead(desiredAimDirWorld);

            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                if (IsAimedEnough())
                    ExecuteFire();

                fireTimer = 1f / fireRate;
            }
        }
        else
        {
            ResetHeadTracking();
        }
    }

    bool IsAimedEnough()
    {
        if (firePoint == null) return false;
        float angle = Vector3.Angle(firePoint.forward, desiredAimDirWorld);
        return angle <= fireConeDeg;
    }

    float MassKg => Mathf.Max(1e-6f, bbMassGrams / 1000f);
    float RadiusM => Mathf.Max(1e-6f, (bbDiameterMm / 1000f) * 0.5f);
    float Area => PointMassBallistics.CrossSectionArea(RadiusM);
    float MuzzleSpeed => PointMassBallistics.MuzzleSpeedFromEnergy(muzzleEnergyJ, MassKg);

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
            UpdateTargetKinematics(lockedTarget, lockedAimPoint);
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
            UpdateTargetKinematics(lockedTarget, lockedAimPoint);
        }
        else
        {
            lockedTarget = null;
        }
    }

    Vector3 GetTargetCenter(Transform target)
    {
        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds.center;

        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds.center;

        return target.position;
    }

    void UpdateTargetKinematics(Transform target, Vector3 currentPos)
    {
        float t = Time.time;

        if (lastTime.TryGetValue(target, out float tPrev))
        {
            float dt = Mathf.Max(1e-4f, t - tPrev);

            Vector3 v = (currentPos - lastPos[target]) / dt;

            if (lastVel.TryGetValue(target, out Vector3 vPrev))
                lockedAcceleration = (v - vPrev) / dt;
            else
                lockedAcceleration = Vector3.zero;

            lockedVelocity = v;
            lastVel[target] = v;
        }
        else
        {
            lockedVelocity = Vector3.zero;
            lockedAcceleration = Vector3.zero;
            lastVel[target] = Vector3.zero;
        }

        lastPos[target] = currentPos;
        lastTime[target] = t;
    }

    void AimHead(Vector3 aimDirWorld)
    {
        ApplyHeadRotationFromDirection(aimDirWorld);
    }

    void ApplyHeadRotationFromDirection(Vector3 aimDirWorld)
    {
        Vector3 localAim = baseTransform.InverseTransformDirection(aimDirWorld.normalized);

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


    void SolveIntercept()
    {
        if (firePoint == null)
        {
            desiredAimDirWorld = (lockedAimPoint - transform.position).normalized;
            return;
        }

        Vector3 firePos = firePoint.position;

        Vector3 dir0 = (lockedAimPoint - firePos).normalized;
        DirToYawPitch(dir0, out float yaw0, out float pitch0);
        pitch0 = Mathf.Clamp(pitch0, minHeadAngle, maxHeadAngle);

        float bestYaw = yaw0;
        float bestPitch = pitch0;
        desiredScore = float.PositiveInfinity;
        desiredTOF = 0f;

        float step = coarseAngleStepDeg;

        for (int iter = 0; iter < refineIterations; iter++)
        {
            for (int iy = -1; iy <= 1; iy++)
            for (int ip = -1; ip <= 1; ip++)
            {
                float yaw = bestYaw + iy * step;
                float pitch = Mathf.Clamp(bestPitch + ip * step, minHeadAngle, maxHeadAngle);

                Vector3 dir = YawPitchToWorldDir(yaw, pitch);

                float rotateDelay = EstimateRotateDelaySeconds(yaw, pitch);
                float score = SimulateMissDistance(dir, rotateDelay, out float tof);

                if (score < desiredScore)
                {
                    desiredScore = score;
                    desiredTOF = tof;
                    bestYaw = yaw;
                    bestPitch = pitch;
                }
            }

            step *= 0.5f;
        }

        desiredAimDirWorld = YawPitchToWorldDir(bestYaw, bestPitch).normalized;
    }

    float EstimateRotateDelaySeconds(float targetYaw, float targetPitch)
    {
        float dy = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
        float dp = Mathf.Abs(Mathf.DeltaAngle(currentPitch, targetPitch));

        float tYaw = dy / Mathf.Max(1f, maxYawSpeed);
        float tPitch = dp / Mathf.Max(1f, maxPitchSpeed);

        return Mathf.Max(tYaw, tPitch);
    }

    float SimulateMissDistance(Vector3 muzzleDirWorld, float startTimeOffset, out float bestTOF)
{
    BallisticModel.Params p = new BallisticModel.Params
    {
        massKg = MassKg,
        radiusM = RadiusM,
        area = BallisticModel.CrossSectionArea(RadiusM),

        gravity = gravity,
        airDensity = airDensity,
        wind = wind,

        dragCd = dragCd,

        magnusLiftSlope = magnusLiftSlope,
        maxLiftCoefficient = maxLiftCoefficient,

        spinDecayRate = spinDecayRate
    };

    BallisticModel.State s = new BallisticModel.State
    {
        pos = firePoint.position,
        vel = muzzleDirWorld.normalized * MuzzleSpeed,
        omega = BallisticModel.BackspinAxisForVelocity(muzzleDirWorld) * (hopUpSpinRps * 2f * Mathf.PI)
    };

    float bestDist = float.PositiveInfinity;
    bestTOF = 0f;

    float dt = simDt;

    for (float t = 0f; t <= maxSimTime; t += dt)
    {
        BallisticModel.Step(ref s, p, dt, Vector3.zero); // в решателе без wobble

        float shotDelay = Mathf.Max(0f, fireTimer);      // важно: цель едет до реального выстрела
        float tt = t + startTimeOffset + shotDelay;

        Vector3 targetPos = lockedAimPoint
                            + lockedVelocity * tt
                            + 0.5f * lockedAcceleration * tt * tt;

        float d = Vector3.Distance(s.pos, targetPos);
        if (d < bestDist)
        {
            bestDist = d;
            bestTOF = t;
            if (bestDist <= hitRadius) return bestDist;
        }
    }

    return bestDist;
}

    void DirToYawPitch(Vector3 dirWorld, out float yawDeg, out float pitchDeg)
    {
        Vector3 localAim = baseTransform.InverseTransformDirection(dirWorld.normalized);

        yawDeg = Mathf.Atan2(localAim.x, localAim.z) * Mathf.Rad2Deg;
        float pitchDist = Mathf.Sqrt(localAim.x * localAim.x + localAim.z * localAim.z);
        pitchDeg = -Mathf.Atan2(localAim.y, pitchDist) * Mathf.Rad2Deg;
    }

    Vector3 YawPitchToWorldDir(float yawDeg, float pitchDeg)
    {
        Vector3 localDir = Quaternion.Euler(pitchDeg, yawDeg, 0f) * Vector3.forward;
        return baseTransform.TransformDirection(localDir).normalized;
    }


    void ExecuteFire()
    {
        if (bulletPrefab == null || firePoint == null || lockedTarget == null) return;

        Vector3 spawnPos = firePoint.position + firePoint.forward * muzzleForwardOffset;
        Quaternion spawnRot = firePoint.rotation;

        GameObject projectile = Instantiate(bulletPrefab, spawnPos, spawnRot);

        Collider projCol = projectile.GetComponent<Collider>();
        if (projCol != null)
        {
            foreach (var col in turretColliders)
                Physics.IgnoreCollision(projCol, col);
        }

        Bullet b = projectile.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetIgnoreColliders(turretColliders);

            b.bbMassGrams = bbMassGrams;
            b.bbDiameterMm = bbDiameterMm;
            b.muzzleEnergyJ = muzzleEnergyJ;

            b.gravity = gravity;
            b.airDensity = airDensity;
            b.wind = wind;

            b.dragCd = dragCd;
            b.hopUpSpinRps = hopUpSpinRps;
            b.magnusLiftSlope = magnusLiftSlope;
            b.maxLiftCoefficient = maxLiftCoefficient;
            b.spinDecayRate = spinDecayRate;

            b.InitFromTurret(firePoint.forward);
        }
        else
        {
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // <!--citation:3-->
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = firePoint.forward * MuzzleSpeed;
#else
                rb.velocity = firePoint.forward * MuzzleSpeed;
#endif
            }
        }
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}