using UnityEngine;

public class TurretShooter : MonoBehaviour
{
    [Header("Targeting")]
    public string targetTag = "Enemy";
    public float detectionRange = 30f;
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
    private float noiseOffsetX;
    private float noiseOffsetY;
    
    [Header("Turret Parts")]
    public Transform baseTransform;
    public Transform headTransform;
    public float headRotationSpeed = 3f;
    public float minHeadAngle = -10f;
    public float maxHeadAngle = 45f;

    private float fireTimer;
    private Transform currentTarget;
    private Rigidbody targetRigidbody;
    private Vector3 lastTargetPosition;
    private Vector3 estimatedVelocity;

    void Start()
    {
        if (baseTransform == null)
            baseTransform = transform;
        
        if (headTransform == null)
            headTransform = transform.Find("Head");
        
        if (firePoint == null)
        {
            Transform muzzle = transform.Find("Head/Muzzle");
            if (muzzle == null)
                muzzle = transform.Find("Head/Head_Muzzle");
            firePoint = muzzle;
        }

        if (headTransform == null)
            Debug.LogError("[TurretShooter] Head transform not found!");
        
        if (firePoint == null)
            Debug.LogError("[TurretShooter] FirePoint (Muzzle) not found!");

        noiseOffsetX = Random.Range(0f, 100f);
        noiseOffsetY = Random.Range(0f, 100f);
    }

    void Update()
    {
        FindTarget();

        if (currentTarget != null)
        {
            AimAtTarget();

            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                Fire();
                fireTimer = fireCooldown;
            }
        }
        else
        {
            ReturnHeadToNeutral();
        }
    }

    void FindTarget()
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag(targetTag);

        float bestDistance = Mathf.Infinity;
        Transform bestTarget = null;

        foreach (GameObject target in targets)
        {
            if (target == null) continue;

            SoldierHealth health = target.GetComponent<SoldierHealth>();
            if (health != null && health.IsDead())
                continue;

            float dist = Vector3.Distance(baseTransform.position, target.transform.position);
            if (dist < detectionRange && dist < bestDistance)
            {
                bestDistance = dist;
                bestTarget = target.transform;
            }
        }

        if (currentTarget != bestTarget)
        {
            lastTargetPosition = bestTarget != null ? bestTarget.position : Vector3.zero;
            estimatedVelocity = Vector3.zero;
        }

        currentTarget = bestTarget;
        
        if (currentTarget != null)
        {
            targetRigidbody = currentTarget.GetComponent<Rigidbody>();
            
            if (targetRigidbody == null && Time.deltaTime > 0)
            {
                estimatedVelocity = (currentTarget.position - lastTargetPosition) / Time.deltaTime;
                lastTargetPosition = currentTarget.position;
            }
        }
        else
        {
            targetRigidbody = null;
        }
    }

    void AimAtTarget()
    {
        if (currentTarget == null || firePoint == null || headTransform == null) return;

        Vector3 targetPosition = CalculateAimPoint();
        RotateHead(targetPosition);
    }

    Vector3 CalculateAimPoint()
    {
        Vector3 targetPosition = currentTarget.position + Vector3.up * targetHeightOffset;

        if (useLeading)
        {
            Vector3 targetVelocity = Vector3.zero;

            if (targetRigidbody != null)
            {
                targetVelocity = targetRigidbody.linearVelocity;
            }
            else
            {
                targetVelocity = estimatedVelocity;
            }

            Vector3 toTarget = targetPosition - firePoint.position;
            float distance = toTarget.magnitude;
            float timeToTarget = distance / bulletSpeed;

            Vector3 leadOffset = targetVelocity * timeToTarget * leadingAccuracy;
            targetPosition += leadOffset;

            if (Application.isEditor)
            {
                Debug.DrawLine(currentTarget.position, targetPosition, Color.yellow);
            }
        }

        return targetPosition;
    }

    void RotateHead(Vector3 targetPosition)
    {
        Vector3 fireDirection;

        if (useBallisticCalculation)
        {
            fireDirection = BallisticsCalculator.CalculateTrajectory(
                firePoint.position,
                targetPosition,
                bulletSpeed,
                Physics.gravity.y,
                useHighArc
            );

            if (fireDirection == Vector3.zero)
            {
                fireDirection = (targetPosition - firePoint.position).normalized;
            }
        }
        else
        {
            fireDirection = (targetPosition - firePoint.position).normalized;
        }

        Vector3 localDirection = baseTransform.InverseTransformDirection(fireDirection);
        
        float targetYaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float distanceXZ = Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z);
        float targetPitch = -Mathf.Atan2(localDirection.y, distanceXZ) * Mathf.Rad2Deg;
        
        targetYaw += GetHorizontalNoise();
        targetPitch += GetVerticalNoise();
        
        targetPitch = Mathf.Clamp(targetPitch, minHeadAngle, maxHeadAngle);

        Quaternion targetLocalRotation = Quaternion.Euler(targetPitch, targetYaw, 0f);
        
        headTransform.localRotation = Quaternion.Slerp(
            headTransform.localRotation, 
            targetLocalRotation, 
            headRotationSpeed * Time.deltaTime
        );
    }

    float GetHorizontalNoise()
    {
        if (horizontalSpread <= 0f) return 0f;

        if (usePerlinNoise)
        {
            noiseOffsetX += Time.deltaTime * 0.5f;
            float noise = Mathf.PerlinNoise(noiseOffsetX, 0f) * 2f - 1f;
            return noise * horizontalSpread;
        }
        else
        {
            return Random.Range(-horizontalSpread, horizontalSpread);
        }
    }

    float GetVerticalNoise()
    {
        if (verticalSpread <= 0f) return 0f;

        if (usePerlinNoise)
        {
            noiseOffsetY += Time.deltaTime * 0.5f;
            float noise = Mathf.PerlinNoise(noiseOffsetY, 0f) * 2f - 1f;
            return noise * verticalSpread;
        }
        else
        {
            return Random.Range(-verticalSpread, verticalSpread);
        }
    }

    void ReturnHeadToNeutral()
    {
        if (headTransform == null) return;

        headTransform.localRotation = Quaternion.Slerp(
            headTransform.localRotation, 
            Quaternion.identity, 
            headRotationSpeed * Time.deltaTime * 0.5f
        );
    }

    void Fire()
    {
        if (bulletPrefab == null || firePoint == null || currentTarget == null)
            return;

        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Rigidbody rb = bullet.GetComponent<Rigidbody>();

        if (rb != null)
        {
            Vector3 targetPosition = CalculateAimPoint();
            Vector3 fireDirection;

            if (useBallisticCalculation)
            {
                fireDirection = BallisticsCalculator.CalculateTrajectory(
                    firePoint.position,
                    targetPosition,
                    bulletSpeed,
                    Physics.gravity.y,
                    useHighArc
                );

                if (fireDirection == Vector3.zero)
                {
                    fireDirection = (targetPosition - firePoint.position).normalized;
                }
            }
            else
            {
                fireDirection = (targetPosition - firePoint.position).normalized;
            }

            fireDirection = ApplyFinalSpread(fireDirection);

            rb.linearVelocity = fireDirection * bulletSpeed;
            
            Debug.Log($"Turret fired at {currentTarget.name}");
        }
    }

    Vector3 ApplyFinalSpread(Vector3 direction)
    {
        float spreadH = Random.Range(-horizontalSpread * 0.5f, horizontalSpread * 0.5f);
        float spreadV = Random.Range(-verticalSpread * 0.5f, verticalSpread * 0.5f);

        Quaternion spread = Quaternion.Euler(spreadV, spreadH, 0f);
        return spread * direction;
    }

    void OnDrawGizmosSelected()
    {
        if (baseTransform == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(baseTransform.position, detectionRange);

        if (currentTarget != null && firePoint != null)
        {
            Vector3 baseTargetPos = currentTarget.position + Vector3.up * targetHeightOffset;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(baseTargetPos, 0.2f);

            Vector3 leadTargetPos = CalculateAimPoint();
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(leadTargetPos, 0.3f);
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(baseTargetPos, leadTargetPos);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(firePoint.position, leadTargetPos);

            if (useBallisticCalculation && Application.isPlaying)
            {
                Vector3 fireDir = BallisticsCalculator.CalculateTrajectory(
                    firePoint.position, 
                    leadTargetPos, 
                    bulletSpeed, 
                    Physics.gravity.y, 
                    useHighArc
                );

                if (fireDir != Vector3.zero)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawRay(firePoint.position, fireDir * 10f);
                }
            }
        }

        if (headTransform != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(headTransform.position, headTransform.forward * 3f);
        }

        if (firePoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawRay(firePoint.position, firePoint.forward * 2f);
        }
    }
}