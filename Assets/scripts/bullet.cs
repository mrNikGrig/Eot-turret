using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("Bullet")]
    public float damage = 10f;
    public float lifeTime = 5f;
    
    [Header("Physics")]
    public bool useGravity = true;

    [Header("Visuals")]
    public bool useTrail = true;
    public float trailDuration = 1f;
    public Color trailColor = Color.yellow;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        if (rb != null && useGravity)
        {
            rb.useGravity = true;
        }

        if (useTrail)
        {
            SetupTrail();
        }
        
        Destroy(gameObject, lifeTime);
    }

    void SetupTrail()
    {
        TrailRenderer tr = GetComponent<TrailRenderer>();
        if (tr == null)
        {
            tr = gameObject.AddComponent<TrailRenderer>();
        }

        tr.time = trailDuration;
        
        tr.startWidth = 0.05f;
        tr.endWidth = 0f;
        
        tr.material = new Material(Shader.Find("Sprites/Default"));
        
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(trailColor, 0.0f), new GradientColorKey(Color.red, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        tr.colorGradient = gradient;
    }

    void OnCollisionEnter(Collision collision)
    {
        SoldierHealth health = collision.collider.GetComponentInParent<SoldierHealth>();

        if (health != null)
        {
            health.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}