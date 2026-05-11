using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("Bullet")]
    public float damage = 10f;
    public float lifeTime = 5f;
    
    [Header("Physics")]
    public bool useGravity = true; // Включить физику гравитации

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        if (rb != null && useGravity)
        {
            rb.useGravity = true;
        }
        
        Destroy(gameObject, lifeTime);
        Debug.Log($"[{name}] Bullet spawned with gravity: {useGravity}");
    }

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[{name}] Hit: {collision.collider.name}");

        SoldierHealth health = collision.collider.GetComponentInParent<SoldierHealth>();

        if (health != null)
        {
            Debug.Log($"[{name}] SoldierHealth found on {health.name}. Applying damage.");
            health.TakeDamage(damage);
        }
        else
        {
            Debug.Log($"[{name}] No SoldierHealth found in parent chain.");
        }

        Destroy(gameObject);
    }
}