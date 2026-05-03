using UnityEngine;

public class TurretShooter : MonoBehaviour
{
    [Header("Targeting")]
    public string targetTag = "Enemy";
    public float detectionRange = 30f;
    public float rotateSpeed = 5f;

    [Header("Shooting")]
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletSpeed = 25f;
    public float fireCooldown = 0.5f;

    private float fireTimer;
    private Transform currentTarget;

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
    }

    void FindTarget()
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag(targetTag);

        float bestDistance = Mathf.Infinity;
        Transform bestTarget = null;

        foreach (GameObject target in targets)
        {
            if (target == null) continue;

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist < detectionRange && dist < bestDistance)
            {
                bestDistance = dist;
                bestTarget = target.transform;
            }
        }

        currentTarget = bestTarget;
    }

    void AimAtTarget()
    {
        Vector3 dir = currentTarget.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
    }

    void Fire()
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("TurretShooter: bulletPrefab is not assigned.");
            return;
        }

        if (firePoint == null)
        {
            Debug.LogError("TurretShooter: firePoint is not assigned.");
            return;
        }

        if (currentTarget == null)
            return;

        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = (currentTarget.position - firePoint.position).normalized;
            rb.linearVelocity = dir * bulletSpeed;
            Debug.Log($"Turret fired bullet toward {currentTarget.name}");
        }
        else
        {
            Debug.LogError("Bullet prefab has no Rigidbody.");
        }
    }
}